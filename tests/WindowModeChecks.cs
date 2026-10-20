using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using BilibiliPotPlayer;

internal static class WindowModeChecks
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); }
    static void Wait(int milliseconds) { Thread.Sleep(milliseconds); }
    static Rectangle Bounds(IntPtr player) { Native.Rect r; Native.GetWindowRect(player, out r); return Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom); }
    static bool Property(IntPtr player, string name) { return GetProp(player, "BilibiliLayout." + name) != IntPtr.Zero; }
    static void Size(IntPtr player, int width, int height) { Native.SetWindowPos(player, IntPtr.Zero, 40, 70, width, height, 0x0014); Wait(300); }
    static void Key(IntPtr player, byte key) { Native.SetForegroundWindow(player); Wait(100); keybd_event(key, 0, 0, UIntPtr.Zero); Wait(30); keybd_event(key, 0, 2, UIntPtr.Zero); Wait(350); }
    static void Open(IntPtr player, bool value) { if (Property(player, "Intent") != value) Native.SetCommand(player, 10011); Wait(200); }
    internal static void Run()
    {
        List<IntPtr> players = Native.PlayerWindows();
        if (players.Count != 1) throw new Exception("Open exactly one test player with the input bridge running.");
        IntPtr player = players[0]; Rectangle original = Bounds(player); bool wanted = Property(player, "Intent");
        try
        {
            for (int i = 0; i < 100 && !Property(player, "Original"); i++) Wait(100);
            Check(Property(player, "Original"), "UI-thread layout hook is attached");
            if (Native.IsFullscreen(player)) Native.Fullscreen(player);
            Size(player, 1100, 700); Open(player, true);
            Check(Property(player, "Open") && PlaylistLayout.IsOpen(player), "sidebar opens inside the player");
            Rectangle stable = Bounds(player); var samples = new List<Rectangle>(); bool finished = false;
            Thread sampler = new Thread(delegate() { while (!Volatile.Read(ref finished)) { lock (samples) samples.Add(Bounds(player)); Thread.Sleep(1); } });
            long clamped = GetProp(player, "BilibiliLayout.ClampedCount").ToInt64();
            sampler.Start();
            try { for (int i = 0; i < 8; i++) { Native.SetCommand(player, 10011); Wait(100); } }
            finally { Volatile.Write(ref finished, true); sampler.Join(); }
            Check(samples.Count > 20 && samples.TrueForAll(rect => rect == stable), "outer window never expands during eight playlist toggles (" + samples.Count + " samples)");
            Check(GetProp(player, "BilibiliLayout.ClampedCount").ToInt64() > clamped, "native resize requests are constrained before commit");
            Size(player, 900, 650);
            Check(ResponsivePolicy.Stage(900, 650, 1) == PlayerStage.Normal, "sidebar suppression keeps normal playback controls");
            Check(!Property(player, "Open") && Property(player, "Intent") && !PlaylistLayout.IsOpen(player), "small normal window hides sidebar but remembers open intent");
            Native.SetCommand(player, 10011); Wait(150);
            Check(!Property(player, "Open"), "native playlist shortcut cannot open a suppressed sidebar");
            Size(player, 1100, 700);
            Check(Property(player, "Open") && PlaylistLayout.IsOpen(player), "growing the window automatically restores the sidebar");
            Open(player, false); Size(player, 900, 650); Size(player, 1100, 700);
            Check(!Property(player, "Open") && !Property(player, "Intent"), "explicitly closed sidebar stays closed after resize");
            Open(player, true); Key(player, 0x53);
            Rectangle client = Native.ClientBounds(player), video = Native.ClientBounds(Native.VideoSurface(player));
            Check(Property(player, "Small") && client.Width < 800 && !Property(player, "Open") && Property(player, "Intent"), "S enters small-window mode and retains sidebar intent");
            Check(Math.Abs(video.Top - client.Top - 3) <= 2 && Math.Abs(video.Bottom - client.Bottom + 3) <= 2, "small-window video covers the former native header and footer");
            CaptionOptions before = CaptionOptions.Load(); Key(player, 0x43); CaptionOptions after = CaptionOptions.Load();
            Check(after.SubtitlesEnabled != before.SubtitlesEnabled, "C toggles subtitle visibility");
            Check(after.DanmakuEnabled == before.DanmakuEnabled, "C does not change danmaku visibility");
            Key(player, 0x43); Key(player, 0x53); client = Native.ClientBounds(player);
            Check(!Property(player, "Small") && client.Width == 1024 && client.Height == 600, "S restores the minimum normal size that permits a sidebar");
            Check(Property(player, "Open") && PlaylistLayout.IsOpen(player), "S restores the remembered sidebar");
            Console.WriteLine("PASS: " + checks + " live window checks");
        }
        finally
        {
            Size(player, Math.Max(1024, original.Width), Math.Max(600, original.Height)); Open(player, wanted);
            Native.SetWindowPos(player, IntPtr.Zero, original.X, original.Y, original.Width, original.Height, 0x0014);
        }
    }
    internal static void CheckAspect()
    {
        IntPtr player = Native.PlayerWindows()[0];
        double ratio = GetProp(player, "BilibiliLayout.Ratio").ToInt64() / 100000.0;
        Rectangle client = Native.ClientBounds(player), video = Native.ClientBounds(Native.VideoSurface(player));
        Check(Property(player, "Small") && ratio > 0, "aspect lock remains active in small-window mode");
        Check(GetProp(player, "BilibiliLayout.AspectResizeCount").ToInt64() > 0, "native sizing gestures used the aspect constraint");
        Check(Math.Abs((client.Width - 6) / (double)(client.Height - 6) - ratio) < .01, "resized window follows the locked video ratio");
        Check(video.Width == client.Width - 6 && video.Height == client.Height - 6, "locked viewport follows the window size");
        Console.WriteLine("Client " + client.Size + "; video " + video.Size + "; ratio " + ratio);
    }
    static void Click(int x, int y)
    { Native.SetCursorPos(x, y); Wait(120); Native.VirtualLeft(2); Wait(60); Native.VirtualLeft(4); Wait(250); }
    static void Drag(int x, int y, int dx, int dy)
    {
        Native.SetCursorPos(x, y); Wait(150); Native.VirtualLeft(2); Wait(120);
        try { for (int i = 1; i <= 12; i++) { Native.SetCursorPos(x + dx * i / 12, y + dy * i / 12); Wait(40); } }
        finally { Native.VirtualLeft(4); }
        Wait(350);
    }
    internal static void CheckAspectGestures()
    {
        IntPtr player = Native.PlayerWindows()[0]; Native.SetForegroundWindow(player); Wait(200);
        Size(player, 646, 410); Rectangle client = Native.ClientBounds(player);
        if (GetProp(player, "BilibiliLayout.Ratio") != IntPtr.Zero) Click(client.Right - 66, client.Bottom - 25);
        Size(player, 646, 410); client = Native.ClientBounds(player);
        Click(client.Right - 66, client.Bottom - 25);
        Check(GetProp(player, "BilibiliLayout.Ratio") != IntPtr.Zero, "clicking the aspect button enables the lock");
        client = Native.ClientBounds(player);
        Check(Math.Abs((client.Width - 6) / (double)(client.Height - 6) - 16.0 / 9) < .01, "enabling the lock immediately corrects a mismatched window");
        Rectangle before = Bounds(player);
        long hit; Native.TrySend(player, 0x84, 0, ((long)(before.Top + before.Height / 2) << 16) | (uint)(before.Right - 1), out hit);
        Check(hit == 11, "right window border is a native resize target");
        Drag(before.Right - 1, before.Top + before.Height / 2, 80, 0);
        Rectangle after = Bounds(player);
        Check(after.Width > before.Width + 50 && after.Height > before.Height + 20, "dragging the right border adjusts width and height together");
        CheckAspect(); before = Bounds(player);
        Drag(before.Left + before.Width / 2, before.Bottom - 1, 0, 20); after = Bounds(player);
        Check(after.Height > before.Height + 10 && after.Width > before.Width + 15, "dragging the bottom border adjusts height and width together");
        CheckAspect();
        Rectangle small = Native.ClientBounds(player);
        Key(player, 0x46); Check(Native.IsFullscreen(player), "F enters fullscreen from small-window mode");
        Key(player, 0x1B); Check(Property(player, "Small") && Native.ClientBounds(player).Size == small.Size, "Escape restores the small window and its size");
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
