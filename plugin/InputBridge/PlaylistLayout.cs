using System;
using System.Drawing;
using System.Diagnostics;

namespace BilibiliPotPlayer
{
    internal sealed class PlaylistLayout
    {
        readonly IntPtr player;
        Rectangle previous, observed, beforeResize;
        bool initialized, wasOpen, settling;
        long changedAt, ignoreUntil;
        readonly Stopwatch clock = Stopwatch.StartNew();
        internal PlaylistLayout(IntPtr player) { this.player = player; }
        internal static bool IsOpen(IntPtr player)
        {
            Rectangle client = Native.ClientBounds(player), video = Native.ClientBounds(Native.VideoSurface(player));
            return !client.IsEmpty && !video.IsEmpty && client.Right - video.Right > 100 * Math.Max(96, Native.GetDpiForWindow(player)) / 96.0;
        }
        static Rectangle Bounds(IntPtr player)
        { Native.Rect rect; return Native.GetWindowRect(player, out rect) ? Rectangle.FromLTRB(rect.left, rect.top, rect.right, rect.bottom) : Rectangle.Empty; }
        void Restore(Rectangle bounds)
        {
            if (!bounds.IsEmpty && !Native.IsFullscreen(player) && (Native.GetWindowLong(player, -16) & 0x01000000) == 0)
                Native.SetWindowPos(player, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0014);
        }
        internal void Close() { if (IsOpen(player)) ToggleCore(); }
        internal void Toggle()
        {
            Rectangle client = Native.ClientBounds(player);
            if (!IsOpen(player) && ResponsivePolicy.Stage(client.Width, client.Height, Math.Max(96, Native.GetDpiForWindow(player)) / 96.0) != PlayerStage.Full) return;
            ToggleCore();
        }
        void ToggleCore()
        {
            Rectangle before = Bounds(player); bool expectedOpen = !IsOpen(player);
            Native.SetCommand(player, 10011);
            Restore(before);
            previous = observed = Bounds(player); wasOpen = expectedOpen; initialized = true; settling = false; ignoreUntil = clock.ElapsedMilliseconds + 160;
        }
        internal void Update()
        {
            if (!Native.IsWindowVisible(player) || Native.IsIconic(player)) return;
            long now = clock.ElapsedMilliseconds; if (now < ignoreUntil) return;
            bool open = IsOpen(player); Rectangle bounds = Bounds(player);
            if (!initialized) { previous = observed = bounds; wasOpen = open; initialized = true; return; }
            // The native video rectangle settles after its outer window. Sample only once
            // both have settled; a transient old child size must never undo a user resize.
            if (bounds != observed)
            {
                if (!settling) beforeResize = previous;
                observed = bounds; changedAt = now; settling = true; return;
            }
            if (settling)
            {
                if (now - changedAt < 100) return;
                if (open != wasOpen && bounds.Size != beforeResize.Size) { Restore(beforeResize); bounds = Bounds(player); ignoreUntil = now + 160; }
                settling = false;
            }
            previous = observed = bounds; wasOpen = open;

        }
    }
}
