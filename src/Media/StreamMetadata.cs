using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BilibiliPotPlayer
{
    internal sealed class MediaTrack
    {
        internal bool Video, Lossless, Atmos, Interlaced;
        internal string Label = "", Codec = "", Channels = "", Spatial = "", Hdr = "";
        internal int Width, Height, Bits, SampleRate;
        internal double DisplayRatio;
        internal double Fps;
        internal long Bitrate;
        internal bool HighResolution { get { return !Video && Lossless && Bits >= 24 && SampleRate >= 96000; } }
        internal string Summary(UiLanguage language)
        {
            if (Video) return Join(language.Hdr(Hdr), Resolution);
            return Join(HighResolution ? "Hi-Res" : "", language.Spatial(Spatial), language.ChannelLabel(Channels));
        }
        internal string Details(UiLanguage language)
        {
            // The menu shows measurements instead of repeating a quality badge.
            if (Video) return Join(Codec, language.Hdr(Hdr), ResolutionText(false), Bits > 0 ? Bits + "bit" : "", Rate(Bitrate));
            return Join(language.Spatial(Spatial), Codec, language.ChannelLabel(Channels), AudioSpecification, Rate(Bitrate));
        }
        internal string AudioSpecification
        {
            get
            {
                string rate = SampleRate > 0 ? (SampleRate / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "kHz" : "";
                string depth = Bits > 0 ? Bits + "bit" : "";
                return rate.Length > 0 && depth.Length > 0 ? rate + "/" + depth : rate + depth;
            }
        }
        internal string Resolution
        { get { return ResolutionText(true); } }
        string ResolutionText(bool compact)
        {
            string size = Width > 0 && Height > 0 ? compact ? ResolutionName(Width, Height) : Width + "×" + Height : "";
            if (compact && Interlaced && size.EndsWith("P", StringComparison.Ordinal)) size = size.Substring(0, size.Length - 1) + "I";
            return Join(size, Fps > 0 ? "@" + Fps.ToString("0.###", CultureInfo.InvariantCulture) + "fps" : "").Replace(" @", "@");
        }
        internal static string ResolutionName(int width, int height)
        {
            // Recognize exact conventional sizes, including portrait orientation.
            // Cropped or unusual dimensions retain their actual size.
            int longer = Math.Max(width, height), shorter = Math.Min(width, height);
            if (longer == 7680 && shorter == 4320) return "8K";
            if (longer == 5120 && shorter == 2880) return "5K";
            if ((longer == 3840 || longer == 4096) && shorter == 2160) return "4K";
            if (longer == 2560 && shorter == 1440) return "1440P";
            if (longer == 2048 && shorter == 1080) return "2K";
            if (longer == 1920 && shorter == 1080) return "1080P";
            if (longer == 1280 && shorter == 720) return "720P";
            if (longer == 960 && shorter == 540) return "540P";
            if ((longer == 768 || longer == 720) && shorter == 576) return "576P";
            if ((longer == 854 || longer == 720 || longer == 640) && shorter == 480) return "480P";
            if (longer == 640 && shorter == 360) return "360P";
            return width + "×" + height;
        }
        static string Rate(long value)
        { return value > 0 ? (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kbps" : ""; }
        internal static string Join(params string[] parts)
        { return String.Join(" ", Array.FindAll(parts, delegate(string s) { return !String.IsNullOrWhiteSpace(s); })); }
        internal static string CodecName(string format, string profile)
        {
            if (format == "AVC") return "H.264";
            if (format == "HEVC") return "H.265";
            if (format == "MLP FBA") return "TrueHD";
            if (format == "MPEG Audio") return profile.IndexOf("3", StringComparison.OrdinalIgnoreCase) >= 0 ? "MP3" : "MPEG Audio";
            if (format == "DTS" && profile.IndexOf("MA", StringComparison.OrdinalIgnoreCase) >= 0) return "DTS-HD MA";
            if (format == "DTS" && profile.IndexOf("HRA", StringComparison.OrdinalIgnoreCase) >= 0) return "DTS-HD HRA";
            return format;
        }
        internal static string SpatialName(string additional, string profile, string commercial)
        {
            string text = Join(additional, profile, commercial);
            if (text.IndexOf("Atmos", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("JOC", StringComparison.OrdinalIgnoreCase) >= 0) return "Atmos";
            if (text.IndexOf("DTS:X", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("DTS X", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("XLL X", StringComparison.OrdinalIgnoreCase) >= 0) return "DTS:X";
            return "";
        }
        internal static string HdrName(string format, string compatibility, string transfer)
        {
            string text = Join(format, compatibility);
            if (text.IndexOf("Dolby Vision", StringComparison.OrdinalIgnoreCase) >= 0) return "Dolby Vision";
            if (text.IndexOf("2094-40", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("HDR10+", StringComparison.OrdinalIgnoreCase) >= 0) return "HDR10+";
            if (text.IndexOf("HDR10", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("2086", StringComparison.OrdinalIgnoreCase) >= 0) return "HDR10";
            if (transfer.IndexOf("HLG", StringComparison.OrdinalIgnoreCase) >= 0) return "HLG";
            if (transfer.IndexOf("PQ", StringComparison.OrdinalIgnoreCase) >= 0 || transfer.IndexOf("2084", StringComparison.OrdinalIgnoreCase) >= 0) return "HDR (PQ)";
            return "";
        }
        internal static string ChannelName(string layout, int channels)
        {
            if (channels <= 0) return "";
            // Use the source bed layout, never invent Atmos object counts / height channels.
            int lfe = 0, heights = 0;
            foreach (string part in layout.Split(new char[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.StartsWith("LFE", StringComparison.OrdinalIgnoreCase)) lfe++;
                else if (part.StartsWith("T", StringComparison.OrdinalIgnoreCase)) heights++;
            }
            return heights > 0 ? (channels - lfe - heights) + "." + lfe + "." + heights : (channels - lfe) + "." + lfe;
        }
    }
    internal sealed class MediaCatalog
    {
        internal bool Spherical;
        internal string Container = "";
        internal long FileSize, DurationMilliseconds, OverallBitrate;
        internal readonly List<MediaTrack> Audio = new List<MediaTrack>(), Video = new List<MediaTrack>();
        internal readonly List<ContainerCaption> Text = new List<ContainerCaption>();
        internal static MediaCatalog Read(string path)
        {
            MediaCatalog catalog = new MediaCatalog();
            // Only local files; metadata is neither persisted nor sent over the network.
            if (!Path.IsPathRooted(path) || !File.Exists(path)) return catalog;
            IntPtr handle = MediaInfo_New();
            if (handle == IntPtr.Zero) return catalog;
            try
            {
                MediaInfo_Option(handle, "Internet", "No");
                MediaInfo_Option(handle, "ParseSpeed", "0.5");
                if (MediaInfo_Open(handle, path) == IntPtr.Zero) return catalog;
                catalog.Container = Marshal.PtrToStringUni(MediaInfo_Get(handle, IntPtr.Zero, IntPtr.Zero, "Format", (IntPtr)1, IntPtr.Zero)) ?? "";
                catalog.FileSize = Number(Marshal.PtrToStringUni(MediaInfo_Get(handle, IntPtr.Zero, IntPtr.Zero, "FileSize", (IntPtr)1, IntPtr.Zero)) ?? "");
                catalog.DurationMilliseconds = Number(Marshal.PtrToStringUni(MediaInfo_Get(handle, IntPtr.Zero, IntPtr.Zero, "Duration", (IntPtr)1, IntPtr.Zero)) ?? "");
                catalog.OverallBitrate = Number(Marshal.PtrToStringUni(MediaInfo_Get(handle, IntPtr.Zero, IntPtr.Zero, "OverallBitRate", (IntPtr)1, IntPtr.Zero)) ?? "");
                HashSet<long> rpuTracks = null;
                for (int kind = 1; kind <= 2; kind++)
                {
                    int count = Math.Min(256, MediaInfo_Count_Get(handle, (IntPtr)kind, (IntPtr)(-1)).ToInt32());
                    for (int i = 0; i < count; i++)
                    {
                        Func<string, string> get = delegate(string field)
                        { return Marshal.PtrToStringUni(MediaInfo_Get(handle, (IntPtr)kind, (IntPtr)i, field, (IntPtr)1, IntPtr.Zero)) ?? ""; };
                        MediaTrack track = new MediaTrack(); track.Video = kind == 1;
                        string format = get("Format"), profile = get("Format_Profile");
                        track.Label = Clean(get("Title"));
                        if (track.Label.Length == 0) track.Label = Clean(get("Language/String"));
                        track.Codec = MediaTrack.CodecName(format, profile);
                        track.Bits = (int)Number(get("BitDepth"));
                        track.Bitrate = Number(get("BitRate"));
                        if (track.Video)
                        {
                            track.Width = (int)Number(get("Width")); track.Height = (int)Number(get("Height"));
                            double displayRatio, rotation;
                            if (Double.TryParse(get("DisplayAspectRatio"), NumberStyles.Float, CultureInfo.InvariantCulture, out displayRatio)) track.DisplayRatio = displayRatio;
                            if (Double.TryParse(get("Rotation"), NumberStyles.Float, CultureInfo.InvariantCulture, out rotation) && Math.Abs(rotation % 180) > 45 && Math.Abs(rotation % 180) < 135) track.DisplayRatio = track.DisplayRatio > 0 ? 1 / track.DisplayRatio : track.Width > 0 ? track.Height / (double)track.Width : 0;
                            track.Interlaced = get("ScanType").Equals("Interlaced", StringComparison.OrdinalIgnoreCase);
                            double fps; Double.TryParse(get("FrameRate"), NumberStyles.Float, CultureInfo.InvariantCulture, out fps); track.Fps = fps;
                            track.Hdr = MediaTrack.HdrName(get("HDR_Format"), get("HDR_Format_Compatibility"), get("transfer_characteristics"));
                            if (format == "HEVC" && track.Hdr != "Dolby Vision")
                            {
                                if (rpuTracks == null) rpuTracks = DolbyVisionProbe.Read(path);
                                if (rpuTracks.Contains(Number(get("ID")))) track.Hdr = "Dolby Vision";
                            }
                            catalog.Video.Add(track);
                        }
                        else
                        {
                            track.Lossless = get("Compression_Mode") == "Lossless" || format == "PCM" || format == "FLAC" || format == "ALAC" || format == "MLP FBA";
                            track.SampleRate = (int)Number(get("SamplingRate"));
                            track.Channels = MediaTrack.ChannelName(get("ChannelLayout"), (int)Number(get("Channels")));
                            track.Spatial = MediaTrack.SpatialName(get("Format_AdditionalFeatures"), profile, get("Format_Commercial_IfAny"));
                            track.Atmos = track.Spatial == "Atmos"; catalog.Audio.Add(track);
                        }
                    }
                }
                int textCount = Math.Min(256, MediaInfo_Count_Get(handle, (IntPtr)3, (IntPtr)(-1)).ToInt32());
                for (int i = 0; i < textCount; i++)
                {
                    int ordinal = i;
                    Func<string, string> text = field => Marshal.PtrToStringUni(MediaInfo_Get(handle, (IntPtr)3, (IntPtr)ordinal, field, (IntPtr)1, IntPtr.Zero)) ?? "";
                    string codec = text("Format");
                    catalog.Text.Add(new ContainerCaption { Ordinal = i, Title = Clean(text("Title")), Language = Clean(text("Language/String")), Codec = codec, Default = text("Default") == "Yes", Bitmap = codec == "PGS" || codec == "RLE" || codec == "DVB Subtitle" || codec == "VobSub" });
                }
                if (catalog.Video.Count > 0) catalog.Spherical = SphericalVideoProbe.Read(path);
                return catalog;
            }
            finally { MediaInfo_Close(handle); MediaInfo_Delete(handle); }
        }
        static long Number(string value)
        { long n; Int64.TryParse(value.Split('/')[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n); return n; }
        static string Clean(string value)
        { return value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim(); }
        [DllImport("MediaInfo.dll")] static extern IntPtr MediaInfo_New();
        [DllImport("MediaInfo.dll")] static extern void MediaInfo_Delete(IntPtr handle);
        [DllImport("MediaInfo.dll", CharSet = CharSet.Unicode)] static extern IntPtr MediaInfo_Open(IntPtr handle, string path);
        [DllImport("MediaInfo.dll")] static extern void MediaInfo_Close(IntPtr handle);
        [DllImport("MediaInfo.dll", CharSet = CharSet.Unicode)] static extern IntPtr MediaInfo_Get(IntPtr handle, IntPtr kind, IntPtr stream, string field, IntPtr info, IntPtr search);
        [DllImport("MediaInfo.dll", CharSet = CharSet.Unicode)] static extern IntPtr MediaInfo_Option(IntPtr handle, string option, string value);
        [DllImport("MediaInfo.dll")] static extern IntPtr MediaInfo_Count_Get(IntPtr handle, IntPtr kind, IntPtr stream);
    }
    internal sealed class MetadataMonitor : NativeWindow, IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct CopyData { internal IntPtr type; internal int size; internal IntPtr bytes; }
        string filename = "", requestedPath = "";
        IntPtr readingPlayer;
        long nextRead;
        int generation;
        readonly object sync = new object();
        MediaCatalog completed;
        internal MediaCatalog Catalog { get; private set; }
        internal string SourcePath { get { return requestedPath; } }
        internal MetadataMonitor()
        {
            Catalog = new MediaCatalog();
            CreateHandle(new CreateParams { Caption = "Bilibili metadata endpoint", Parent = (IntPtr)(-3) });
        }
        internal void UpdatePlayer(IntPtr player, long now)
        {
            lock (sync) { if (completed != null) { Catalog = completed; completed = null; } }
            if (player == IntPtr.Zero || now < nextRead) return;
            nextRead = now + 500;
            readingPlayer = player; filename = "";
            long unused;
            // A busy player is not a newly opened empty file. Preserve the current catalog.
            if (!Native.TrySend(player, Native.WM_USER, 0x6020, Handle.ToInt64(), out unused)) return;
            if (filename == requestedPath) return;
            requestedPath = filename; generation++;
            Catalog = new MediaCatalog(); int revision = generation; string path = filename;
            ThreadPool.QueueUserWorkItem(delegate
            {
                MediaCatalog result;
                try { result = MediaCatalog.Read(path); } catch { result = new MediaCatalog(); }
                lock (sync) { if (revision == generation) completed = result; }
            });
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x4A && m.WParam == readingPlayer && m.LParam != IntPtr.Zero)
            {
                CopyData data = (CopyData)Marshal.PtrToStructure(m.LParam, typeof(CopyData));
                if (data.type.ToInt64() == 0x6020 && data.size > 0 && data.size < 65536 && data.bytes != IntPtr.Zero)
                {
                    byte[] bytes = new byte[data.size]; Marshal.Copy(data.bytes, bytes, 0, bytes.Length);
                    filename = Encoding.UTF8.GetString(bytes).TrimEnd('\0'); m.Result = (IntPtr)1; return;
                }
            }
            base.WndProc(ref m);
        }
        public void Dispose() { lock (sync) generation++; DestroyHandle(); }
    }
}
