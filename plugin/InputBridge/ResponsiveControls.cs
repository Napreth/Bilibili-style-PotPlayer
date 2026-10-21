using System;
using System.Drawing;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal enum PlayerStage { Normal, Small }
    internal static class ResponsivePolicy
    {
        internal static PlayerStage Stage(int width, int height, double scale)
        { return width / Math.Max(.5, scale) < 800 || height / Math.Max(.5, scale) < 480 ? PlayerStage.Small : PlayerStage.Normal; }
        internal static bool SidebarAllowed(int width, int height, double scale)
        { return width / Math.Max(.5, scale) >= 1024 && height / Math.Max(.5, scale) >= 540; }
        internal static Size AspectSize(int width, int height, double ratio, bool fromHeight)
        {
            ratio = Math.Max(.1, Math.Min(10, ratio));
            return fromHeight ? new Size((int)Math.Round(height * ratio), height) : new Size(width, (int)Math.Round(width / ratio));
        }
    }
    internal sealed class WindowMode
    {
        readonly IntPtr player;
        readonly StreamControls streams;
        internal bool AspectLocked;
        internal PlayerStage Stage;
        internal WindowMode(IntPtr player, StreamControls streams) { this.player = player; this.streams = streams; }
        internal double Ratio
        {
            get
            {
                MediaTrack video = streams.Video.Current;
                if (video != null && video.DisplayRatio > 0) return video.DisplayRatio;
                return video != null && video.Width > 0 && video.Height > 0 ? video.Width / (double)video.Height : 16.0 / 9;
            }
        }
        internal void Update()
        {
            Rectangle client = Native.ClientBounds(player);
            Stage = Native.IsFullscreen(player) ? PlayerStage.Normal : ResponsivePolicy.Stage(client.Width, client.Height, Math.Max(96, Native.GetDpiForWindow(player)) / 96.0);
        }
        internal void Toggle()
        {
            if (Native.IsFullscreen(player)) Native.Fullscreen(player);
            bool restore = Stage == PlayerStage.Small;
            double scale = Math.Max(96, Native.GetDpiForWindow(player)) / 96.0;
            Rectangle work = Screen.FromHandle(player).WorkingArea;
            Size size = restore ? new Size((int)(1024 * scale), (int)(540 * scale)) : ResponsivePolicy.AspectSize((int)(640 * scale), 0, Ratio, false);
            if (!restore && size.Height > 440 * scale) size = ResponsivePolicy.AspectSize(0, (int)(440 * scale), Ratio, true);
            if (!restore) { size.Width = Math.Max((int)(320 * scale), size.Width + (int)(6 * scale)); size.Height += (int)(6 * scale); }
            Native.Rect bounds; Native.GetWindowRect(player, out bounds);
            if ((Native.GetWindowLong(player, -16) & 0x01000000) != 0) { long unused; Native.TrySend(player, 0x112, 0xF120, 0, out unused); }
            Rectangle client = Native.ClientBounds(player); Native.GetWindowRect(player, out bounds);
            size.Width += bounds.right - bounds.left - client.Width; size.Height += bounds.bottom - bounds.top - client.Height;
            int x = Math.Max(work.Left, Math.Min(work.Right - size.Width, bounds.left)), y = Math.Max(work.Top, Math.Min(work.Bottom - size.Height, bounds.top));
            Native.SetWindowPos(player, IntPtr.Zero, x, y, size.Width, size.Height, 0x0014);
            Update();
        }
        internal void ToggleAspect()
        {
            AspectLocked = !AspectLocked;
            if (!AspectLocked) return;
            Rectangle client = Native.ClientBounds(player); Native.Rect bounds; Native.GetWindowRect(player, out bounds);
            int border = (int)Math.Round(6 * Math.Max(96, Native.GetDpiForWindow(player)) / 96.0);
            Size video = ResponsivePolicy.AspectSize(Math.Max(1, client.Width - border), 0, Ratio, false);
            int height = video.Height + border + bounds.bottom - bounds.top - client.Height;
            Native.SetWindowPos(player, IntPtr.Zero, 0, 0, bounds.right - bounds.left, height, 0x0016);
        }
    }
}
