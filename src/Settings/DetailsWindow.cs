using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal sealed class DetailsWindow : PlayerWindow
    {
        readonly StreamControls streams;
        readonly CaptionCatalog captions;
        readonly TextBox details;
        internal DetailsWindow(IntPtr player, StreamControls streams, CaptionCatalog captions) : base(player, "视频详情", new Size(800, 690))
        {
            this.streams = streams; this.captions = captions;
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(24, 12, 24, 12) };
            Button copy = Button("复制详情"); copy.Dock = DockStyle.Right; copy.Width = 130;
            Label note = new Label { Text = "媒体信息来自本地文件；播放状态实时刷新。", Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft };
            copy.Click += delegate { try { Clipboard.SetText(Build()); note.Text = "已复制。"; } catch (System.Runtime.InteropServices.ExternalException) { note.Text = "剪贴板暂不可用，请重试。"; } };
            footer.Controls.Add(note); footer.Controls.Add(copy);
            details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, BackColor = Background, ForeColor = ForeColor, Font = Font, WordWrap = true, TabStop = true };
            Panel padding = new Panel { Dock = DockStyle.Fill, Padding = new Padding(26, 22, 26, 8) }; padding.Controls.Add(details); Body.Controls.Add(padding); Body.Controls.Add(footer); RefreshDetails();
            Shown += delegate { ActiveControl = copy; details.SelectionStart = details.SelectionLength = 0; };
        }
        internal void RefreshDetails() { if (!details.Focused) { string value = Build(); if (value != details.Text) details.Text = value; } }
        string Build()
        {
            StringBuilder text = new StringBuilder(); MediaCatalog media = streams.Catalog;
            text.AppendLine("文件"); text.AppendLine(String.IsNullOrEmpty(streams.SourcePath) ? "尚未打开媒体" : streams.SourcePath); text.AppendLine();
            if (!String.IsNullOrEmpty(media.Container)) text.AppendLine("容器格式：" + media.Container);
            if (media.FileSize > 0) text.AppendLine("文件大小：" + (media.FileSize / 1048576.0).ToString("N2", CultureInfo.CurrentCulture) + " MiB (" + media.FileSize + " 字节)");
            if (media.OverallBitrate > 0) text.AppendLine("总码率：" + (media.OverallBitrate / 1000.0).ToString("0.#") + " kbps");
            long duration, position, speed, volume, status;
            text.AppendLine("时长：" + (Native.Get(Player, Native.GetDuration, out duration) && duration > 0 ? Time(duration) : media.DurationMilliseconds > 0 ? Time(media.DurationMilliseconds) : "—"));
            text.AppendLine("当前进度：" + (Native.Get(Player, Native.GetPosition, out position) ? Time(position) : "—"));
            text.AppendLine("播放状态：" + (Native.Get(Player, Native.GetPlayStatus, out status) ? status == 2 ? "播放" : status == 1 ? "暂停" : "停止" : "—"));
            text.AppendLine("播放速度：" + (Native.Get(Player, Native.GetSpeed, out speed) && speed > 0 ? (speed / 1000.0).ToString("0.###") + "×" : "—"));
            text.AppendLine("音量：" + (Native.Get(Player, Native.GetVolume, out volume) ? volume + "%" : "—"));
            text.AppendLine(); UiLanguage language = UiLanguage.Read();
            text.AppendLine("视频轨道（源文件）");
            if (media.Video.Count == 0) text.AppendLine("—");
            for (int i = 0; i < media.Video.Count; i++) { MediaTrack track = media.Video[i]; text.AppendLine("#" + (i + 1) + "  " + track.Label + "  " + track.Details(language)); }
            Rectangle viewport = Native.ClientBounds(Native.VideoSurface(Player)); if (!viewport.IsEmpty) text.AppendLine("渲染视口：" + viewport.Width + " × " + viewport.Height);
            text.AppendLine(); text.AppendLine("音频轨道（源文件）");
            if (media.Audio.Count == 0) text.AppendLine("—");
            for (int i = 0; i < media.Audio.Count; i++) text.AppendLine("#" + (i + 1) + "  " + media.Audio[i].Label + "  " + media.Audio[i].Details(language));
            text.AppendLine(); text.AppendLine("字幕与弹幕");
            text.AppendLine("内封字幕轨道：" + media.Text.Count + "    发现的文本源：" + captions.Sources.Count);
            text.AppendLine("字幕：" + (captions.Options.SubtitlesEnabled ? "开启" : "关闭") + "    普通弹幕：" + (captions.Options.DanmakuEnabled ? "开启" : "关闭") + "    ASS 特效：" + (captions.Options.AssEffectsEnabled ? "开启" : "关闭"));
            text.AppendLine(); text.AppendLine("Bilibili-style-PotPlayer " + typeof(DetailsWindow).Assembly.GetName().Version + "  ·  pre-alpha");
            return text.ToString();
        }
        internal static string Time(long milliseconds)
        { long seconds = Math.Max(0, milliseconds / 1000); return (seconds / 3600).ToString("00") + ":" + (seconds / 60 % 60).ToString("00") + ":" + (seconds % 60).ToString("00"); }
    }
}
