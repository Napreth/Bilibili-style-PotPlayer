using System;
using System.IO;
using System.Collections.Generic;
using BilibiliPotPlayer;
internal static class NativeTimedTextChecks
{
    internal static void Run(Action<bool, bool, string> equal)
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
        string temporary = Path.Combine(Path.GetTempPath(), "bili-subtitles-你好 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        string oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "");
            equal(true, NativeTimedText.License.StartsWith("LGPL", StringComparison.Ordinal), "bundled FFmpeg reports an LGPL license without toolchain DLLs on PATH");
            equal(false, NativeTimedText.Configuration.Contains("--enable-gpl") || NativeTimedText.Configuration.Contains("--enable-nonfree"), "bundled FFmpeg excludes GPL and nonfree configurations");
            equal(true, NativeTimedText.Configuration.Contains("--disable-programs") && NativeTimedText.Configuration.Contains("--enable-shared"), "FFmpeg is built as shared libraries without executables");
            string file = Path.Combine(temporary, "字幕 sample.mkv");
            File.Copy(Path.Combine(root, "tests", "fixtures", "text-tracks.mkv"), file);
            List<TextSource> first = TimedText.Parse(CaptionCatalog.Extract(file, 0), "first", ".srt");
            List<TextSource> second = TimedText.Parse(CaptionCatalog.Extract(file, 1), "second", ".srt");
            equal(true, first[0].Cues.Count == 2 && first[0].Cues[0].Text.Contains("你好"), "native FFmpeg reads SRT in a Unicode path with no executable on PATH");
            equal(true, Math.Abs(first[0].Cues[0].Start - 1.25) < .001 && Math.Abs(first[0].Cues[0].End - 3.75) < .001, "native extraction preserves millisecond timestamps");
            equal(true, second[0].Cues.Count == 1 && second[0].Cues[0].Text.Contains("日本語"), "subtitle ordinals select the second ASS track independently");
            string mp4 = Path.Combine(root, "tests", "fixtures", "text-track.mp4");
            equal(true, TimedText.Parse(CaptionCatalog.Extract(mp4,0), "mp4", ".srt")[0].Cues[0].Text.Contains("你好"), "native FFmpeg decodes MP4 mov_text");
            List<TextSource> plain = NativeTimedText.Read(file,0,"SRT");
            equal(true, plain[0].Category == TextCategory.Uncertain && !plain[0].Events[0].PositionKnown && plain[0].Events[0].Motion == CueMotion.Unknown, "container SRT uses the same classifier without invented ASS spatial evidence");
            List<TextSource> mixed = NativeTimedText.Read(Path.Combine(root,"tests","fixtures","classification.mkv"),0,"Mixed");
            equal(true, mixed.Count == 3 && mixed[0].Category == TextCategory.Subtitle && mixed[1].Category == TextCategory.Danmaku && mixed[2].IntrinsicAdvanced,"container ASS preserves enough metadata to split dialogue, scrolling and effects");
            equal(true, mixed[1].Events[0].RawContent.Contains("\\move(") && mixed[1].Events[0].PositionKnown && mixed[2].Events.Count == 3 && mixed[2].Cues.Count == 0,"container extraction retains original animation tags, canvas and unplayable advanced events");
            equal(true, Math.Abs(mixed[0].Events[0].Start-1.25)<.001 && Math.Abs(mixed[0].Events[0].End-3.75)<.001,"raw metadata extraction retains container timing");
            bool invalid = false;
            try { CaptionCatalog.Extract(file, 20); } catch (InvalidOperationException) { invalid = true; }
            equal(true, invalid, "invalid subtitle ordinal reports an error instead of selecting another stream");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH",oldPath);
            foreach (string file in Directory.GetFiles(temporary)) File.Delete(file);
            Directory.Delete(temporary);
        }
    }
}
