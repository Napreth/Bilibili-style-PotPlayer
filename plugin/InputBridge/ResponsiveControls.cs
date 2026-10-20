using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal enum PlayerStage { Full, Compact, Minimal }
    internal static class ResponsivePolicy
    {
        // Logical pixels. These budgets leave room for the native sidebar and our menus.
        // The client reference has the same two transitions; its exact breakpoints vary.
        internal static PlayerStage Stage(int width, int height, double scale)
        {
            double w = width / Math.Max(.5, scale), h = height / Math.Max(.5, scale);
            return w < 800 || h < 480 ? PlayerStage.Minimal : w < 1024 || h < 600 ? PlayerStage.Compact : PlayerStage.Full;
        }
    }
    internal sealed class MinimalControls : SpeedSurface
    {
        readonly Action toggleDanmaku;
        long position, duration, status, mute, volume;
        bool danmaku;
        int pressed = -1;
        internal MinimalControls(Action toggleDanmaku) : base("简洁播放控件", true) { this.toggleDanmaku = toggleDanmaku; BackColor = Color.FromArgb(30, 30, 30); }
        internal void UpdatePlayer(IntPtr player, bool active, bool comments)
        {
            if (!active || !Native.ControlBarVisible(player) || !PlaceInControlBar(player, Native.ControlRowBounds(player))) { Dismiss(); return; }
            Native.Get(player, Native.GetPosition, out position); Native.Get(player, Native.GetDuration, out duration); Native.Get(player, Native.GetPlayStatus, out status);
            Native.Get(player, Native.GetMute, out mute); Native.Get(player, Native.GetVolume, out volume); danmaku = comments;
            Reveal(); Invalidate();
        }
        float S { get { return Height / 40f; } }
        int At(int x)
        {
            float p = x / S, width = Width / S;
            return p < 32 ? 0 : p < 68 ? 1 : p < 100 ? 2 : p >= width - 41 ? 5 : p >= width - 82 ? 4 : p >= width - 120 ? 3 : -1;
        }
        static string Time(long ms)
        { long seconds = Math.Max(0, ms / 1000); return seconds >= 3600 ? (seconds / 3600) + ":" + (seconds / 60 % 60).ToString("00") + ":" + (seconds % 60).ToString("00") : (seconds / 60) + ":" + (seconds % 60).ToString("00"); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.ScaleTransform(S, S); g.SmoothingMode = SmoothingMode.AntiAlias;
            float width = Width / S;
            using (Brush white = new SolidBrush(Color.FromArgb(220, 220, 220)))
            using (Pen pen = new Pen(Color.FromArgb(220, 220, 220), 1.5f))
            {
                foreach (int n in new[] { 0, 2 })
                { float c = n == 0 ? 16 : 84; float direction = n == 0 ? -1 : 1; g.FillPolygon(white, new[] { new PointF(c + direction * 4, 20), new PointF(c - direction * 3, 15), new PointF(c - direction * 3, 25) }); g.DrawLine(pen, c + direction * 5, 15, c + direction * 5, 25); }
                if (status == 2) { g.FillRectangle(white, 44, 14, 3, 12); g.FillRectangle(white, 52, 14, 3, 12); }
                else g.FillPolygon(white, new[] { new PointF(45, 13), new PointF(45, 27), new PointF(57, 20) });
                float v = width - 61;
                g.FillPolygon(white, new[] { new PointF(v - 7, 17), new PointF(v - 3, 17), new PointF(v + 2, 13), new PointF(v + 2, 27), new PointF(v - 3, 23), new PointF(v - 7, 23) });
                if (mute != 0 || volume == 0) { g.DrawLine(pen, v + 5, 17, v + 10, 23); g.DrawLine(pen, v + 5, 23, v + 10, 17); }
                else g.DrawArc(pen, v - 2, 13, 14, 14, -55, 110);
                float f = width - 20;
                foreach (int dx in new[] { -1, 1 }) foreach (int dy in new[] { -1, 1 }) { g.DrawLine(pen, f + dx * 7, 20 + dy * 3, f + dx * 7, 20 + dy * 7); g.DrawLine(pen, f + dx * 7, 20 + dy * 7, f + dx * 3, 20 + dy * 7); }
            }
            g.ResetTransform();
            using (Font font = new Font("Microsoft YaHei UI", 11 * S, GraphicsUnit.Pixel))
            {
                Rectangle timeBox = new Rectangle((int)(106 * S), 0, Math.Max(0, Width - (int)(232 * S)), Height);
                TextRenderer.DrawText(g, Time(position) + " / " + Time(duration), font, timeBox, Color.Gainsboro, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(g, "弹", font, new Rectangle((int)((width - 120) * S), 0, (int)(38 * S), Height), danmaku ? Color.FromArgb(251,114,153) : Color.Gray, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) pressed = At(e.X); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (e.Button != MouseButtons.Left) return;
            int hit = At(e.X); if (hit != pressed) { pressed = -1; return; } pressed = -1;
            if (hit == 0) Native.Set(Player, Native.SetPlayOrder, 0); else if (hit == 1) Native.Set(Player, Native.SetPlayStatus, 0); else if (hit == 2) Native.Set(Player, Native.SetPlayOrder, 1);
            else if (hit == 3) toggleDanmaku(); else if (hit == 4) Native.ToggleMute(Player); else if (hit == 5) Native.Fullscreen(Player);
        }
    }
}
