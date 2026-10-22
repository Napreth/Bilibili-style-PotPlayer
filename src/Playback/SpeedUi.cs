using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal sealed class SpeedButton : LayeredSurface
    {
        long speed = 1000, lastRead;
        internal SpeedButton() : base("倍速按钮", true) { BackColor = Color.FromArgb(30, 30, 30); }
        internal void UpdatePlayer(IntPtr player, long now)
        {
            if (player == IntPtr.Zero || !Native.ControlBarVisible(player)) { Dismiss(); return; }
            Rectangle position = Native.SpeedButtonBounds(player);
            if (!PlaceInControlBar(player, position)) return;
            if (now - lastRead >= 80 || !Visible)
            {
                long value;
                if (Native.Get(player, Native.GetSpeed, out value) && value != speed) { speed = value; Invalidate(); }
                lastRead = now;
            }
            Reveal();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Font font = new Font("Microsoft YaHei UI", 12 * Width / 58f, FontStyle.Regular, GraphicsUnit.Pixel))
                TextRenderer.DrawText(e.Graphics, speed == 1000 ? "倍速" : RateText(speed), font, ClientRectangle,
                    Color.FromArgb(218, 218, 218), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class SpeedPopup : LayeredSurface
    {
        // PotPlayer's native rate interface supports 0.2–12×; omit the requested unsupported 0.1×.
        internal static readonly int[] Rates = { 10000, 5000, 3000, 2000, 1000, 500, 250 };
        readonly SpeedButton button;
        readonly Action<IntPtr, int> selectRate;
        long speed = 1000, lastRead, lastInside, hoverSince;
        IntPtr hoverPlayer;
        int hovered = -1, pressedRow = -1;
        const int RowHeight = 30, Inset = 6, MenuWidth = 100;
        internal SpeedPopup(SpeedButton button, Action<IntPtr, int> selectRate) : base("倍速菜单")
        { this.button = button; this.selectRate = selectRate; }
        internal bool IsHoverTarget(IntPtr player, Native.Point point)
        {
            return player != IntPtr.Zero && (Player == player && IsOver(point) ||
                button.Player == player && button.IsOver(point));
        }
        internal void UpdateHover(IntPtr player, long now)
        {
            Native.Point pointer;
            if (player == IntPtr.Zero || !button.Visible || !Native.GetCursorPos(out pointer)) { Dismiss(); hoverPlayer = IntPtr.Zero; return; }
            Rectangle anchor = button.ScreenBounds;
            bool overButton = anchor.Contains(pointer.x, pointer.y) &&
                button.Player == player && button.IsOver(pointer);
            bool overPanel = Player == player && IsOver(pointer);
            if (overButton)
            {
                if (hoverPlayer != player) { hoverPlayer = player; hoverSince = now; }
                lastInside = now;
            }
            else hoverPlayer = IntPtr.Zero;
            if (Visible && Player != player) Dismiss();
            if (overPanel) lastInside = now;
            if (!Visible && (!overButton || now - hoverSince < 120)) return;
            if (Visible && !overButton && !overPanel && now - lastInside >= 250) { Dismiss(); return; }
            float scale = Math.Max(96, Native.GetDpiForWindow(player)) / 96f;
            Size size = new Size((int)Math.Round(MenuWidth * scale), (int)Math.Round((Rates.Length * RowHeight + 2 * Inset) * scale));
            Rectangle screen = Screen.FromHandle(player).Bounds;
            int left = anchor.Left + (anchor.Width - size.Width) / 2;
            Rectangle position = new Rectangle(Math.Max(screen.Left, Math.Min(screen.Right - size.Width, left)),
                Math.Max(screen.Top, anchor.Top - size.Height), size.Width, size.Height);
            if (Bounds != position) Bounds = position;
            Player = player;
            if (now - lastRead >= 80 || !Visible)
            {
                long value;
                if (Native.Get(player, Native.GetSpeed, out value) && value != speed) { speed = value; Invalidate(); }
                lastRead = now;
            }
            Reveal();
        }
        int RowAt(int y)
        {
            float scale = Width / (float)MenuWidth;
            int row = (int)Math.Floor((y / scale - Inset) / RowHeight);
            return row >= 0 && row < Rates.Length ? row : -1;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        { base.OnMouseMove(e); int row = RowAt(e.Y); if (row != hovered) { hovered = row; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e)
        { base.OnMouseLeave(e); hovered = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) pressedRow = RowAt(e.Y); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int row = RowAt(e.Y);
            if (row >= 0 && row == pressedRow) { selectRate(Player, Rates[row]); Dismiss(); }
            pressedRow = -1;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float scale = Width / (float)MenuWidth;
            using (Font font = new Font("Segoe UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font selectedFont = new Font("Segoe UI", 13 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Pen border = new Pen(Color.FromArgb(58, 59, 64), Math.Max(1, scale)))
            {
                for (int i = 0; i < Rates.Length; i++)
                {
                    Rectangle row = new Rectangle(1, (int)Math.Round((Inset + i * RowHeight) * scale), Width - 2, (int)Math.Round(RowHeight * scale));
                    if (i == hovered) using (Brush fill = new SolidBrush(Color.FromArgb(48, 50, 55))) e.Graphics.FillRectangle(fill, row);
                    Color color = speed == Rates[i] ? Color.FromArgb(0, 174, 236) : Color.White;
                    TextRenderer.DrawText(e.Graphics, RateText(Rates[i]), speed == Rates[i] ? selectedFont : font, row, color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
