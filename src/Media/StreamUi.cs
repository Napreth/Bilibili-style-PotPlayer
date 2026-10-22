using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal sealed class StreamButton : LayeredSurface
    {
        internal readonly bool Video;
        internal List<MediaTrack> Tracks = new List<MediaTrack>();
        internal UiLanguage Language = new UiLanguage("English.ini");
        readonly NativeTrackAccess access;
        string lastText = "";
        internal StreamButton(bool video, NativeTrackAccess access) : base(video ? "视频参数" : "音频参数", true)
        { Video = video; this.access = access; BackColor = Color.FromArgb(30, 30, 30); }
        internal int Selected
        { get { return Tracks.Count == 1 ? 0 : (Video ? access.Video : access.Audio).Selected; } }
        internal MediaTrack Current
        {
            get
            {
                int index = Selected;
                // A display fallback is never a request to change PotPlayer's selected stream.
                return index >= 0 && index < Tracks.Count ? Tracks[index] : Tracks.Count > 0 ? Tracks[0] : null;
            }
        }
        internal string DisplayText
        { get { return Current != null ? Current.Summary(Language) : ""; } }
        internal int PreferredWidth(float scale)
        {
            using (Font font = new Font("Microsoft YaHei UI", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                int width = TextRenderer.MeasureText(DisplayText, font).Width + (int)(20 * scale);
                foreach (MediaTrack track in Tracks)
                    width = Math.Max(width, TextRenderer.MeasureText(track.Summary(Language), font).Width + (int)(20 * scale) +
                        (track.Atmos ? DolbyMark.Advance(font, Language.Spatial(track.Spatial), (int)(40 * scale), scale) : 0));
                return width;
            }
        }
        internal void UpdatePlayer(IntPtr player, Rectangle position, bool barVisible)
        {
            if (!barVisible || player == IntPtr.Zero || Tracks.Count == 0 || position.Width < 30) { Dismiss(); return; }
            if (!PlaceInControlBar(player, position)) return;
            Cursor = Tracks.Count > 1 ? Cursors.Hand : Cursors.Default;
            string text = DisplayText;
            if (text != lastText) { lastText = text; Invalidate(); }
            Reveal();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float scale = Height / 40f;
            Color color = Tracks.Count == 1 ? Color.FromArgb(139, 139, 145) : Color.FromArgb(218, 218, 218);
            Rectangle rect = Rectangle.Inflate(ClientRectangle, -(int)(10 * scale), 0);
            using (Font font = new Font("Microsoft YaHei UI", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                MediaTrack track = Current;
                if (track != null && track.Atmos)
                {
                    if (track.HighResolution)
                    {
                        string badge = "Hi-Res ";
                        int width = TextRenderer.MeasureText(badge, font).Width;
                        TextRenderer.DrawText(e.Graphics, badge, font, new Rectangle(rect.Left, 0, width, Height), color, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        rect.X += width; rect.Width -= width;
                    }
                    int logo = DolbyMark.Draw(e.Graphics, rect.Left, rect.Top, rect.Height, font, Language.Spatial(track.Spatial), scale, color);
                    rect.X += logo; rect.Width -= logo;
                    TextRenderer.DrawText(e.Graphics, MediaTrack.Join(Language.Spatial(track.Spatial), Language.ChannelLabel(track.Channels)), font, rect, color,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
                else TextRenderer.DrawText(e.Graphics, DisplayText, font, rect, color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }
    }
    internal sealed class StreamPopup : LayeredSurface
    {
        readonly StreamButton button;
        readonly NativeTrackAccess access;
        readonly VScrollBar scroll = new VScrollBar();
        IntPtr hoverPlayer;
        long hoverSince, lastInside;
        bool requested;
        float scale = 1;
        int hovered = -1, pressedRow = -1, visibleRows;
        internal StreamPopup(StreamButton button, NativeTrackAccess access) : base(button.Video ? "视频流" : "音频流")
        {
            this.button = button; this.access = access;
            Controls.Add(scroll); scroll.Visible = false; scroll.ValueChanged += delegate { Invalidate(); };
        }
        internal bool IsHoverTarget(IntPtr player, Native.Point point)
        { return player != IntPtr.Zero && button.Tracks.Count > 1 &&
            (button.Player == player && button.IsOver(point) || Player == player && IsOver(point)); }
        internal void UpdateHover(IntPtr player, long now)
        {
            Native.Point point;
            if (player == IntPtr.Zero || button.Tracks.Count <= 1 || !button.Visible || !Native.GetCursorPos(out point))
            { Dismiss(); hoverPlayer = IntPtr.Zero; requested = false; return; }
            bool overButton = button.Player == player && button.IsOver(point), overPanel = Player == player && IsOver(point);
            if (overButton)
            {
                if (hoverPlayer != player) { hoverPlayer = player; hoverSince = now; requested = false; }
                lastInside = now;
            }
            else if (!overPanel) { hoverPlayer = IntPtr.Zero; requested = false; }
            if (overPanel) lastInside = now;
            if (Visible && Player != player) Dismiss();
            if (Visible && !overButton && !overPanel && now - lastInside >= 250) { Dismiss(); return; }
            if (!Visible && (!overButton || now - hoverSince < 120)) return;
            if (!requested && overButton)
            { requested = access.Request(button.Video, button.Tracks, now); }
            NativeTrackAccess.Selection selection = button.Video ? access.Video : access.Audio;
            if (selection.Loading) return;
            Player = player; scale = Math.Max(96, Native.GetDpiForWindow(player)) / 96f;
            Rectangle screen = Screen.FromHandle(player).Bounds, anchor = button.ScreenBounds;
            visibleRows = Math.Max(1, Math.Min(button.Tracks.Count, (int)((anchor.Top - screen.Top - 12 * scale) / (34 * scale))));
            int width = (int)(300 * scale);
            using (Font label = new Font("Microsoft YaHei UI", 13 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font detail = new Font("Consolas", 13 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                foreach (MediaTrack track in button.Tracks)
                {
                    int labelWidth = track.Label.Length == 0 ? 0 : Math.Min((int)(170 * scale), TextRenderer.MeasureText(track.Label, label).Width) + (int)(12 * scale);
                    width = Math.Max(width, labelWidth + TextRenderer.MeasureText(track.Details(button.Language), detail).Width + (int)(34 * scale) +
                        (track.Atmos ? DolbyMark.Advance(detail, button.Language.Spatial(track.Spatial), (int)(34 * scale), scale) : 0));
                }
            width = Math.Min(width, Math.Min((int)(850 * scale), screen.Width));
            int height = (int)Math.Round((visibleRows * 34 + 12) * scale);
            Rectangle position = new Rectangle(Math.Max(screen.Left, Math.Min(screen.Right - width, anchor.Right - width)), Math.Max(screen.Top, anchor.Top - height), width, height);
            if (Bounds != position) Bounds = position;
            scroll.Visible = button.Tracks.Count > visibleRows;
            scroll.SetBounds(Width - (int)(16 * scale) - 1, 1, (int)(16 * scale), Height - 2);
            scroll.Minimum = 0; scroll.Maximum = button.Tracks.Count - 1; scroll.LargeChange = visibleRows; scroll.SmallChange = 1;
            if (scroll.Value > button.Tracks.Count - visibleRows) scroll.Value = button.Tracks.Count - visibleRows;
            Reveal(); Invalidate();
        }
        int RowAt(int y)
        {
            int row = (int)Math.Floor((y / scale - 6) / 34);
            return row >= 0 && row < visibleRows && row + scroll.Value < button.Tracks.Count ? row + scroll.Value : -1;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        { base.OnMouseMove(e); int row = RowAt(e.Y); if (row != hovered) { hovered = row; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) pressedRow = RowAt(e.Y); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int row = RowAt(e.Y);
            if (row >= 0 && row == pressedRow)
            {
                // The native checked state is re-read before displaying a newly selected item.
                Dismiss(); hoverPlayer = IntPtr.Zero; requested = false;
                if (access.Select(button.Video, row)) access.RefreshAfterSwitch(button.Video, button.Tracks);
            }
            pressedRow = -1;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Font labelNormal = new Font("Microsoft YaHei UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font labelBold = new Font("Microsoft YaHei UI", 13 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font detailNormal = new Font("Consolas", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font detailBold = new Font("Consolas", 13 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Pen border = new Pen(Color.FromArgb(58, 59, 64), Math.Max(1, scale)))
            {
                for (int i = 0; i < visibleRows; i++)
                {
                    int index = i + scroll.Value; if (index >= button.Tracks.Count) break;
                    MediaTrack track = button.Tracks[index]; bool selected = index == button.Selected;
                    Rectangle row = new Rectangle(1, (int)((6 + i * 34) * scale), Width - 2 - (scroll.Visible ? scroll.Width : 0), (int)(34 * scale));
                    if (index == hovered) using (Brush fill = new SolidBrush(Color.FromArgb(48, 50, 55))) e.Graphics.FillRectangle(fill, row);
                    row.X += (int)(12 * scale); row.Width -= (int)(24 * scale);
                    if (track.Label.Length > 0)
                    {
                        int width = Math.Min((int)(170 * scale), TextRenderer.MeasureText(track.Label, labelBold).Width);
                        TextRenderer.DrawText(e.Graphics, track.Label, selected ? labelBold : labelNormal, new Rectangle(row.Left, row.Top, width, row.Height),
                            selected ? Color.FromArgb(0, 174, 236) : Color.FromArgb(168, 170, 179), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                        row.X += width + (int)(12 * scale); row.Width -= width + (int)(12 * scale);
                    }
                    Color color = selected ? Color.FromArgb(0, 174, 236) : Color.FromArgb(229, 229, 231);
                    Font detailFont = selected ? detailBold : detailNormal;
                    if (track.Atmos)
                    {
                        int logo = DolbyMark.Draw(e.Graphics, row.Left, row.Top, row.Height, detailFont, button.Language.Spatial(track.Spatial), scale, color);
                        row.X += logo; row.Width -= logo;
                    }
                    TextRenderer.DrawText(e.Graphics, track.Details(button.Language), detailFont, row, color,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }
        }
    }
    internal sealed class StreamControls : IDisposable, IMediaContext
    {
        public object Revision { get { return metadata.Catalog; } }
        public System.Collections.Generic.IList<ContainerCaption> CaptionTracks { get { return metadata.Catalog.Text; } }
        public double DisplayRatio { get { MediaTrack track = Video.Current; return track == null ? 16.0 / 9 : track.DisplayRatio > 0 ? track.DisplayRatio : track.Width > 0 && track.Height > 0 ? track.Width / (double)track.Height : 16.0 / 9; } }
        public string SourcePath { get { return metadata.SourcePath; } }
        internal MediaCatalog Catalog { get { return metadata.Catalog; } }
        public bool CameraCandidate
        { get { return metadata.Catalog.Video.Exists(track => SphericalVideoProbe.Candidate(metadata.SourcePath, track.Width, track.Height, metadata.Catalog.Spherical)); } }
        readonly MetadataMonitor metadata = new MetadataMonitor();
        readonly NativeTrackAccess access = new NativeTrackAccess();
        internal readonly StreamButton Audio, Video;
        internal readonly StreamPopup AudioPopup, VideoPopup;
        MediaCatalog catalog;
        long nextLanguageRead;
        internal StreamControls()
        {
            Audio = new StreamButton(false, access); Video = new StreamButton(true, access);
            AudioPopup = new StreamPopup(Audio, access); VideoPopup = new StreamPopup(Video, access);
        }
        internal void UpdatePlayer(IntPtr player, long now, bool barVisible, bool externalLayout = false)
        {
            metadata.UpdatePlayer(player, now); access.Tick(now);
            if (catalog != metadata.Catalog || player != IntPtr.Zero && access.Player != player)
            {
                catalog = metadata.Catalog; access.Reset(player); Audio.Tracks = catalog.Audio; Video.Tracks = catalog.Video;
                AudioPopup.Dismiss(); VideoPopup.Dismiss();
            }
            if (now >= nextLanguageRead)
            { nextLanguageRead = now + 1000; Audio.Language = Video.Language = UiLanguage.Read(); Audio.Invalidate(); Video.Invalidate(); }
            if (player == IntPtr.Zero) { Audio.Dismiss(); Video.Dismiss(); AudioPopup.Dismiss(); VideoPopup.Dismiss(); return; }
            if (externalLayout) return;
            float scale = Math.Max(96, Native.GetDpiForWindow(player)) / 96f;
            Rectangle speed = Native.SpeedButtonBounds(player);
            Native.Rect rect; Native.GetWindowRect(player, out rect);
            int available = Math.Max(0, speed.Left - rect.left - (int)(320 * scale));
            int audioWidth = Audio.Tracks.Count == 0 ? 0 : Audio.PreferredWidth(scale);
            int videoWidth = Video.Tracks.Count == 0 ? 0 : Video.PreferredWidth(scale);
            int desired = audioWidth + videoWidth;
            if (desired > available && desired > 0) { audioWidth = available * audioWidth / desired; videoWidth = available - audioWidth; }
            Audio.UpdatePlayer(player, new Rectangle(speed.Left - videoWidth - audioWidth, speed.Top, audioWidth, speed.Height), barVisible);
            Video.UpdatePlayer(player, new Rectangle(speed.Left - videoWidth, speed.Top, videoWidth, speed.Height), barVisible);
        }
        internal void UpdateHover(IntPtr player, long now)
        {
            Native.Point point;
            if (Native.GetCursorPos(out point))
            {
                if (AudioPopup.IsHoverTarget(player, point)) VideoPopup.Dismiss();
                else if (VideoPopup.IsHoverTarget(player, point)) AudioPopup.Dismiss();
            }
            AudioPopup.UpdateHover(player, now); VideoPopup.UpdateHover(player, now);
        }
        internal bool IsOver(IntPtr player, Native.Point point)
        { return Audio.Player == player && Audio.IsOver(point) || Video.Player == player && Video.IsOver(point) ||
            AudioPopup.Player == player && AudioPopup.IsOver(point) || VideoPopup.Player == player && VideoPopup.IsOver(point); }
        internal bool IsHoverTarget(IntPtr player, Native.Point point)
        { return AudioPopup.IsHoverTarget(player, point) || VideoPopup.IsHoverTarget(player, point); }
        internal IntPtr FocusedPlayer()
        {
            foreach (LayeredSurface surface in new LayeredSurface[] { Audio, Video, AudioPopup, VideoPopup })
            { IntPtr player = surface.FocusedPlayer(); if (player != IntPtr.Zero) return player; }
            return access.PendingPlayer != IntPtr.Zero && Native.GetForegroundWindow() == access.PendingPlayer ? access.PendingPlayer : IntPtr.Zero;
        }
        internal void DismissPopups() { AudioPopup.Dismiss(); VideoPopup.Dismiss(); }
        public void Dispose() { metadata.Dispose(); access.Dispose(); Audio.Dispose(); Video.Dispose(); AudioPopup.Dispose(); VideoPopup.Dispose(); }
    }
}
