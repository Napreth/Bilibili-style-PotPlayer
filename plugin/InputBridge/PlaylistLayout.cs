using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace BilibiliPotPlayer
{
    internal sealed class PlaylistLayout
    {
        readonly IntPtr player;
        bool allowed;
        internal PlaylistLayout(IntPtr player)
        {
            this.player = player;
            bool open = IsOpen(player);
            Set("Open", open); Set("Intent", open);
        }
        internal static bool IsOpen(IntPtr player)
        {
            Rectangle client = Native.ClientBounds(player), video = Native.ClientBounds(Native.VideoSurface(player));
            return !client.IsEmpty && !video.IsEmpty && client.Right - video.Right > 100 * Math.Max(96, Native.GetDpiForWindow(player)) / 96.0;
        }
        internal bool Requested { get { return GetProp(player, "BilibiliLayout.Intent") != IntPtr.Zero; } }
        void Set(string name, bool value) { SetProp(player, "BilibiliLayout." + name, value ? (IntPtr)1 : IntPtr.Zero); }
        internal void Toggle()
        {
            if (!allowed && !Requested) return;
            if (!allowed) { Set("Intent", false); return; }
            WindowLayout.TogglePlaylist(player);
        }
        internal void Update()
        {
            // The UI-thread layout owns actual visibility. Never infer intent from
            // a renderer rectangle while the native skin is midway through resizing.
            allowed = GetProp(player, "BilibiliLayout.AllowSidebar") != IntPtr.Zero;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr window, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window, string name);
    }
}
