using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal static class VolumeScale
    {
        internal static int PercentAt(float y, float top, float bottom)
        { return Math.Max(0, Math.Min(100, (int)Math.Round((bottom - y) * 100 / (bottom - top)))); }
        internal static float PositionAt(int percent, float top, float bottom)
        { return bottom - (bottom - top) * Math.Max(0, Math.Min(100, percent)) / 100; }
    }
    internal sealed class VolumePopup : Form
    {
        const int RailTop = 38, RailBottom = 118, PanelHeight = 136;
        internal IntPtr Player { get; private set; }
        int volume;
        bool muted, dragging;
        long lastInside, lastRead, hoverSince;
        IntPtr hoverPlayer;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                // NOACTIVATE hides the popup from the taskbar without a WinForms hidden owner.
                p.ExStyle = (p.ExStyle | 0x08000000) & ~0x00040080;
                return p;
            }
        }

        internal VolumePopup()
        {
            Text = "音量"; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = true; TopMost = true; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; BackColor = Color.FromArgb(28, 29, 32);
            DoubleBuffered = true; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        internal bool IsOver(Native.Point point)
        { return Visible && Bounds.Contains(point.x, point.y) && Native.GetAncestor(Native.WindowFromPoint(point), 2) == Handle; }

        internal IntPtr FocusedPlayer()
        {
            IntPtr player = Native.FocusedPlayer();
            if (player == IntPtr.Zero && Visible && IsHandleCreated && Native.GetForegroundWindow() == Handle && Native.IsPlayer(Player))
                return Player;
            return player;
        }

        internal void UpdateHover(IntPtr player, long now)
        {
            Native.Point pointer;
            if (player == IntPtr.Zero || !Native.GetCursorPos(out pointer)) { Dismiss(); hoverPlayer = IntPtr.Zero; return; }
            Rectangle button = Native.VolumeButtonBounds(player);
            bool overButton = button.Contains(pointer.x, pointer.y) &&
                Native.IsPlayerSurface(player, pointer);
            bool overPanel = Player == player && IsOver(pointer);
            if (overButton)
            {
                if (hoverPlayer != player) { hoverPlayer = player; hoverSince = now; }
                lastInside = now;
            }
            else hoverPlayer = IntPtr.Zero;

            if (Visible && Player != player) Dismiss();
            if (overPanel || dragging) lastInside = now;
            if (!Visible && (!overButton || now - hoverSince < 120)) return;
            if (Visible && !overButton && !overPanel && !dragging && now - lastInside >= 250) { Dismiss(); return; }

            float scale = Math.Max(96, Native.GetDpiForWindow(player)) / 96f;
            Rectangle screen = Screen.FromHandle(player).Bounds;
            Size size = new Size((int)Math.Round(54 * scale), (int)Math.Round(PanelHeight * scale));
            int left = button.Left + (button.Width - size.Width) / 2;
            int top = button.Top - size.Height;
            Rectangle position = new Rectangle(
                Math.Max(screen.Left, Math.Min(screen.Right - size.Width, left)),
                Math.Max(screen.Top, Math.Min(screen.Bottom - size.Height, top)), size.Width, size.Height);
            if (Bounds != position) Bounds = position;
            Player = player;
            if (now - lastRead >= 80 || !Visible)
            {
                long level, silence;
                if (Native.Get(player, Native.GetVolume, out level) && Native.Get(player, Native.GetMute, out silence))
                {
                    int next = (int)Math.Max(0, Math.Min(100, level));
                    bool quiet = silence != 0;
                    if (next != volume || quiet != muted) { volume = next; muted = quiet; Invalidate(); }
                }
                lastRead = now;
            }
            if (!Visible) Show();
            // PotPlayer raises its fullscreen OSD separately; keep the fader above both surfaces.
            Native.SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0013);
        }

        internal void Dismiss()
        {
            if (!Visible) return;
            dragging = false; Capture = false;
            bool focused = Native.GetForegroundWindow() == Handle;
            Hide();
            if (focused && Native.IsWindow(Player)) Native.SetForegroundWindow(Player);
        }

        void SetFromPointer(int y)
        {
            float scale = Width / 54f;
            volume = VolumeScale.PercentAt(y, RailTop * scale, RailBottom * scale); muted = volume == 0;
            Native.SetVolumePercent(Player, volume); Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || e.Y < (RailTop - 8) * Width / 54f) return;
            dragging = true; Capture = true; SetFromPointer(e.Y);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        { base.OnMouseMove(e); if (dragging) SetFromPointer(e.Y); }
        protected override void OnMouseUp(MouseEventArgs e)
        { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) { dragging = false; Capture = false; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Width / 54f, x = Width / 2f, top = RailTop * scale, bottom = RailBottom * scale;
            int shown = muted ? 0 : volume;
            float thumb = VolumeScale.PositionAt(shown, top, bottom);
            using (Pen border = new Pen(Color.FromArgb(58, 59, 64), Math.Max(1, scale)))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            using (Font font = new Font("Segoe UI", 11 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, shown + "%", font, new Rectangle(0, (int)(8 * scale), Width, (int)(22 * scale)),
                    Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using (Pen rail = new Pen(Color.FromArgb(84, 86, 92), 3 * scale))
            using (Pen fill = new Pen(Color.FromArgb(0, 174, 236), 3 * scale))
            {
                rail.StartCap = rail.EndCap = LineCap.Round; fill.StartCap = fill.EndCap = LineCap.Round;
                g.DrawLine(rail, x, top, x, bottom);
                if (shown > 0) g.DrawLine(fill, x, thumb, x, bottom);
            }
            using (Brush handle = new SolidBrush(Color.White))
                g.FillEllipse(handle, x - 4 * scale, thumb - 4 * scale, 8 * scale, 8 * scale);
        }
    }
}
