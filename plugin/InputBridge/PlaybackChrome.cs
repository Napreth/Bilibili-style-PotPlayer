using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    // One per-pixel-alpha child owns the chrome. Transparent pixels and the video
    // remain native hit targets; menus use logical anchors on the same drawing.
    internal sealed class PlaybackChrome : SpeedSurface
    {
        sealed class Hit { internal string Id; internal RectangleF Bounds; }
        readonly PlayerControls owner;
        readonly List<Hit> hits = new List<Hit>();
        readonly ToolTip tip = new ToolTip();
        readonly ChromeActivity activity = new ChromeActivity();
        Bitmap bitmap;
        Rectangle video;
        Rectangle lastVideo, lastClient;
        bool lastSmall;
        float scale;
        bool small, controls, seeking;
        long position, duration, status, speed, mute, volume, nextFrame;
        float seekRatio;
        string pressed, hovered;
        RectangleF seek, row, header, volumeAnchor;
        internal bool ControlsVisible { get { return controls && Visible; } }
        internal new bool IsOver(Native.Point point)
        {
            Rectangle bounds = ScreenBounds;
            return Visible && bounds.Contains(point.x, point.y) && HitArea((point.x - bounds.Left) / scale, (point.y - bounds.Top) / scale) && Native.WindowFromPoint(point) == Handle;
        }
        internal PlaybackChrome(PlayerControls owner) : base("播放控制栏", true) { this.owner = owner; }
        protected override CreateParams CreateParams
        { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x80000; return p; } }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x8318) { owner.Mode.Toggle(); message.Result = IntPtr.Zero; return; }
            if (message.Msg == 0x84)
            {
                Native.Point point; Native.GetCursorPos(out point);
                Point local = PointToClient(new Point(point.x, point.y));
                if (!HitArea(local.X / scale, local.Y / scale)) { message.Result = (IntPtr)(-1); return; }
            }
            base.WndProc(ref message);
        }
        bool HitArea(float x, float y)
        {
            if (small && controls && header.Contains(x, y)) return true;
            if (controls && (row.Contains(x, y) || seek.Contains(x, y))) return true;
            return hits.Exists(hit => hit.Bounds.Contains(x, y));
        }
        string At(float x, float y)
        { Hit hit = hits.FindLast(item => item.Bounds.Contains(x, y)); return hit == null ? null : hit.Id; }
        Rectangle ScreenRect(RectangleF rect)
        { Rectangle client = Native.ClientBounds(Player); return new Rectangle(client.Left + (int)(rect.X * scale), client.Top + (int)(rect.Y * scale), (int)(rect.Width * scale), (int)(rect.Height * scale)); }
        void SetButtonAnchor(SpeedSurface button, RectangleF bounds)
        { button.SetAnchor(Player, ScreenRect(bounds), Handle, controls && !small && bounds.Width > 0); }
        internal void UpdatePlayer(long now)
        {
            Player = owner.Player;
            if (Native.IsIconic(Player) || !Native.IsWindowVisible(Player)) { Dismiss(); return; }
            Rectangle client = Native.ClientBounds(Player);
            if (client.IsEmpty || !PlaceInPlayer(Player, client)) { Dismiss(); return; }
            scale = Math.Max(96, Native.GetDpiForWindow(Player)) / 96f;
            small = owner.Mode.Stage == PlayerStage.Small;
            video = ControlLayout.RelativeTo(Native.ClientBounds(Native.VideoSurface(Player)), client);
            if (client != lastClient || video != lastVideo || small != lastSmall) nextFrame = 0;
            lastClient = client; lastVideo = video; lastSmall = small;
            Native.Point pointer; Native.GetCursorPos(out pointer);
            IntPtr target = Native.WindowFromPoint(pointer);
            bool inside = (small ? client : ScreenVideo(client)).Contains(pointer.x, pointer.y) && Native.GetAncestor(target, 2) == Player;
            bool menu = owner.Volume.IsOver(pointer) || owner.SpeedMenu.IsOver(pointer) || owner.Streams.IsOver(Player, pointer) || owner.SubtitleMenu.IsOver(pointer);
            bool show = !owner.Layout.Sizing && activity.Visible(now, pointer.x, pointer.y, inside, Native.IsFullscreen(Player), menu || seeking);
            bool changed = controls != show; controls = show;
            if (now < nextFrame && !changed) { RaiseBelowMenu(); return; }
            nextFrame = now + 80;
            Native.Get(Player, Native.GetPosition, out position); Native.Get(Player, Native.GetDuration, out duration); Native.Get(Player, Native.GetPlayStatus, out status);
            Native.Get(Player, Native.GetSpeed, out speed); Native.Get(Player, Native.GetMute, out mute); Native.Get(Player, Native.GetVolume, out volume);
            if (bitmap == null || bitmap.Width != Width || bitmap.Height != Height)
            { if (bitmap != null) bitmap.Dispose(); bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppPArgb); }
            hits.Clear();
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent); g.ScaleTransform(scale, scale); g.SmoothingMode = SmoothingMode.AntiAlias;
                float width = Width / scale, height = Height / scale;
                if (controls)
                {
                    float left = video.Left / scale, right = video.Right / scale, bottom = video.Bottom / scale;
                    row = new RectangleF(left, bottom - 44, right - left, 44);
                    seek = new RectangleF(left + 12, bottom - 62, right - left - 24, 18);
                    using (LinearGradientBrush back = new LinearGradientBrush(new PointF(0, bottom - 98), new PointF(0, bottom), Color.Transparent, Color.FromArgb(205, 0, 0, 0)) { WrapMode = WrapMode.TileFlipXY }) g.FillRectangle(back, left, bottom - 98, right - left, 98);
                    DrawSeek(g);
                    if (small)
                    {
                        header = new RectangleF(3, 3, width - 6, 40);
                        using (LinearGradientBrush back = new LinearGradientBrush(new PointF(0, 3), new PointF(0, 65), Color.FromArgb(210, 0, 0, 0), Color.Transparent) { WrapMode = WrapMode.TileFlipXY }) g.FillRectangle(back, 3, 3, width - 6, 62);
                        Label(g, Title(), new RectangleF(14, 3, Math.Max(1, width - 158), 40), Color.White, 13);
                        Button(g, "pin", new RectangleF(width - 129, 3, 42, 40), "pin", (Native.GetWindowLong(Player, -20) & 8) != 0);
                        Button(g, "mode", new RectangleF(width - 87, 3, 42, 40), "mode");
                        Button(g, "close", new RectangleF(width - 45, 3, 42, 40), "close");
                        float buttonWidth = right - left < 420 ? 34 : 42, toggleWidth = right - left < 420 ? 30 : 36, timeWidth = right - left < 420 ? 90 : 116;
                        Label(g, Time(position) + " / " + Time(duration), new RectangleF(left + 14, row.Y, timeWidth, row.Height), Color.White, 12);
                        Button(g, "subtitles", new RectangleF(left + timeWidth + 16, row.Y, toggleWidth, row.Height), "字", owner.Captions.Options.SubtitlesEnabled);
                        Button(g, "danmaku", new RectangleF(left + timeWidth + 16 + toggleWidth, row.Y, toggleWidth, row.Height), "弹", owner.Captions.Options.DanmakuEnabled);
                        volumeAnchor = new RectangleF(right - 3 * buttonWidth, row.Y, buttonWidth, row.Height);
                        Button(g, "volume", volumeAnchor, "volume");
                        Button(g, "aspect", new RectangleF(right - 2 * buttonWidth, row.Y, buttonWidth, row.Height), "aspect", owner.Mode.AspectLocked);
                        Button(g, "full", new RectangleF(right - buttonWidth, row.Y, buttonWidth, row.Height), "full");
                        float middle = (left + right) / 2, cy = (video.Top + video.Bottom) / (2 * scale);
                        for (int i = -1; i <= 1; i++)
                        {
                            RectangleF circle = new RectangleF(middle + i * 62 - 24, cy - 24, 48, 48);
                            using (Brush b = new SolidBrush(Color.FromArgb(95, 0, 0, 0))) g.FillEllipse(b, circle);
                            Button(g, i < 0 ? "prev" : i > 0 ? "next" : "play", circle, i < 0 ? "prev" : i > 0 ? "next" : "play");
                        }
                    }
                    else DrawNormal(g, left, right);
                }
            }
            if (!controls || small)
            { owner.Speed.Dismiss(); owner.Subtitles.Dismiss(); owner.Streams.Audio.Dismiss(); owner.Streams.Video.Dismiss(); }
            owner.Volume.ButtonAnchor = controls ? ScreenRect(volumeAnchor) : Rectangle.Empty;
            owner.Volume.AnchorSurface = Handle;
            Reveal(false); TimedOverlay.Present(Handle, bitmap); RaiseBelowMenu();
        }
        Rectangle ScreenVideo(Rectangle client)
        { return new Rectangle(client.Left + video.Left, client.Top + video.Top, video.Width, video.Height); }
        void RaiseBelowMenu()
        {
            if (!Visible) return;
            IntPtr after = owner.SubtitleMenu.Visible ? owner.SubtitleMenu.Handle : IntPtr.Zero;
            if (Native.GetWindow(Handle, 3) != after) Native.SetWindowPos(Handle, after, 0, 0, 0, 0, 0x0013);
        }
        void DrawNormal(Graphics g, float left, float right)
        {
            Button(g, "prev", new RectangleF(left, row.Y, 36, 44), "prev");
            Button(g, "play", new RectangleF(left + 36, row.Y, 40, 44), "play");
            Button(g, "next", new RectangleF(left + 76, row.Y, 36, 44), "next");
            Label(g, Time(position) + " / " + Time(duration), new RectangleF(left + 122, row.Y, 152, 44), Color.White, 12);
            Button(g, "full", new RectangleF(right - 41, row.Y, 41, 44), "full");
            volumeAnchor = new RectangleF(right - 82, row.Y, 41, 44);
            Button(g, "volume", volumeAnchor, "volume");
            RectangleF sub = new RectangleF(right - 174, row.Y, 92, 44), rate = new RectangleF(right - 232, row.Y, 58, 44);
            Label(g, UiLanguage.Read().Subtitles, sub, Color.White, 12, true); hits.Add(new Hit { Id = "menu", Bounds = sub }); SetButtonAnchor(owner.Subtitles, sub);
            Label(g, speed == 1000 ? "倍速" : RateText(speed), rate, Color.White, 12, true); hits.Add(new Hit { Id = "speed", Bounds = rate }); SetButtonAnchor(owner.Speed, rate);
            float available = Math.Max(0, right - left - 520), aw = owner.Streams.Audio.Tracks.Count == 0 ? 0 : owner.Streams.Audio.PreferredWidth(1), vw = owner.Streams.Video.Tracks.Count == 0 ? 0 : owner.Streams.Video.PreferredWidth(1);
            if (aw + vw > available && aw + vw > 0) { aw = available * aw / (aw + vw); vw = available - aw; }
            RectangleF audio = new RectangleF(rate.Left - aw - vw, row.Y, aw, 44), videoBox = new RectangleF(rate.Left - vw, row.Y, vw, 44);
            DrawStream(g, owner.Streams.Audio, audio); DrawStream(g, owner.Streams.Video, videoBox);
        }
        void DrawStream(Graphics g, StreamButton button, RectangleF bounds)
        {
            SetButtonAnchor(button, bounds); if (bounds.Width < 1) return;
            hits.Add(new Hit { Id = button.Video ? "video" : "audio", Bounds = bounds });
            Color color = button.Tracks.Count <= 1 ? Color.FromArgb(170, 170, 176) : Color.White;
            RectangleF text = bounds; text.X += 8; text.Width -= 16;
            MediaTrack track = button.Current;
            if (track != null && track.Atmos)
            {
                if (track.HighResolution) { Label(g, "Hi-Res", new RectangleF(text.X, text.Y, 42, text.Height), color, 12); text.X += 44; text.Width -= 44; }
                using (Font font = new Font("Microsoft YaHei UI", 12, GraphicsUnit.Pixel))
                { int mark = DolbyMark.Draw(g, text.X, (int)text.Y, (int)text.Height, font, button.Language.Spatial(track.Spatial), 1, color); text.X += mark; text.Width -= mark; }
                Label(g, MediaTrack.Join(button.Language.Spatial(track.Spatial), button.Language.ChannelLabel(track.Channels)), text, color, 12);
            }
            else Label(g, button.DisplayText, text, color, 12);
        }
        void DrawSeek(Graphics g)
        {
            float y = seek.Y + seek.Height / 2, ratio = seeking ? seekRatio : duration > 0 ? Math.Max(0, Math.Min(1, position / (float)duration)) : 0;
            using (Pen rail = new Pen(Color.FromArgb(110, 225, 225, 225), 2)) using (Pen fill = new Pen(Color.FromArgb(251, 114, 153), 2)) using (Brush thumb = new SolidBrush(Color.White))
            { g.DrawLine(rail, seek.Left, y, seek.Right, y); g.DrawLine(fill, seek.Left, y, seek.Left + ratio * seek.Width, y); g.FillEllipse(thumb, seek.Left + ratio * seek.Width - 3, y - 3, 6, 6); }
            hits.Add(new Hit { Id = "seek", Bounds = seek });
        }
        static void Label(Graphics g, string text, RectangleF bounds, Color color, float size, bool center = false)
        {
            if (bounds.Width <= 0) return;
            using (FontFamily family = new FontFamily("Microsoft YaHei UI")) using (GraphicsPath path = new GraphicsPath())
            using (StringFormat format = new StringFormat { Alignment = center ? StringAlignment.Center : StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter }) using (Brush b = new SolidBrush(color))
            { GraphicsState state = g.Save(); g.SetClip(bounds, CombineMode.Intersect); path.AddString(text, family, 0, size, bounds, format); g.FillPath(b, path); g.Restore(state); }
        }
        void Button(Graphics g, string id, RectangleF bounds, string icon, bool selected = false)
        {
            hits.Add(new Hit { Id = id, Bounds = bounds });
            // Layered windows hit-test alpha, including the space between icon strokes.
            using (Brush target = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillRectangle(target, bounds);
            Color color = selected ? Color.FromArgb(251, 114, 153) : Color.White;
            if (hovered == id) using (Brush hover = new SolidBrush(Color.FromArgb(32, 255, 255, 255))) g.FillRectangle(hover, bounds);
            float x = bounds.X + bounds.Width / 2, y = bounds.Y + bounds.Height / 2;
            using (Pen p = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) using (Brush b = new SolidBrush(color))
            {
                if (icon == "字" || icon == "弹") { g.DrawRectangle(p, x - 9, y - 8, 18, 16); Label(g, icon, new RectangleF(x - 8, y - 9, 16, 18), color, 12, true); }
                else if (icon == "prev" || icon == "next") { int d = icon == "prev" ? -1 : 1; g.FillPolygon(b, new[] { new PointF(x + d * 5, y), new PointF(x - d * 4, y - 6), new PointF(x - d * 4, y + 6) }); g.DrawLine(p, x + d * 6, y - 6, x + d * 6, y + 6); }
                else if (icon == "play") { if (status == 2) { g.FillRectangle(b, x - 5, y - 7, 3, 14); g.FillRectangle(b, x + 2, y - 7, 3, 14); } else g.FillPolygon(b, new[] { new PointF(x - 4, y - 8), new PointF(x - 4, y + 8), new PointF(x + 8, y) }); }
                else if (icon == "volume") { g.FillPolygon(b, new[] { new PointF(x - 8, y - 3), new PointF(x - 4, y - 3), new PointF(x + 1, y - 8), new PointF(x + 1, y + 8), new PointF(x - 4, y + 3), new PointF(x - 8, y + 3) }); if (mute != 0 || volume == 0) { g.DrawLine(p, x + 5, y - 3, x + 10, y + 3); g.DrawLine(p, x + 5, y + 3, x + 10, y - 3); } else { g.DrawArc(p, x - 3, y - 6, 14, 12, -60, 120); g.DrawArc(p, x - 6, y - 9, 22, 18, -55, 110); } }
                else if (icon == "full" || icon == "aspect")
                {
                    foreach (int dx in new[] { -1, 1 }) foreach (int dy in new[] { -1, 1 }) { g.DrawLine(p, x + dx * 8, y + dy * 3, x + dx * 8, y + dy * 7); g.DrawLine(p, x + dx * 8, y + dy * 7, x + dx * 3, y + dy * 7); }
                    if (icon == "aspect") { g.DrawRectangle(p, x - 3, y - 1, 6, 6); if (selected) g.DrawArc(p, x - 2, y - 5, 4, 7, 180, 180); else g.DrawArc(p, x, y - 5, 4, 7, 180, 180); }
                }
                else if (icon == "mode") { g.DrawRectangle(p, x - 7, y - 6, 14, 11); g.FillRectangle(b, x + 1, y, 8, 7); }
                else if (icon == "close") { g.DrawLine(p, x - 6, y - 6, x + 6, y + 6); g.DrawLine(p, x - 6, y + 6, x + 6, y - 6); }
                else if (icon == "pin") { g.DrawPolygon(p, new[] { new PointF(x - 5, y - 7), new PointF(x + 5, y - 7), new PointF(x + 3, y - 3), new PointF(x + 6, y + 2), new PointF(x - 6, y + 2), new PointF(x - 3, y - 3) }); g.DrawLine(p, x, y + 2, x, y + 9); }
            }
        }
        string Title() { return String.IsNullOrEmpty(owner.Streams.SourcePath) ? "PotPlayer" : Path.GetFileNameWithoutExtension(owner.Streams.SourcePath); }
        static string Time(long ms) { long s = Math.Max(0, ms / 1000); return s >= 3600 ? (s / 3600) + ":" + (s / 60 % 60).ToString("00") + ":" + (s % 60).ToString("00") : s / 60 + ":" + (s % 60).ToString("00"); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); string hit = At(e.X / scale, e.Y / scale);
            if (hovered != hit) { hovered = hit; nextFrame = 0; tip.SetToolTip(this, hit == "mode" ? small ? "恢复窗口 (S)" : "小窗模式 (S)" : hit == "aspect" ? "锁定视频宽高比" : hit == "subtitles" ? "字幕 (C)" : hit == "danmaku" ? "弹幕 (D)" : hit == "full" ? "全屏 (F)" : ""); }
            if (seeking) { seekRatio = Math.Max(0, Math.Min(1, (e.X / scale - seek.X) / seek.Width)); nextFrame = 0; }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = null; nextFrame = 0; }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            Native.SetForegroundWindow(Player);
            pressed = At(e.X / scale, e.Y / scale);
            if (pressed == "seek") { seeking = true; Capture = true; seekRatio = Math.Max(0, Math.Min(1, (e.X / scale - seek.X) / seek.Width)); nextFrame = 0; }
            else if (pressed == null && small && header.Contains(e.X / scale, e.Y / scale)) { ReleaseCapture(); PostMessage(Player, 0x112, (IntPtr)0xF012, IntPtr.Zero); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (e.Button != MouseButtons.Left) return;
            if (seeking) { Native.Set(Player, Native.SetPosition, (long)(Math.Max(0, duration - 1) * seekRatio)); seeking = false; Capture = false; pressed = null; nextFrame = 0; return; }
            string hit = At(e.X / scale, e.Y / scale); if (hit != pressed) { pressed = null; return; } pressed = null;
            if (hit == "prev") Native.Set(Player, Native.SetPlayOrder, 0); else if (hit == "next") Native.Set(Player, Native.SetPlayOrder, 1); else if (hit == "play") { owner.Camera.UserPlaybackAction(); Native.Set(Player, Native.SetPlayStatus, 0); }
            else if (hit == "full") Native.Fullscreen(Player); else if (hit == "volume") Native.ToggleMute(Player); else if (hit == "mode") owner.Mode.Toggle(); else if (hit == "aspect") owner.Mode.ToggleAspect();
            else if (hit == "subtitles") owner.Captions.ToggleSubtitles(); else if (hit == "danmaku") owner.SubtitleMenu.ToggleDanmaku();
            else if (hit == "pin") Native.SetWindowPos(Player, (Native.GetWindowLong(Player, -20) & 8) == 0 ? (IntPtr)(-1) : (IntPtr)(-2), 0, 0, 0, 0, 0x0013);
            else if (hit == "close") PostMessage(Player, 0x10, IntPtr.Zero, IntPtr.Zero);
            nextFrame = 0;
        }
        protected override void Dispose(bool disposing) { if (disposing) { tip.Dispose(); if (bitmap != null) bitmap.Dispose(); } base.Dispose(disposing); }
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    }
}
