using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace BilibiliPotPlayer
{
    // A small UI-thread hook handles pre-commit geometry. Polling the outer rectangle
    // afterwards produces a visible expansion and cannot constrain WM_SIZING smoothly.
    internal sealed class WindowLayout : IDisposable
    {
        const uint Message = 0x8317;
        readonly IntPtr player;
        IntPtr module, hook;
        long nextAttach;
        internal bool Available { get { return hook != IntPtr.Zero && GetProp(player, "BilibiliLayout.Original") != IntPtr.Zero; } }
        internal bool Sizing { get { return GetProp(player, "BilibiliLayout.Sizing") != IntPtr.Zero; } }
        internal WindowLayout(IntPtr player)
        {
            this.player = player;
            module = LoadLibrary(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PlayerLayout.dll"));
            if (module == IntPtr.Zero) return;
            uint pid; uint thread = Native.GetWindowThreadProcessId(player, out pid);
            SetProp(player, "BilibiliLayout.Attach", (IntPtr)1);
            hook = SetWindowsHookEx(4, GetProcAddress(module, "LayoutHook"), module, thread);
        }
        internal void Update(double ratio)
        {
            if (hook == IntPtr.Zero) return;
            IntPtr video = Native.VideoSurface(player);
            if (video != IntPtr.Zero) while (Native.GetParent(video) != player && Native.GetParent(video) != IntPtr.Zero) video = Native.GetParent(video);
            if (video != IntPtr.Zero) SetProp(player, "BilibiliLayout.Video", video);
            if (!Available)
            {
                if (video == IntPtr.Zero) return;
                long now = Environment.TickCount & Int32.MaxValue;
                if (now < nextAttach) return;
                nextAttach = now + 500;
                long reply; Native.TrySend(player, Message, 0, 0, out reply);
                if (!Available) return;
            }
            SetProp(player, "BilibiliLayout.Ratio", (IntPtr)(int)Math.Round(ratio * 100000));
            long unused; Native.TrySend(player, Message, 0, 0, out unused);
        }
        internal void BindChrome(IntPtr window) { SetProp(player, "BilibiliLayout.Callback", window); }
        internal static void TogglePlaylist(IntPtr player) { long unused; Native.TrySend(player, Message, 1, 0, out unused); }
        public void Dispose()
        {
            if (hook != IntPtr.Zero)
            {
                long unused; Native.TrySend(player, Message, 2, 0, out unused);
                Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero;
                RemoveProp(player, "BilibiliLayout.Attach");
                Rectangle client = Native.ClientBounds(player);
                Native.TrySend(player, 5, 0, (client.Height << 16) | (client.Width & 0xFFFF), out unused);
            }
            if (module != IntPtr.Zero) { FreeLibrary(module); module = IntPtr.Zero; }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")] static extern IntPtr SetWindowsHookEx(int kind, IntPtr callback, IntPtr module, uint thread);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr window, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr window, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window, string name);
    }
}
