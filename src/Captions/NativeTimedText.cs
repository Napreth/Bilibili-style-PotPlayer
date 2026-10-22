using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
namespace BilibiliPotPlayer
{
    internal static class NativeTimedText
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int CueCallback(long start, long end, IntPtr utf8, int bytes);
        [DllImport("BilibiliTimedText.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        static extern int bili_extract_caption(string path, int ordinal, CueCallback callback, byte[] error, int errorSize);
        [DllImport("BilibiliTimedText.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr bili_ffmpeg_license();
        [DllImport("BilibiliTimedText.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr bili_ffmpeg_configuration();
        internal static string License { get { return Marshal.PtrToStringAnsi(bili_ffmpeg_license()); } }
        internal static string Configuration { get { return Marshal.PtrToStringAnsi(bili_ffmpeg_configuration()); } }
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
