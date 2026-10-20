using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace BilibiliPotPlayer
{
    internal sealed class CaptionOptions
    {
        public bool Multilingual, DanmakuEnabled = true, Scrolling = true, Fixed = true, Colored = true, AvoidSubtitles = true, ScaleWithScreen = true, UseDanmakuColor = true;
        public int SettingsVersion = 2;
        public int Region = 10, Opacity = 80, Size = 100, Speed = 100;
        public int SubtitleSize = 32, SubtitleOpacity = 100, SubtitleColor = 0xFFFFFF, SubtitleBottom = 7;
        public bool SubtitleBackground;
        public bool SubtitlesEnabled = true;
        public bool DanmakuSpeedSync, DanmakuBold = true;
        public string DanmakuFont = "SimHei";
        public int DanmakuOutline;
        public string SubtitleFont = "Microsoft YaHei UI";
        public bool SubtitleBold, SubtitleItalic, DanmakuItalic, DanmakuUniformColor, DanmakuBackground;
        public int SubtitleOutline = 1, SubtitleOutlineColor, SubtitleBackgroundColor;
        public int DanmakuTextColor = 0xFFFFFF, DanmakuOutlineColor, DanmakuBackgroundColor;
        internal void ResetDanmaku()
        { DanmakuEnabled = Scrolling = Fixed = Colored = AvoidSubtitles = ScaleWithScreen = UseDanmakuColor = DanmakuBold = true; Region = 10; Opacity = 80; Size = Speed = 100; DanmakuSpeedSync = false; DanmakuFont = "SimHei"; DanmakuOutline = 0; DanmakuItalic = DanmakuUniformColor = DanmakuBackground = false; DanmakuTextColor = 0xFFFFFF; DanmakuOutlineColor = DanmakuBackgroundColor = 0; }

        public Dictionary<string, int> Classes = new Dictionary<string, int>();
        internal static CaptionOptions Load() { return Load(SettingsPath); }
        internal static CaptionOptions Load(string path)
        {
            try { if (File.Exists(path)) { string json = File.ReadAllText(path); JavaScriptSerializer serializer = new JavaScriptSerializer(); CaptionOptions value = serializer.Deserialize<CaptionOptions>(json); if (value != null) { Dictionary<string, object> fields = serializer.Deserialize<Dictionary<string, object>>(json); if (!fields.ContainsKey("SettingsVersion") && value.Region == 50) value.Region = 10; value.Clamp(); return value; } } }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { } catch (InvalidOperationException) { }
            return new CaptionOptions();
        }
        internal void Clamp()
        {
            SettingsVersion = 2; Region = DanmakuSettings.Snap(Region, DanmakuSettings.Regions); Opacity = Math.Max(10, Math.Min(100, Opacity)); Size = Math.Max(50, Math.Min(170, Size)); Speed = DanmakuSettings.Snap(Speed, DanmakuSettings.Speeds);
            SubtitleSize = Math.Max(16, Math.Min(64, SubtitleSize)); SubtitleOpacity = Math.Max(10, Math.Min(100, SubtitleOpacity)); SubtitleBottom = Math.Max(2, Math.Min(30, SubtitleBottom)); SubtitleColor &= 0xFFFFFF;
            DanmakuOutline = Math.Max(0, Math.Min(3, DanmakuOutline)); if (Array.IndexOf(DanmakuSettings.Fonts, DanmakuFont) < 0) DanmakuFont = "SimHei";
            SubtitleOutline = Math.Max(0, Math.Min(3, SubtitleOutline));
            SubtitleOutlineColor &= 0xFFFFFF; SubtitleBackgroundColor &= 0xFFFFFF; DanmakuTextColor &= 0xFFFFFF; DanmakuOutlineColor &= 0xFFFFFF; DanmakuBackgroundColor &= 0xFFFFFF;
            if (Array.IndexOf(DanmakuSettings.Fonts, SubtitleFont) < 0) SubtitleFont = "Microsoft YaHei UI";
            if (Classes == null) Classes = new Dictionary<string, int>();
        }
        internal static string SettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BilibiliPotPlayer", "caption-options.json"); } }
        internal void Save() { Save(SettingsPath); }
        internal void Save(string path)
        {
            Clamp();
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); string temporary = path + ".tmp"; File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    internal sealed class ContainerCaption
    {
        internal int Ordinal;
        internal string Title = "", Language = "", Codec = "";
        internal bool Default, Bitmap;
    }
    internal sealed class CaptionCatalog : IDisposable
    {
        readonly IntPtr player;
        readonly StreamControls streams;
        readonly Func<TextSource, List<TextCue>> load;
        readonly Action saveOptions;
        readonly object sync = new object();
        readonly Queue<Action> completed = new Queue<Action>();
        int generation;
        string path = "";
        MediaCatalog media;
        long nextScan;
        bool nativeCaptured, nativeWasVisible, nativeVisible, initialSelectionDone, captionControlActive;
        internal bool Discovering;
        internal string Status = "";
        internal readonly List<TextSource> Sources = new List<TextSource>();
        internal readonly SubtitleSlots Slots = new SubtitleSlots();
        internal readonly CaptionOptions Options;
        internal int Revision;
        internal CaptionCatalog(IntPtr player, StreamControls streams) : this(player, streams, CaptionOptions.Load(), LoadContainer, null) { }
        internal CaptionCatalog(IntPtr player, StreamControls streams, CaptionOptions options, Func<TextSource, List<TextCue>> load, Action save)
        { this.player = player; this.streams = streams; Options = options; this.load = load; saveOptions = save ?? Options.Save; Slots.Multi = Options.Multilingual; }
        internal void DrainCompleted() { lock (sync) while (completed.Count > 0) completed.Dequeue()(); }
        static List<TextCue> LoadContainer(TextSource source)
        {
            string text = Extract(source.Path, source.Stream);
            if (String.IsNullOrWhiteSpace(text)) return new List<TextCue>();
            TextSource parsed = TimedText.Parse(text, source.Title, ".srt")[0];
            if (parsed.Error.Length > 0) throw new InvalidDataException(parsed.Error);
            return parsed.Cues;
        }
        internal static bool CanUse(TextSource source)
        { return !source.Bitmap && !source.IntrinsicAdvanced && source.Category != TextCategory.Advanced && (source.Error.Length == 0 || source.Stream >= 0); }
        internal void Tick(long now)
        {
            DrainCompleted();
            if (path != streams.SourcePath)
            {
                RestoreNative(); path = streams.SourcePath; media = null; generation++; Discovering = false; initialSelectionDone = captionControlActive = false; Sources.Clear(); Slots.Clear(); Status = ""; Revision++; nextScan = 0;
            }
            if (path.Length == 0 || !File.Exists(path)) return;
            if (media != streams.Catalog)
            {
                media = streams.Catalog;
                foreach (ContainerCaption track in media.Text)
                {
                    string id = Key(path) + ":stream:" + track.Ordinal;
                    if (Sources.Exists(s => s.Id == id)) continue;
                    Sources.Add(new TextSource { Id = id, Title = track.Title.Length > 0 ? track.Title : track.Language.Length > 0 ? track.Language : "字幕 " + (track.Ordinal + 1), Detail = track.Codec + (track.Language.Length > 0 ? " · " + track.Language : ""), Format = track.Codec, Path = path, Stream = track.Ordinal, Bitmap = track.Bitmap, Error = track.Bitmap ? "图像字幕沿用 PotPlayer 原生渲染，暂不在此切换" : "", Category = TextCategory.Subtitle });
                    Revision++;
                }
                ApplyClasses(Sources);
                ChooseInitial();
            }
            if (now >= nextScan) { nextScan = now + 3000; Discover(); }
            TextSource main = Find(Slots.Main), secondary = Find(Slots.Secondary);
            EnsureLoaded(main); EnsureLoaded(secondary);
            if (captionControlActive || !Options.SubtitlesEnabled) SetNativeVisible(false);
        }
        void Discover()
        {
            if (Discovering) return;
            Discovering = true; string current = path; int version = generation;
            HashSet<string> existing = new HashSet<string>(); foreach (TextSource source in Sources) if (source.Stream < 0 && source.Path != null) existing.Add(source.Path);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<TextSource> found = new List<TextSource>();
                try
                {
                    int count = 0;
                    foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(current)))
                    {
                        string extension = Path.GetExtension(file).ToLowerInvariant();
                        if (!SupportedExtension(extension) || existing.Contains(file)) continue;
                        if (++count > 256) break;
                        try { List<TextSource> parsed = ReadFile(file); string stem = Path.GetFileNameWithoutExtension(current); foreach (TextSource source in parsed) source.Enabled = source.Category == TextCategory.Danmaku && Path.GetFileName(file).StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase); found.AddRange(parsed); }
                        catch (Exception error) { if (!(error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is System.Xml.XmlException || error is ArgumentException || error is InvalidOperationException || error is System.Text.RegularExpressions.RegexMatchTimeoutException)) throw; found.Add(new TextSource { Id = Key(file), Path = file, Title = Path.GetFileName(file), Category = TextCategory.Uncertain, Loaded = true, Error = error.Message, Detail = "读取失败" }); }
                    }
                }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
                lock (sync) completed.Enqueue(delegate { if (version != generation) return; Discovering = false; Sources.AddRange(found); ApplyClasses(found); ChooseInitial(); Revision++; });
            });
        }
        internal static bool SupportedExtension(string extension)
        { return new HashSet<string> { ".srt", ".ass", ".ssa", ".vtt", ".lrc", ".xml", ".json", ".ttml", ".dfxp" }.Contains(extension.ToLowerInvariant()); }
        static List<TextSource> ReadFile(string file)
        {
            List<TextSource> sources = TimedText.Read(file);
            for (int i = 0; i < sources.Count; i++) { TextSource source = sources[i]; source.Path = file; source.Id = Key(file) + ":" + i; source.Enabled = source.Category == TextCategory.Danmaku && source.Cues.Count > 0; }
            return sources;
        }
        internal void Import(string file)
        {
            int version = generation; Status = "正在读取文件…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<TextSource> result = null; string error = "";
                try { result = ReadFile(file); } catch (Exception ex) { error = ex.Message; }
                lock (sync) completed.Enqueue(delegate
                {
                    if (version != generation) return;
                    if (result == null) { Status = "无法读取：" + error; return; }
                    foreach (TextSource source in result)
                    { TextSource existing = Find(source.Id); if (existing == null) Sources.Add(source); else if (existing.Category == TextCategory.Danmaku) existing.Enabled = true; }
                    ApplyClasses(result); Status = "";
                    TextSource subtitle = result.Find(s => s.Category == TextCategory.Subtitle && s.Error.Length == 0); if (subtitle != null) { initialSelectionDone = captionControlActive = true; if (Slots.Main != subtitle.Id && Slots.Secondary != subtitle.Id) Slots.Select(subtitle.Id); EnsureLoaded(Find(subtitle.Id)); }
                    Revision++;
                });
            });
        }
        void ApplyClasses(List<TextSource> sources)
        { foreach (TextSource source in sources) { int category; if (!source.IntrinsicAdvanced && Options.Classes.TryGetValue(source.Id, out category) && category >= 0 && category <= 3) source.Category = (TextCategory)category; } }
        void ChooseInitial()
        {
            if (initialSelectionDone || Slots.Main != null || Sources.Count == 0) return;
            // Initial discovery only: closing a selected track does not call this path again.
            string stem = Path.GetFileNameWithoutExtension(path);
            TextSource selected = Sources.Find(s => s.Category == TextCategory.Subtitle && s.Error.Length == 0 && s.Path != null && s.Stream < 0 && (Path.GetFileNameWithoutExtension(s.Path).Equals(stem, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(s.Path).StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)));
            if (selected == null && media != null && media.Text.Exists(t => t.Default && t.Bitmap)) { initialSelectionDone = true; return; }
            if (selected == null) selected = Sources.Find(s => s.Category == TextCategory.Subtitle && !s.Bitmap && s.Stream >= 0 && media != null && media.Text.Exists(t => t.Ordinal == s.Stream && t.Default));
            if (selected == null) selected = Sources.Find(s => s.Category == TextCategory.Subtitle && !s.Bitmap && s.Stream >= 0);
            if (selected != null) { initialSelectionDone = captionControlActive = true; Slots.Main = selected.Id; EnsureLoaded(selected); }
        }
        internal TextSource Find(string id) { return id == null ? null : Sources.Find(s => s.Id == id); }
        internal void Select(TextSource source)
        {
            if (source.Category != TextCategory.Subtitle || !CanUse(source)) return;
            initialSelectionDone = captionControlActive = true;
            Options.SubtitlesEnabled = true; saveOptions();
            Slots.Select(source.Id);
            EnsureLoaded(Find(Slots.Main), true); EnsureLoaded(Find(Slots.Secondary), true); Revision++;
        }
        internal void SetMulti(bool value) { Slots.SetMulti(value); Options.Multilingual = value; saveOptions(); Revision++; }
        internal void ToggleSubtitles()
        {
            Options.SubtitlesEnabled = !Options.SubtitlesEnabled;
            if (!captionControlActive) SetNativeVisible(Options.SubtitlesEnabled);
            saveOptions(); Revision++;
        }
        internal void Classify(TextSource source, TextCategory category)
        {
            if (source.IntrinsicAdvanced && category != TextCategory.Advanced) { Status = "高级 / 代码弹幕暂不支持播放"; return; }
            if (Slots.Main == source.Id || Slots.Secondary == source.Id) Slots.Select(source.Id);
            source.Category = category; source.Enabled = category == TextCategory.Danmaku && CanUse(source);
            if (source.Enabled) EnsureLoaded(source, true);
            source.Detail = category == TextCategory.Uncertain ? "用途待确认，请拖动分类" : source.Cues.Count + (category == TextCategory.Subtitle ? " 条字幕" : category == TextCategory.Danmaku ? " 条普通弹幕" : " 条 · 暂不播放") + " · 手动分类";
            Options.Classes[source.Id] = (int)category; saveOptions(); Status = ""; Revision++;
        }
        internal void Toggle(TextSource source)
        {
            if (source.Category != TextCategory.Danmaku || !CanUse(source)) return;
            source.Enabled = !source.Enabled;
            if (source.Enabled) EnsureLoaded(source, true);
            Revision++;
        }
        void EnsureLoaded(TextSource source, bool retry = false)
        {
            if (source == null || source.Loaded || source.Loading || source.Bitmap || source.Stream < 0 || source.Error.Length > 0 && !retry) return;
            source.Loading = true; source.Error = ""; Status = ""; int version = generation;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<TextCue> cues = null; string error = "";
                try { cues = load(source); }
                catch (Exception ex) { error = ex.Message; }
                lock (sync) completed.Enqueue(delegate
                {
                    if (version != generation) return; source.Loading = false; source.Loaded = cues != null; source.Error = error;
                    if (cues != null) { source.Cues.Clear(); source.Cues.AddRange(cues); }
                    else { source.Enabled = false; Status = error; }
                    if (cues == null && (Slots.Main == source.Id || Slots.Secondary == source.Id))
                    { Slots.Select(source.Id); if (Slots.Main == null) { captionControlActive = false; RestoreNative(); } }
                    Revision++;
                });
            });
        }
        internal static string Quote(string argument)
        {
            StringBuilder result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in argument) { if (c == '\\') { slashes++; continue; } if (c == '"') result.Append('\\', slashes * 2 + 1).Append(c); else result.Append('\\', slashes).Append(c); slashes = 0; }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        internal static string DecoderPath()
        { return FindDecoder(AppDomain.CurrentDomain.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetEnvironmentVariable("PATH") ?? "", File.Exists); }
        internal static string FindDecoder(string runtime, string localData, string searchPath, Func<string, bool> exists)
        {
            string result = DecoderCandidate(runtime, exists);
            if (result != null) return result;
            try { result = DecoderCandidate(Path.Combine(localData, "Microsoft", "WinGet", "Links"), exists); }
            catch (ArgumentException) { }
            if (result != null) return result;
            foreach (string entry in searchPath.Split(';'))
            {
                result = DecoderCandidate(entry, exists);
                if (result != null) return result;
            }
            return null;
        }
        static string DecoderCandidate(string folder, Func<string, bool> exists)
        {
            try
            {
                folder = Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"'));
                if (folder.Length == 0 || folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathRooted(folder)) return null;
                string candidate = Path.Combine(folder, "ffmpeg.exe");
                return exists(candidate) ? candidate : null;
            }
            catch (ArgumentException) { return null; } catch (NotSupportedException) { return null; } catch (PathTooLongException) { return null; }
        }
        internal static string Extract(string file, int ordinal)
        {
            string decoder = DecoderPath(); if (decoder == null) throw new InvalidOperationException("未找到本地 FFmpeg，无法读取这条内嵌文本字幕");
            ProcessStartInfo start = new ProcessStartInfo(decoder, "-nostdin -hide_banner -loglevel error -protocol_whitelist file,pipe -i " + Quote(file) + " -map 0:s:" + ordinal.ToString(CultureInfo.InvariantCulture) + " -c:s srt -f srt pipe:1");
            start.UseShellExecute = false; start.CreateNoWindow = true; start.RedirectStandardOutput = start.RedirectStandardError = true; start.StandardOutputEncoding = Encoding.UTF8;
            using (Process process = Process.Start(start))
            {
                StringBuilder output = new StringBuilder(), diagnostics = new StringBuilder(); bool tooLarge = false;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data == null) return; lock (output) { if (output.Length < TimedText.MaxBytes) output.AppendLine(e.Data); else tooLarge = true; } };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock (diagnostics) if (diagnostics.Length < 2048) diagnostics.AppendLine(e.Data.Substring(0, Math.Min(e.Data.Length, 2048 - diagnostics.Length))); }; process.BeginOutputReadLine(); process.BeginErrorReadLine();
                if (!process.WaitForExit(30000)) { process.Kill(); throw new InvalidOperationException("读取内嵌字幕超时"); }
                process.WaitForExit(); if (tooLarge) throw new InvalidDataException("字幕内容超过大小限制");
                if (process.ExitCode != 0) throw new InvalidOperationException("字幕读取失败，可再次点击重试：" + diagnostics.ToString().Trim());
                return output.ToString();
            }
        }
        void SetNativeVisible(bool value)
        {
            if (nativeCaptured && nativeVisible == value) return;
            List<NativeMenuSnapshot.Item> items = NativeCaptionAccess.Read(player); NativeMenuSnapshot.Item visible = items.Find(i => i.Command == 10126);
            if (visible == null) return;
            if (!nativeCaptured) { nativeWasVisible = visible.Checked; nativeCaptured = true; }
            if (visible.Checked != value) Native.SetCommand(player, 10126);
            nativeVisible = value;
        }
        void RestoreNative()
        {
            if (nativeCaptured && Native.IsWindow(player))
            { NativeMenuSnapshot.Item visible = NativeCaptionAccess.Read(player).Find(i => i.Command == 10126); if (visible != null && visible.Checked != nativeWasVisible) Native.SetCommand(player, 10126); }
            // PotPlayer saves CaptionVisible before destroying its window. Restore the user's
            // original preference as well when that window has already gone away.
            if (nativeCaptured)
                try { using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\DAUM\\PotPlayerMini64\\Settings")) { if (nativeWasVisible) key.DeleteValue("CaptionVisible", false); else key.SetValue("CaptionVisible", 0, RegistryValueKind.DWord); } }
                catch (UnauthorizedAccessException) { } catch (System.Security.SecurityException) { }
            nativeCaptured = false;
        }
        internal static string Key(string path)
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(path).ToLowerInvariant()))).Replace("-", "");
        }
        public void Dispose() { generation++; RestoreNative(); }
    }
}
