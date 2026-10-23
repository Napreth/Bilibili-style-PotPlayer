using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
namespace BilibiliPotPlayer
{
    internal static class NativeTimedText
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int CueCallback(long start, long end, IntPtr utf8, int bytes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int RawCallback(long start, long end, IntPtr utf8, int bytes, int flags);
        [DllImport("BilibiliTimedText.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        static extern int bili_extract_caption_raw(string path, int ordinal, RawCallback callback, byte[] error, int errorSize);
        [DllImport("BilibiliTimedText.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        static extern int bili_extract_caption(string path, int ordinal, CueCallback callback, byte[] error, int errorSize);
        [DllImport("BilibiliTimedText.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr bili_ffmpeg_license();
        [DllImport("BilibiliTimedText.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr bili_ffmpeg_configuration();
        internal static string License { get { return Marshal.PtrToStringAnsi(bili_ffmpeg_license()); } }
        internal static string Configuration { get { return Marshal.PtrToStringAnsi(bili_ffmpeg_configuration()); } }
        internal static List<TextSource> Read(string path, int ordinal, string title)
        {
            if (ordinal < 0) throw new ArgumentOutOfRangeException("ordinal");
            path = Path.GetFullPath(path); if (!File.Exists(path)) throw new FileNotFoundException("媒体文件不存在",path);
            StringBuilder header = new StringBuilder(), events = new StringBuilder(); int count = 0, total = 0;
            List<bool> inferred = new List<bool>(); Exception callbackError = null; bool originalAss = false;
            RawCallback callback = delegate(long start, long end, IntPtr text, int bytes, int flags) {
                try {
                    if (bytes < 0 || bytes > TimedText.MaxBytes || (total += bytes) > TimedText.MaxBytes || (flags & 2) != 0 && ++count > TimedText.MaxCues) throw new InvalidDataException("字幕内容超过大小限制");
                    byte[] buffer = new byte[bytes]; Marshal.Copy(text,buffer,0,bytes); string value = Encoding.UTF8.GetString(buffer).TrimEnd('\0');
                    if ((flags & 1) != 0) { header.Append(value); originalAss = (flags & 8) != 0; }
                    else {
                        int comma = value.IndexOf(','); if (comma < 0) throw new InvalidDataException("ASS 事件字段缺失");
                        events.Append("Dialogue: ").Append(value.Substring(0,comma)).Append(',').Append(AssTime(start)).Append(',').Append(AssTime(end)).Append(',').Append(value.Substring(comma+1)).Append('\n');
                        inferred.Add((flags & 4) != 0);
                    }
                    return 0;
                } catch (Exception error) { callbackError = error; return 1; }
            };
            byte[] diagnostics = new byte[1024]; int result = bili_extract_caption_raw(path,ordinal,callback,diagnostics,diagnostics.Length); GC.KeepAlive(callback);
            if (callbackError != null) throw new InvalidDataException("字幕元数据读取失败",callbackError);
            if (result < 0) throw new InvalidOperationException("字幕读取失败，可重试："+Encoding.UTF8.GetString(diagnostics).TrimEnd('\0'));
            if (header.Length == 0) throw new InvalidDataException("字幕缺少可分析的样式头");
            // Mark inferred end times before classification rather than treating fallback
            // durations as measurements supplied by the container.
            TextSource source = new TextSource { Title = title, Format = "容器文本 / ASS", Loaded = true };
            AssEvents.Parse(header.ToString()+"\n"+events,source);
            for (int i = 0; i < source.Events.Count && i < inferred.Count; i++) {
                source.Events[i].DurationInferred = inferred[i]; source.Events[i].Origin = "Container / "+(originalAss ? "ASS" : "Text");
                if (!originalAss) { source.Events[i].PositionKnown = false; source.Events[i].Motion = CueMotion.Unknown; }
            }
            return CaptionClassifier.Split(source);
        }
        static string AssTime(long ms)
        { return (ms/3600000).ToString(CultureInfo.InvariantCulture)+":"+(ms/60000%60).ToString("00",CultureInfo.InvariantCulture)+":"+(ms/1000%60).ToString("00",CultureInfo.InvariantCulture)+"."+(ms%1000).ToString("000",CultureInfo.InvariantCulture); }
        internal static string Extract(string path, int ordinal)
        {
            if (ordinal < 0) throw new ArgumentOutOfRangeException("ordinal");
            path = Path.GetFullPath(path);
            if (!File.Exists(path)) throw new FileNotFoundException("媒体文件不存在", path);
            StringBuilder output = new StringBuilder(); int cues = 0; bool overflow = false;
            Exception callbackError = null;
            CueCallback callback = delegate(long start, long end, IntPtr text, int count)
            {
                // Never allow a managed exception to unwind through native FFmpeg frames.
                try
                {
                    if (++cues > TimedText.MaxCues || count < 0 || count > TimedText.MaxBytes || output.Length + count > TimedText.MaxBytes)
                    { overflow = true; return 1; }
                    byte[] bytes = new byte[count]; Marshal.Copy(text, bytes, 0, count);
                    output.Append(cues).Append('\n').Append(Time(start)).Append(" --> ").Append(Time(end)).Append('\n');
                    output.Append(Encoding.UTF8.GetString(bytes)).Append("\n\n"); return 0;
                }
                catch (Exception error) { callbackError = error; return 1; }
            };
            byte[] diagnostics = new byte[1024];
            int result = bili_extract_caption(path, ordinal, callback, diagnostics, diagnostics.Length);
            GC.KeepAlive(callback);
            if (callbackError != null) throw new InvalidDataException("字幕回调失败", callbackError);
            if (overflow) throw new InvalidDataException("字幕内容超过大小限制");
            if (result < 0) throw new InvalidOperationException("字幕读取失败，可再次点击重试：" + Encoding.UTF8.GetString(diagnostics).TrimEnd('\0'));
            return output.ToString();
        }
        static string Time(long ms)
        {
            return (ms / 3600000).ToString("00", CultureInfo.InvariantCulture) + ":" + (ms / 60000 % 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (ms / 1000 % 60).ToString("00", CultureInfo.InvariantCulture) + "," + (ms % 1000).ToString("000", CultureInfo.InvariantCulture);
        }
    }
}
