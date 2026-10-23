#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/avutil.h>
#include <libavutil/mem.h>

typedef int (__cdecl *CueCallback)(int64_t, int64_t, const char *, int);
typedef int (__cdecl *RawCallback)(int64_t, int64_t, const char *, int, int);
typedef struct { ULONGLONG deadline; } Deadline;
static int interrupted(void *opaque) { return GetTickCount64() > ((Deadline *)opaque)->deadline; }

__declspec(dllexport) const char *__cdecl bili_ffmpeg_license(void) { return avcodec_license(); }
__declspec(dllexport) const char *__cdecl bili_ffmpeg_configuration(void) { return avcodec_configuration(); }

/* Only public FFmpeg APIs cross this bridge; managed code never copies ABI structs.
   The FFmpeg libraries remain separately replaceable LGPL shared libraries. */
static int extract_caption(const wchar_t *path, int ordinal,
    CueCallback emit, RawCallback raw, char *error, int error_size)
{
    AVFormatContext *format = NULL;
    AVCodecContext *decoder = NULL, *encoder = NULL;
    AVPacket *packet = NULL;
    AVDictionary *options = NULL;
    char *utf8 = NULL, *text = NULL;
    AVStream *stream = NULL;
    Deadline deadline = { GetTickCount64() + 30000 };
    int result = AVERROR(EINVAL), selected = -1, count = 0;
    int length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, path, -1, NULL, 0, NULL, NULL);
    if (!length || ordinal < 0 || (!emit && !raw)) goto done;
    utf8 = av_malloc(length);
    if (!utf8) { result = AVERROR(ENOMEM); goto done; }
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, path, -1, utf8, length, NULL, NULL);
    format = avformat_alloc_context();
    if (!format) { result = AVERROR(ENOMEM); goto done; }
    format->interrupt_callback.callback = interrupted;
    format->interrupt_callback.opaque = &deadline;
    av_dict_set(&options, "protocol_whitelist", "file", 0);
    av_dict_set(&options, "probesize", "4194304", 0);
    if ((result = avformat_open_input(&format, utf8, NULL, &options)) < 0) goto done;
    if ((result = avformat_find_stream_info(format, NULL)) < 0) goto done;
    for (unsigned i = 0; i < format->nb_streams; i++)
        if (format->streams[i]->codecpar->codec_type == AVMEDIA_TYPE_SUBTITLE && count++ == ordinal)
        { selected = i; stream = format->streams[i]; break; }
    if (!stream) { result = AVERROR_STREAM_NOT_FOUND; goto done; }
    const AVCodec *codec = avcodec_find_decoder(stream->codecpar->codec_id);
    if (!codec) { result = AVERROR_DECODER_NOT_FOUND; goto done; }
    decoder = avcodec_alloc_context3(codec);
    if (!decoder) { result = AVERROR(ENOMEM); goto done; }
    if ((result = avcodec_parameters_to_context(decoder, stream->codecpar)) < 0) goto done;
    decoder->pkt_timebase = stream->time_base;
    if ((result = avcodec_open2(decoder, codec, NULL)) < 0) goto done;
    if (raw && decoder->subtitle_header_size > 0 && raw(0, 0, (char *)decoder->subtitle_header, decoder->subtitle_header_size,
        1 | (stream->codecpar->codec_id == AV_CODEC_ID_ASS || stream->codecpar->codec_id == AV_CODEC_ID_SSA ? 8 : 0))) { result = AVERROR(ENOSPC); goto done; }
    codec = avcodec_find_encoder(AV_CODEC_ID_SUBRIP);
    if (!codec) { result = AVERROR_ENCODER_NOT_FOUND; goto done; }
    encoder = avcodec_alloc_context3(codec);
    if (!encoder) { result = AVERROR(ENOMEM); goto done; }
    encoder->time_base = (AVRational){1, 1000};
    if (decoder->subtitle_header_size > 0) {
        encoder->subtitle_header = av_mallocz(decoder->subtitle_header_size + AV_INPUT_BUFFER_PADDING_SIZE);
        if (!encoder->subtitle_header) { result = AVERROR(ENOMEM); goto done; }
        memcpy(encoder->subtitle_header, decoder->subtitle_header, decoder->subtitle_header_size);
        encoder->subtitle_header_size = decoder->subtitle_header_size;
    }
    if ((result = avcodec_open2(encoder, codec, NULL)) < 0) goto done;
    packet = av_packet_alloc(); text = av_malloc(1024 * 1024);
    if (!packet || !text) { result = AVERROR(ENOMEM); goto done; }
    while ((result = av_read_frame(format, packet)) >= 0) {
        if (packet->stream_index == selected) {
            AVSubtitle subtitle = {0}; int got = 0;
            result = avcodec_decode_subtitle2(decoder, &subtitle, &got, packet);
            if (result < 0) { avsubtitle_free(&subtitle); goto done; }
            if (got) {
                int64_t time = subtitle.pts == AV_NOPTS_VALUE ? 0 : subtitle.pts / 1000;
                if (format->start_time != AV_NOPTS_VALUE) time -= format->start_time / 1000;
                int64_t start = time + subtitle.start_display_time;
                int64_t end = time + subtitle.end_display_time;
                int inferred = end <= start;
                if (inferred) end = start + 2000;
                if (start < 0) start = 0;
                if (raw) {
                    for (unsigned i = 0; i < subtitle.num_rects; i++) {
                        const char *ass = subtitle.rects[i]->ass;
                        /* FFmpeg ASS rectangles contain ReadOrder,Layer,Style,...,Text.
                           Retain all fields after ReadOrder; managed code adds times. */
                        const char *body = ass ? strchr(ass, ',') : NULL;
                        if (body && raw(start,end,body+1,(int)strlen(body+1),2 | (inferred ? 4 : 0))) {
                            avsubtitle_free(&subtitle); result = AVERROR(ENOSPC); goto done;
                        }
                    }
                } else {
                    length = avcodec_encode_subtitle(encoder, (unsigned char *)text, 1024 * 1024, &subtitle);
                    if (length < 0) { avsubtitle_free(&subtitle); result = length; goto done; }
                    if (length > 0 && end > start && emit(start, end, text, length)) {
                        avsubtitle_free(&subtitle); result = AVERROR(ENOSPC); goto done;
                    }
                }
            }
            avsubtitle_free(&subtitle);
        }
        av_packet_unref(packet);
    }
    if (result == AVERROR_EOF) result = 0;
done:
    if (result < 0 && error && error_size > 0) av_strerror(result, error, error_size);
    av_dict_free(&options); av_packet_free(&packet);
    avcodec_free_context(&decoder); avcodec_free_context(&encoder);
    avformat_close_input(&format); av_free(utf8); av_free(text);
    return result < 0 ? result : 0;
}

__declspec(dllexport) int __cdecl bili_extract_caption(const wchar_t *path, int ordinal,
    CueCallback emit, char *error, int error_size)
{ return extract_caption(path,ordinal,emit,NULL,error,error_size); }

__declspec(dllexport) int __cdecl bili_extract_caption_raw(const wchar_t *path, int ordinal,
    RawCallback emit, char *error, int error_size)
{ return extract_caption(path,ordinal,NULL,emit,error,error_size); }
