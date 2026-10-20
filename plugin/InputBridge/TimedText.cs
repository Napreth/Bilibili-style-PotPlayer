using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

namespace BilibiliPotPlayer
{
    internal enum TextCategory { Subtitle, Danmaku, Advanced, Uncertain }
    internal sealed class TextCue
    {
        internal double Start, End;
        internal string Text = "";
        internal int Mode = 1, Color = 0xFFFFFF, Size = 25;
    }
    internal sealed class TextSource
    {
        internal string Id, Title, Detail = "", Path, Format = "", Error = "";
        internal TextCategory Category;
        internal bool IntrinsicAdvanced, Enabled, Loading, Loaded, Bitmap;
        internal int Stream = -1;
        internal readonly List<TextCue> Cues = new List<TextCue>();
    }
    internal static class TimedText
    {
        internal const int MaxBytes = 32 * 1024 * 1024, MaxCues = 100000;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static Match Match(string value, string pattern) { return Regex.Match(value, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)); }
        static bool Has(string value, string pattern) { return Match(value, pattern).Success; }
        internal static List<TextSource> Read(string path)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length > MaxBytes) throw new InvalidDataException("文件过大或不存在");
            byte[] bytes = File.ReadAllBytes(path); string value;
            try { value = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
            catch (DecoderFallbackException) { value = Encoding.GetEncoding(936).GetString(bytes); }
            if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF))
                value = (bytes[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(bytes, 2, bytes.Length - 2);
            return Parse(value, Path.GetFileName(path), Path.GetExtension(path));
        }
        internal static List<TextSource> Parse(string value, string name, string extension)
        {
            if (value.Length > MaxBytes) throw new InvalidDataException("文件过大");
            string trimmed = value.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            List<TextSource> result = new List<TextSource>();
            TextSource source = new TextSource { Title = name, Category = TextCategory.Uncertain, Loaded = true };
            if (trimmed.StartsWith("<") && Has(trimmed, @"<d\s+[^>]*p\s*=")) return ParseXml(trimmed, name);
            if (Has(trimmed, @"(?m)^\s*\[Events\]")) ParseAss(trimmed, source);
            else if (Has(trimmed, @"(?m)^\s*(?:\d+:)?\d{1,2}:\d{2}[,.]\d+\s*-->")) ParseSrt(trimmed, source);
            else if (trimmed.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase)) ParseSrt(trimmed, source);
            else if (Has(trimmed, @"(?m)^\s*\[\d{1,3}:\d{2}(?:[.]\d+)?\]")) ParseLrc(trimmed, source);
            else if (trimmed.StartsWith("{") || trimmed.StartsWith("[")) return ParseJson(trimmed, name);
            else if (trimmed.StartsWith("<") && Has(trimmed, @"<(?:\w+:)?tt\b")) ParseTtml(trimmed, source);
            else source.Error = "无法识别内容结构";
            Sort(source.Cues); result.Add(source); return result;
        }
        internal static double Time(string value)
        {
            value = value.Trim().Replace(',', '.');
            double seconds;
            if (value.EndsWith("ms") && Double.TryParse(value.Substring(0, value.Length - 2), NumberStyles.Float, Inv, out seconds)) return Valid(seconds / 1000);
            if (value.EndsWith("s") && Double.TryParse(value.Substring(0, value.Length - 1), NumberStyles.Float, Inv, out seconds)) return Valid(seconds);
            string[] parts = value.Split(':'); double total = 0;
            if (parts.Length > 3) return -1;
            for (int i = 0; i < parts.Length; i++) { if (!Double.TryParse(parts[i], NumberStyles.Float, Inv, out seconds) || seconds < 0 || i > 0 && seconds >= 60) return -1; total = total * 60 + seconds; }
            return Valid(total);
        }
        static double Valid(double value) { return Double.IsNaN(value) || Double.IsInfinity(value) || value < 0 || value > 604800 ? -1 : value; }
        static string Plain(string value)
        { return System.Net.WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(value, @"\{[^}]*\}", "", RegexOptions.None, TimeSpan.FromMilliseconds(200)), @"<[^>]*>", "", RegexOptions.None, TimeSpan.FromMilliseconds(200))).Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", " ").Trim(); }
        static void Add(TextSource source, TextCue cue)
        {
            if (source.Cues.Count >= MaxCues) throw new InvalidDataException("条目数量超过限制");
            if (cue.Start < 0 || cue.End <= cue.Start || cue.Text.Length == 0) return;
            if (cue.Text.Length > 2000) cue.Text = cue.Text.Substring(0, 2000);
            cue.Size = Math.Max(12, Math.Min(64, cue.Size)); cue.Color &= 0xFFFFFF; source.Cues.Add(cue);
        }
        internal static void Sort(List<TextCue> cues) { cues.Sort(delegate(TextCue a, TextCue b) { return a.Start.CompareTo(b.Start); }); }
        static void ParseSrt(string value, TextSource source)
        {
            source.Format = "SRT / WebVTT"; source.Category = TextCategory.Subtitle;
            string[] lines = value.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                Match time = Match(lines[i], @"^\s*((?:\d+:)?\d{1,2}:\d{2}[,.]\d+)\s*-->\s*((?:\d+:)?\d{1,2}:\d{2}[,.]\d+)");
                if (!time.Success) continue;
                StringBuilder text = new StringBuilder();
                while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0) { if (text.Length > 2000) break; if (text.Length > 0) text.Append('\n'); text.Append(lines[++i]); }
                Add(source, new TextCue { Start = Time(time.Groups[1].Value), End = Time(time.Groups[2].Value), Text = Plain(text.ToString()) });
            }
        }
        static void ParseLrc(string value, TextSource source)
        {
            source.Format = "LRC"; source.Category = TextCategory.Subtitle;
            foreach (string line in value.Replace("\r", "").Split('\n'))
            {
                MatchCollection timestamps = Regex.Matches(line, @"\[(\d{1,3}:\d{2}(?:[.]\d+)?)\]", RegexOptions.None, TimeSpan.FromMilliseconds(200));
                string text = Regex.Replace(line, @"\[[^]]*\]", "", RegexOptions.None, TimeSpan.FromMilliseconds(200)).Trim();
                foreach (Match time in timestamps) Add(source, new TextCue { Start = Time(time.Groups[1].Value), End = Time(time.Groups[1].Value) + 8, Text = text });
            }
            Sort(source.Cues);
            for (int i = 0; i + 1 < source.Cues.Count; i++) if (source.Cues[i + 1].Start > source.Cues[i].Start) source.Cues[i].End = source.Cues[i + 1].Start;
        }
        static void ParseAss(string value, TextSource source)
        {
            source.Format = "ASS / SSA"; source.Category = TextCategory.Subtitle;
            bool events = false; string[] fields = { "Layer", "Start", "End", "Style", "Name", "MarginL", "MarginR", "MarginV", "Effect", "Text" };
            int moving = 0, complex = 0, lines = 0;
            foreach (string raw in value.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("[")) { events = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase); continue; }
                if (!events) continue;
                if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase)) { fields = line.Substring(7).Split(','); for (int i = 0; i < fields.Length; i++) fields[i] = fields[i].Trim(); continue; }
                if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase)) continue;
                string[] parts = line.Substring(9).Split(new char[] { ',' }, fields.Length);
                if (parts.Length != fields.Length) continue;
                int start = Array.FindIndex(fields, f => f.Equals("Start", StringComparison.OrdinalIgnoreCase)), end = Array.FindIndex(fields, f => f.Equals("End", StringComparison.OrdinalIgnoreCase)), text = Array.FindIndex(fields, f => f.Equals("Text", StringComparison.OrdinalIgnoreCase));
                if (start < 0 || end < 0 || text < 0) continue;
                string body = parts[text]; lines++;
                if (Has(body, @"\\move\s*\(")) moving++;
                if (Has(body, @"\\(?:p[1-9]|clip\(|iclip\(|t\(|frx|fry|frz)")) complex++;
                TextCue cue = new TextCue { Start = Time(parts[start]), End = Time(parts[end]), Text = Plain(body), Mode = Has(body, @"\\an[78]") ? 5 : Has(body, @"\\an[12]") ? 4 : 1 };
                Match color = Match(body, @"\\(?:1?c)&H([0-9a-f]{6})&"); int number;
                if (color.Success && Int32.TryParse(color.Groups[1].Value, NumberStyles.HexNumber, Inv, out number)) cue.Color = (number & 255) << 16 | number & 0xFF00 | number >> 16;
                Match size = Match(body, @"\\fs(\d+)"); if (size.Success && Int32.TryParse(size.Groups[1].Value, out number)) cue.Size = number;
                Add(source, cue);
            }
            bool declared = Has(value, @"(?im)^(?:;|Comment:|Title:|Original Script:|Script Updated By:).*?(?:danmaku|danmu|弹幕|bilibili.*(?:xml|comment))");
            if (declared) { source.Category = complex > 0 ? TextCategory.Advanced : TextCategory.Danmaku; source.IntrinsicAdvanced = complex > 0; }
            else if (complex > 0 || moving >= 3 && moving * 2 >= lines) { source.Category = TextCategory.Uncertain; source.Detail = "动画/绘图 ASS，需确认用途"; }
        }
        static XmlReader Xml(string value)
        { return XmlReader.Create(new StringReader(value), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes }); }
        static List<TextSource> ParseXml(string value, string name)
        {
            TextSource ordinary = new TextSource { Title = name, Category = TextCategory.Danmaku, Format = "Bilibili XML", Loaded = true };
            TextSource advanced = new TextSource { Title = name, Category = TextCategory.Advanced, Format = "高级 / 代码 / BAS", Loaded = true, IntrinsicAdvanced = true };
            int advancedCount = 0, total = 0;
            using (XmlReader reader = Xml(value))
                while (!reader.EOF)
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "d") { reader.Read(); continue; }
                    if (++total > MaxCues) throw new InvalidDataException("条目数量超过限制");
                    string[] p = (reader.GetAttribute("p") ?? "").Split(','); string text = reader.ReadElementContentAsString();
                    int mode, size = 25, color = 0xFFFFFF; double time;
                    if (p.Length < 4 || !Double.TryParse(p[0], NumberStyles.Float, Inv, out time) || Valid(time) < 0 || !Int32.TryParse(p[1], out mode)) continue;
                    if (mode >= 7 && mode <= 9) { advancedCount++; continue; }
                    if (mode < 1 || mode > 6) continue;
                    if (!Int32.TryParse(p[2], out size)) size = 25; if (!Int32.TryParse(p[3], out color)) color = 0xFFFFFF;
                    Add(ordinary, new TextCue { Start = time, End = time + 8, Text = text.Replace("/n", "\n"), Mode = mode, Size = size, Color = color });
                }
            List<TextSource> result = new List<TextSource>();
            if (ordinary.Cues.Count > 0) { ordinary.Detail = ordinary.Cues.Count + " 条普通弹幕"; Sort(ordinary.Cues); result.Add(ordinary); }
            if (advancedCount > 0) { advanced.Detail = advancedCount + " 条 · 暂不播放"; result.Add(advanced); }
            if (result.Count == 0) { ordinary.Detail = "没有有效条目"; result.Add(ordinary); }
            return result;
        }
        static List<TextSource> ParseJson(string value, string name)
        {
            JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 32 };
            object parsed = json.DeserializeObject(value);
            Dictionary<string, object> document = parsed as Dictionary<string, object>;
            bool declared = document != null && document.ContainsKey("danmaku");
            object[] entries = (declared ? document["danmaku"] : parsed) as object[];
            TextSource ordinary = new TextSource { Title = name, Category = declared ? TextCategory.Danmaku : TextCategory.Uncertain, Format = "JSON", Loaded = true };
            TextSource advanced = new TextSource { Title = name, Category = TextCategory.Advanced, IntrinsicAdvanced = true, Loaded = true, Format = "JSON 高级弹幕" };
            int advancedCount = 0;
            if (entries != null)
            {
                if (entries.Length > MaxCues) throw new InvalidDataException("条目数量超过限制");
                foreach (object entry in entries)
                {
                    Dictionary<string, object> item = entry as Dictionary<string, object>; if (item == null || !item.ContainsKey("time") || !item.ContainsKey("text")) continue;
                    double time = Time(Convert.ToString(item["time"], Inv)); int mode = 1, color = 0xFFFFFF, size = 25;
                    if (item.ContainsKey("mode")) Int32.TryParse(Convert.ToString(item["mode"], Inv), out mode);
                    string type = item.ContainsKey("type") ? Convert.ToString(item["type"]) : "";
                    if (mode >= 7 || type == "script" || type == "bas" || type == "advanced") { advancedCount++; continue; }
                    if (item.ContainsKey("color")) { string c = Convert.ToString(item["color"], Inv); if (c.StartsWith("#")) Int32.TryParse(c.Substring(1), NumberStyles.HexNumber, Inv, out color); else Int32.TryParse(c, out color); }
                    if (item.ContainsKey("size")) Int32.TryParse(Convert.ToString(item["size"], Inv), out size);
                    double end = item.ContainsKey("end") ? Time(Convert.ToString(item["end"], Inv)) : time + 8;
                    Add(ordinary, new TextCue { Start = time, End = end, Text = Convert.ToString(item["text"]), Mode = Math.Max(1, Math.Min(6, mode)), Color = color, Size = size });
                }
            }
            Sort(ordinary.Cues); List<TextSource> result = new List<TextSource>();
            if (ordinary.Cues.Count > 0 || advancedCount == 0) { ordinary.Detail = declared ? ordinary.Cues.Count + " 条普通弹幕" : "JSON 用途不明确，请拖动分类"; result.Add(ordinary); }
            if (advancedCount > 0) { advanced.Detail = advancedCount + " 条 · 暂不播放"; result.Add(advanced); }
            return result;
        }
        static void ParseTtml(string value, TextSource source)
        {
            source.Category = TextCategory.Subtitle; source.Format = "TTML";
            using (XmlReader reader = Xml(value))
                while (!reader.EOF)
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "p") { reader.Read(); continue; }
                    double start = Time(reader.GetAttribute("begin") ?? ""), end = Time(reader.GetAttribute("end") ?? "");
                    if (end < 0) { double duration = Time(reader.GetAttribute("dur") ?? ""); if (duration >= 0) end = start + duration; }
                    Add(source, new TextCue { Start = start, End = end, Text = Plain(reader.ReadInnerXml()) });
                }
        }
    }
    internal sealed class SubtitleSlots
    {
        internal bool Multi;
        internal string Main, Secondary;
        internal void Select(string id)
        {
            if (id == Main) { Main = Secondary; Secondary = null; return; }
            if (id == Secondary) { Secondary = null; return; }
            if (!Multi || Main == null) { Main = id; Secondary = null; }
            else Secondary = id;
        }
        internal void SetMulti(bool value) { Multi = value; if (!value) Secondary = null; }
        internal void Clear() { Main = Secondary = null; }
    }
}
