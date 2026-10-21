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
    static void Size(IntPtr player, int width, int height)
    {
        if ((Native.GetWindowLong(player, -16) & 0x01000000) != 0) { long unused; Native.TrySend(player, 0x112, 0xF120, 0, out unused); Wait(150); }
        Native.SetWindowPos(player, IntPtr.Zero, 40, 70, width, height, 0x0014); Wait(300);
    }
    static void Key(IntPtr player, byte key) { Native.SetForegroundWindow(player); Wait(100); keybd_event(key, 0, 0, UIntPtr.Zero); Wait(30); keybd_event(key, 0, 2, UIntPtr.Zero); Wait(350); }
    static void Open(IntPtr player, bool value) { if (Property(player, Property(player, "Full") ? "FullIntent" : "Intent") != value) WindowLayout.TogglePlaylist(player); Wait(200); }
    static void CheckPlaylist(IntPtr player, string mode)
    {
        IntPtr pane = GetProp(player, "BilibiliLayout.Sidebar"), list = GetProp(player, "BilibiliLayout.Playlist");
        Check(pane != IntPtr.Zero && list != IntPtr.Zero && Native.IsWindowVisible(pane) && Native.IsWindowVisible(list), mode + ": the actual playlist control is visible");
        Rectangle sidebar = Bounds(pane), rows = Bounds(list), video = Native.ClientBounds(Native.VideoSurface(player));
        Check(sidebar.Contains(rows) && rows.Width > 100 && rows.Height > 50 && video.Right == sidebar.Left, mode + ": playlist rows touch the viewport without a separator strip");
        Native.Point point = new Native.Point { x = rows.Left + rows.Width / 2, y = rows.Top + 10 };
        IntPtr target = Native.WindowFromPoint(point);
        Check(target == list || Native.IsChild(list, target), mode + ": playlist rows receive mouse input");
    }
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
            Size(player, 1100, 700); Open(player, false);
            Native.Point initialPointer, openedPointer; Native.GetCursorPos(out initialPointer);
            Rectangle stable = Bounds(player); var samples = new List<Rectangle>(); bool finished = false;
            Thread sampler = new Thread(delegate() { while (!Volatile.Read(ref finished)) { lock (samples) samples.Add(Bounds(player)); Thread.Sleep(1); } });
            sampler.Start();
            try { Open(player, true); for (int i = 0; i < 8; i++) { WindowLayout.TogglePlaylist(player); Wait(100); } }
            finally { Volatile.Write(ref finished, true); sampler.Join(); }
            Check(Property(player, "Open") && PlaylistLayout.IsOpen(player), "sidebar opens inside the player");
            Check(samples.Count > 20 && samples.TrueForAll(rect => rect == stable), "outer window never expands during initialization and eight playlist toggles (" + samples.Count + " samples)");
            Native.GetCursorPos(out openedPointer);
            Check(initialPointer.x == openedPointer.x && initialPointer.y == openedPointer.y, "playlist initialization and toggling preserve the pointer");
            CheckPlaylist(player, "after eight toggles");
            Size(player, 900, 650);
            Check(ResponsivePolicy.Stage(900, 650, 1) == PlayerStage.Normal, "sidebar suppression keeps normal playback controls");
            Check(!Property(player, "Open") && Property(player, "Intent") && !PlaylistLayout.IsOpen(player), "small normal window hides sidebar but remembers open intent");
            Native.SetCommand(player, 10011); Wait(150);
            Check(!Property(player, "Open"), "native playlist shortcut cannot open a suppressed sidebar");
            Size(player, 1100, 700);
            Check(Property(player, "Open") && PlaylistLayout.IsOpen(player), "growing the window automatically restores the sidebar");
            CheckPlaylist(player, "after restoring window size");
            Open(player, false); Size(player, 900, 650); Size(player, 1100, 700);
            Check(!Property(player, "Open") && !Property(player, "Intent"), "explicitly closed sidebar stays closed after resize");
            for (int width = 1100; width >= 650; width -= 7)
            {
                Native.SetWindowPos(player, IntPtr.Zero, 40, 70, width, 650, 0x0014);
                Check(!Property(player, "Open") && !Property(player, "Intent"), "closed sidebar remains closed while shrinking to " + width);
            }
            Size(player, 1100, 700);
            Key(player, 0x46); Check(Native.IsFullscreen(player), "normal window enters fullscreen");
            Native.Point pointerBefore, pointerAfter; Native.GetCursorPos(out pointerBefore);
            Native.Rect clipBefore, clipAfter; GetClipCursor(out clipBefore);
            Open(player, true); Native.GetCursorPos(out pointerAfter);
            GetClipCursor(out clipAfter);
            Rectangle fullClient = Native.ClientBounds(player), fullVideo = Native.ClientBounds(Native.VideoSurface(player));
            Check(PlaylistLayout.IsOpen(player) && fullVideo.Width < fullClient.Width - 100, "fullscreen sidebar reduces the playback width");
            CheckPlaylist(player, "fullscreen");
            Check(pointerBefore.x == pointerAfter.x && pointerBefore.y == pointerAfter.y, "opening the fullscreen sidebar never moves the pointer");
            Check(clipBefore.left == clipAfter.left && clipBefore.top == clipAfter.top && clipBefore.right == clipAfter.right && clipBefore.bottom == clipAfter.bottom, "fullscreen playlist initialization restores the original pointer bounds");
            Open(player, false); fullVideo = Native.ClientBounds(Native.VideoSurface(player));
            Check(fullVideo.Width == fullClient.Width, "closing the fullscreen sidebar restores the whole playback area");
            Native.SetCursorPos(fullClient.Right - 1, fullClient.Top + fullClient.Height / 2); Wait(1800);
            Check(!Property(player, "Open") && !Property(player, "Intent"), "hovering at the fullscreen right edge does not open the sidebar");
            Key(player, 0x1B);
            Open(player, true); Rectangle beforeFullscreen = Bounds(player); Key(player, 0x46);
            Check(!Property(player, "Open") && Property(player, "Intent"), "entering fullscreen suppresses the sidebar without losing windowed intent");
            Check(!Native.IsWindowVisible(GetProp(player, "BilibiliLayout.Sidebar")), "automatically suppressed fullscreen sidebar is actually hidden");
            Key(player, 0x1B);
            Check(Bounds(player) == beforeFullscreen, "automatic fullscreen suppression preserves the window's restore bounds");
            Check(Property(player, "Open"), "leaving fullscreen restores the previously open windowed sidebar");
            Open(player, false); Key(player, 0x46); Key(player, 0x58);
            Check(Property(player, "Open") && Property(player, "Intent"), "explicitly opening the fullscreen sidebar also updates windowed intent");
            Key(player, 0x1B);
            Check(Bounds(player) == beforeFullscreen, "opening the fullscreen sidebar preserves the window's restore bounds");
            Check(Property(player, "Open"), "a sidebar explicitly opened in fullscreen remains open in the window");
            Key(player, 0x46); Key(player, 0x58); Key(player, 0x58);
            Check(!Property(player, "Open") && !Native.IsWindowVisible(GetProp(player, "BilibiliLayout.Sidebar")), "X closes the real fullscreen sidebar after opening it");
            Key(player, 0x1B);
            Check(!Property(player, "Open") && !Property(player, "Intent"), "explicit fullscreen closure is remembered when returning to the window");
            long unused; Native.TrySend(player, 0x112, 0xF030, 0, out unused); Wait(300);
            Open(player, true); Rectangle maximized = Bounds(player); Key(player, 0x46);
            Check(!Property(player, "Open") && Property(player, "Intent"), "entering fullscreen from a maximized window also suppresses the sidebar");
            Key(player, 0x1B);
            Check(Bounds(player) == maximized && Property(player, "Open"), "leaving fullscreen restores maximized bounds and the remembered sidebar (before " + maximized + ", after " + Bounds(player) + ", open " + Property(player, "Open") + ")");
            Size(player, 1100, 700);
            Open(player, true); Key(player, 0x53);
            Rectangle client = Native.ClientBounds(player), video = Native.ClientBounds(Native.VideoSurface(player));
            Check(Property(player, "Small") && client.Width < 800 && !Property(player, "Open") && Property(player, "Intent"), "S enters small-window mode and retains sidebar intent");
            Check(Math.Abs(video.Top - client.Top - 3) <= 2 && Math.Abs(video.Bottom - client.Bottom + 3) <= 2, "small-window video covers the former native header and footer");
            CaptionOptions before = CaptionOptions.Load(); Key(player, 0x43); CaptionOptions after = CaptionOptions.Load();
            Check(after.SubtitlesEnabled != before.SubtitlesEnabled, "C toggles subtitle visibility");
            Check(after.DanmakuEnabled == before.DanmakuEnabled, "C does not change danmaku visibility");
            Key(player, 0x43); Key(player, 0x53); client = Native.ClientBounds(player);
            Check(!Property(player, "Small") && client.Width == 1024 && client.Height == 540, "S restores the minimum normal size that permits a sidebar");
            Check(Property(player, "Open") && PlaylistLayout.IsOpen(player), "S restores the remembered sidebar");
            Console.WriteLine("PASS: " + checks + " live window checks");
        }
        finally
        {
            if (Native.IsFullscreen(player)) Key(player, 0x1B);
            Size(player, Math.Max(1024, original.Width), Math.Max(600, original.Height)); Open(player, wanted);
            Native.SetWindowPos(player, IntPtr.Zero, original.X, original.Y, original.Width, original.Height, 0x0014);
        }
    }
    // Run with a plain white 16:9 video. HWND visibility alone cannot detect
    // stale sidebar pixels or an unchanged renderer output rectangle.
    internal static void CheckSidebarPixels()
    {
        IntPtr player = Native.PlayerWindows()[0];
        if (Native.IsFullscreen(player)) Key(player, 0x1B);
        Native.Set(player, Native.SetPlayStatus, 1); Native.Set(player, Native.SetPosition, 1000);
        Size(player, 1100, 700); Open(player, false);
        for (int mode = 0; mode < 2; mode++)
        {
            if (mode == 1) Key(player, 0x46);
            string label = mode == 0 ? "windowed" : "fullscreen";
            for (int i = 0; i < 3; i++)
            {
                Key(player, 0x58); CheckPlaylist(player, label + " pixel check open " + (i + 1));
                Rectangle openedVideo = Native.ClientBounds(Native.VideoSurface(player));
                Check(!Native.IsWindowVisible(GetProp(player, "BilibiliLayout.Splitter")), label + ": the native splitter cannot paint video-colored pixels");
                int bar = (openedVideo.Height - openedVideo.Width * 9 / 16) / 2;
                using (Bitmap column = new Bitmap(1, openedVideo.Height))
                {
                    using (Graphics g = Graphics.FromImage(column)) g.CopyFromScreen(openedVideo.Left + openedVideo.Width / 2, openedVideo.Top, 0, 0, column.Size);
                    Color top = column.GetPixel(0, bar / 2), bottom = column.GetPixel(0, openedVideo.Height - bar / 2 - 1);
                    Check(top.R < 20 && top.G < 20 && top.B < 20 && bottom.R < 20 && bottom.G < 20 && bottom.B < 20, label + ": opening the sidebar scales the picture to its narrower viewport " + (i + 1) + " (top " + top + ", bottom " + bottom + ", reflows " + GetProp(player, "BilibiliLayout.ReflowCount") + ", initializations " + GetProp(player, "BilibiliLayout.SidebarInitializedCount") + ")");
                    Color middle = column.GetPixel(0, openedVideo.Height / 2);
                    Check(middle.R > 235 && middle.G > 235 && middle.B > 235, label + ": the scaled picture is rendered " + (i + 1));
                }
                using (Bitmap row = new Bitmap(openedVideo.Width, 1))
                {
                    using (Graphics g = Graphics.FromImage(row)) g.CopyFromScreen(openedVideo.Left, openedVideo.Top + openedVideo.Height / 2, 0, 0, row.Size);
                    bool uninterrupted = true;
                    for (int x = 2; x < row.Width; x++) { Color color = row.GetPixel(x, 0); uninterrupted &= color.R > 235 && color.G > 235 && color.B > 235; }
                    Check(uninterrupted, label + ": no black column interrupts the open-sidebar picture " + (i + 1));
                }
                int seam = Bounds(GetProp(player, "BilibiliLayout.Sidebar")).Left - 2;
                Native.Point pointer; Native.GetCursorPos(out pointer);
                Key(player, 0x58); Wait(700);
                Native.Point after; Native.GetCursorPos(out after);
                Check(pointer.x == after.x && pointer.y == after.y, label + " close preserves the pointer " + (i + 1));
                Check(!Property(player, "Open") && !Native.IsWindowVisible(GetProp(player, "BilibiliLayout.Sidebar")), label + " pane is hidden " + (i + 1));
                Rectangle client = Native.ClientBounds(player), video = Native.ClientBounds(Native.VideoSurface(player));
                Check(video.Width == client.Width - 2 * GetProp(player, "BilibiliLayout.Border").ToInt32(), label + " viewport expands after closing " + (i + 1));
                using (Bitmap row = new Bitmap(client.Width, 1))
                {
                    using (Graphics g = Graphics.FromImage(row)) g.CopyFromScreen(client.Left, client.Top + client.Height / 2, 0, 0, row.Size);
                    bool white = true;
                    for (int x = seam - client.Left - 3; x <= seam - client.Left + 3; x++)
                    { Color color = row.GetPixel(x, 0); white &= color.R > 235 && color.G > 235 && color.B > 235; }
                    Check(white, label + ": no black seam remains at the former sidebar edge " + (i + 1));
                    Color right = row.GetPixel(client.Width - 80, 0);
                    Check(right.R > 235 && right.G > 235 && right.B > 235, label + ": video replaces the former sidebar pixels " + (i + 1));
                }
            }
        }
        Key(player, 0x1B);
        Console.WriteLine("PASS: " + checks + " sidebar pixel checks");
    }
    internal static void CheckSidebarResize()
    {
        IntPtr player = Native.PlayerWindows()[0];
        if (Native.IsFullscreen(player)) Key(player, 0x1B);
        double scale = Math.Max(96, Native.GetDpiForWindow(player)) / 96.0;
        for (int pass = 0; pass < 3; pass++)
        {
            Size(player, (int)(1100 * scale), (int)(700 * scale)); Open(player, true);
            long unused; Native.TrySend(player, 0x231, 0, 0, out unused);
            try
            {
                foreach (Size target in new[] {new Size(1100, 620), new Size(1100, 560), new Size(1100, 540), new Size(1100, 539), new Size(1100, 500), new Size(1100, 470), new Size(1100, 560), new Size(1024, 560), new Size(1023, 560), new Size(790, 560), new Size(1100, 560)})
                {
                    Native.SetWindowPos(player, IntPtr.Zero, 40, 70, (int)(target.Width * scale), (int)(target.Height * scale), 0x0014); Wait(40);
                    Rectangle client = Native.ClientBounds(player);
                    bool allowed = ResponsivePolicy.SidebarAllowed(client.Width, client.Height, scale);
                    Check(Property(player, "Open") == allowed && Native.IsWindowVisible(GetProp(player, "BilibiliLayout.Sidebar")) == allowed, "resize " + (pass + 1) + ": actual sidebar visibility follows " + target);
                    Check(Property(player, "Intent"), "automatic resize suppression retains explicit open intent");
                }
            }
            finally { Native.TrySend(player, 0x232, 0, 0, out unused); Wait(200); }
            CheckPlaylist(player, "after resize gesture " + (pass + 1));
        }
        Console.WriteLine("PASS: " + checks + " sidebar resize checks");
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
    static IntPtr Surface(IntPtr player, string title)
    {
        IntPtr found = IntPtr.Zero;
        Native.EnumChildWindows(player, delegate(IntPtr child, IntPtr unused) {
            var text = new System.Text.StringBuilder(100); GetWindowText(child, text, text.Capacity);
            if (text.ToString() == title && Native.IsWindowVisible(child)) found = child;
            return true;
        }, IntPtr.Zero);
        return found;
    }
    internal static void CheckChromeGestures()
    {
        IntPtr player = Native.PlayerWindows()[0]; Native.SetForegroundWindow(player);
        if (Native.IsFullscreen(player)) Key(player, 0x1B);
        Size(player, 1100, 650); Open(player, false);
        Rectangle client = Native.ClientBounds(player);
        Click(client.Right - 90, client.Top + 18);
        Check(Property(player, "Small"), "the native skin small-window button enters small mode");
        Key(player, 0x53); Size(player, 1100, 650);
        client = Native.ClientBounds(player);
        Native.SetCursorPos(client.Right - 128, client.Bottom - 25); Wait(750);
        IntPtr menu = Surface(player, "字幕和弹幕菜单");
        Check(menu != IntPtr.Zero, "hovering the caption button opens the custom menu");
        Rectangle menuRect = Bounds(menu);
        Check(menuRect.Height == CaptionDropAreas.MainHeight, "the compact caption menu fits a 650-pixel window without scaling");
        Native.Point overlap = new Native.Point {x = menuRect.Left + menuRect.Width / 2, y = menuRect.Bottom - 7};
        bool menuAlwaysAbove = true;
        for (int i = 0; i < 80; i++) { if (Native.WindowFromPoint(overlap) != menu) menuAlwaysAbove = false; Wait(4); }
        Check(menuAlwaysAbove, "the seek bar never overtakes the caption menu across repeated redraws");
        Native.SetCursorPos(client.Left + 200, client.Top + 200); Wait(700);
        // Drag across both responsive thresholds while observing native visibility.
        Size(player, 1100, 700); Open(player, false);
        bool finished = false, nativeHeaderFlashed = false, sidebarFlashed = false, controlsFlashed = false;
        IntPtr controlSurface = Surface(player, "播放控制栏");
        Thread sample = new Thread(delegate() {
            while (!Volatile.Read(ref finished)) {
                if (Property(player, "Small") && Native.IsWindowVisible(GetProp(player, "BilibiliLayout.Header"))) nativeHeaderFlashed = true;
                if (Property(player, "Open") || Property(player, "Intent") || Native.IsWindowVisible(GetProp(player, "BilibiliLayout.Sidebar"))) sidebarFlashed = true;
                Rectangle current = Native.ClientBounds(player);
                if (Property(player, "Sizing") && Native.WindowFromPoint(new Native.Point {x = current.Left + 20, y = current.Bottom - 20}) == controlSurface) controlsFlashed = true;
                Thread.Sleep(1);
            }
        });
        Rectangle before = Bounds(player); sample.Start();
        try { Drag(before.Right - 1, before.Bottom - 1, -440, -300); }
        finally { Volatile.Write(ref finished, true); sample.Join(); }
        Check(Property(player, "Small") && !nativeHeaderFlashed, "continuous resize enters small mode without showing the native title bar");
        Check(!sidebarFlashed, "continuous resize never flashes a closed sidebar");
        Check(!controlsFlashed, "hover controls remain hidden throughout the window resize gesture");
        Size(player, 1100, 650); Key(player, 0x46); client = Native.ClientBounds(player);
        Native.SetCursorPos(client.Left + client.Width / 2, client.Top + client.Height / 2); Wait(300);
        Native.Point control = new Native.Point { x = client.Left + 20, y = client.Bottom - 20 };
        IntPtr chrome = Surface(player, "播放控制栏");
        Check(chrome != IntPtr.Zero && Native.WindowFromPoint(control) == chrome, "fullscreen mouse movement shows the controls");
        Wait(3200);
        Check(Native.WindowFromPoint(control) != chrome, "fullscreen controls disappear after the mouse stays still");
        Native.Point pointer; Native.GetCursorPos(out pointer); Native.SetCursorPos(pointer.x + 10, pointer.y); Wait(200);
        Check(Native.WindowFromPoint(control) == chrome, "moving the mouse reveals fullscreen controls again");
        Key(player, 0x1B);
        client = Native.ClientBounds(player);
        Native.SetCursorPos(client.Left + client.Width / 2, client.Top + client.Height / 2); Wait(300);
        control = new Native.Point { x = client.Left + 20, y = client.Bottom - 20 };
        Check(Native.WindowFromPoint(control) == chrome, "normal window mouse movement shows the controls");
        Wait(3200);
        Check(Native.WindowFromPoint(control) != chrome, "normal window controls disappear after the mouse stays still");
        Native.GetCursorPos(out pointer); Native.SetCursorPos(pointer.x + 10, pointer.y); Wait(200);
        Check(Native.WindowFromPoint(control) == chrome, "moving the mouse reveals normal window controls again");
        Key(player, 0x53); client = Native.ClientBounds(player);
        Native.SetCursorPos(client.Left + client.Width / 2, client.Top + client.Height / 2); Wait(3200);
        control = new Native.Point { x = client.Left + 20, y = client.Bottom - 20 };
        Check(Native.WindowFromPoint(control) == chrome, "small-window controls remain visible while the stationary pointer is inside");
        Key(player, 0x53);
        Console.WriteLine("PASS: " + checks + " chrome and resize checks");
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll")] static extern bool GetClipCursor(out Native.Rect rect);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
