using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;


namespace BilibiliPotPlayer
{
    internal static class Native
    {
        static IntPtr verifiedWindow;
        static uint verifiedPid;
        internal const uint WM_USER = 0x400, WM_COMMAND = 0x111;
        // PotPlayer InternalSimpleCmd.h (GET/SET_SPEED uses 1000 = 1x).
        internal const int GetVolume = 0x5000, SetVolume = 0x5001, GetDuration = 0x5002;
        internal const int GetPosition = 0x5004, SetPosition = 0x5005;
        internal const int GetPlayStatus = 0x5006, SetPlayStatus = 0x5007, SetPlayOrder = 0x5008;
        internal const int GetMute = 0x5011, SetMute = 0x5012, GetSpeed = 0x5015, SetSpeed = 0x5016;
        internal const int GetOsd = 0x5013, SetOsd = 0x5014;
        internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] internal struct Keyboard { public uint vk, scan, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Mouse { public Point point; public uint data, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct GuiInfo
        {
            public int size; public uint flags; public IntPtr active, focus, capture, menu, move, caret; public Rect caretRect;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct StartupInfo
        {
            public int size; public IntPtr reserved, desktop, title;
            public uint x, y, width, height, charsX, charsY, fill, flags;
            public ushort show, reservedSize; public IntPtr reservedData, input, output, error;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct ProcessInfo
        { public IntPtr process, thread; public uint pid, tid; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateProcess(string app, StringBuilder command, IntPtr processSecurity,
            IntPtr threadSecurity, bool inheritHandles, uint flags, IntPtr environment,
            string folder, ref StartupInfo startup, out ProcessInfo process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        internal static void StartBackground()
        {
            StartupInfo startup = new StartupInfo(); startup.size = Marshal.SizeOf(startup);
            startup.flags = 1; // STARTF_USESHOWWINDOW / SW_HIDE.
            ProcessInfo process;
            // Do not inherit HostExecuteProgram's output pipes; it must be able to finish.
            if (!CreateProcess(Application.ExecutablePath, new StringBuilder("\"" + Application.ExecutablePath + "\""),
                IntPtr.Zero, IntPtr.Zero, false, 0x08000000, IntPtr.Zero,
                AppDomain.CurrentDomain.BaseDirectory, ref startup, out process))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            CloseHandle(process.thread); CloseHandle(process.process);
        }
        [DllImport("user32.dll")] internal static extern IntPtr SetWindowsHookEx(int kind, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
        [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
        [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { internal int x, y; internal uint data, flags, time; internal UIntPtr extra; }
        [StructLayout(LayoutKind.Explicit, Size = 40)] internal struct Input { [FieldOffset(0)] internal uint type; [FieldOffset(8)] internal MouseInput mouse; }
        [DllImport("user32.dll")] internal static extern uint SendInput(uint count, Input[] inputs, int size);
        internal const uint VirtualMouseTag = 0xB11B111;
        internal static void VirtualLeft(uint flags) { SendInput(1, new Input[] { new Input { mouse = new MouseInput { flags = flags, extra = (UIntPtr)VirtualMouseTag } } }, 40); }
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] internal static extern IntPtr GetMenu(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out UIntPtr result);

        internal static bool TrySend(IntPtr hwnd, uint message, int command, long value, out long result)
        {
            UIntPtr reply = UIntPtr.Zero;
            bool ok = IsWindow(hwnd) && SendMessageTimeout(hwnd, message, (IntPtr)command, (IntPtr)value, 2, 80, out reply) != IntPtr.Zero;
            // Definite assignment also when IsWindow returned false.
            if (!ok) { result = 0; return false; }
            result = unchecked((long)reply.ToUInt64()); return true;
        }
        internal static bool Get(IntPtr hwnd, int command, out long value) { return TrySend(hwnd, WM_USER, command, 0, out value); }
        internal static void Set(IntPtr hwnd, int command, long value) { long unused; TrySend(hwnd, WM_USER, command, value, out unused); }
        internal static void SetCommand(IntPtr hwnd, int command) { long unused; TrySend(hwnd, WM_COMMAND, command, 0, out unused); }
        internal static void Fullscreen(IntPtr hwnd) { long unused; TrySend(hwnd, WM_COMMAND, 10013, 0, out unused); }
        internal static bool IsPlayer(IntPtr hwnd)
        {
            StringBuilder name = new StringBuilder(128); GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() != "PotPlayer64" && name.ToString() != "PotPlayer") return false;
            uint pid; GetWindowThreadProcessId(hwnd, out pid);
            if (hwnd == verifiedWindow && pid == verifiedPid) return true;
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                {
                    bool valid = p.ProcessName == "PotPlayerMini64" || p.ProcessName == "PotPlayer64" ||
                                 p.ProcessName == "PotPlayerMini" || p.ProcessName == "PotPlayer";
                    if (valid) { verifiedWindow = hwnd; verifiedPid = pid; }
                    return valid;
                }
            }
            catch { return false; }
        }
        internal static IntPtr FocusedPlayer()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (!IsWindowEnabled(hwnd) || !IsPlayer(hwnd)) return IntPtr.Zero;
            uint pid; uint thread = GetWindowThreadProcessId(hwnd, out pid);
            GuiInfo info = new GuiInfo(); info.size = Marshal.SizeOf(info);
            // Let native menus and settings/edit controls keep their own keyboard input.
            if (GetGUIThreadInfo(thread, ref info))
            {
                if ((info.flags & 0x1C) != 0) return IntPtr.Zero;
                StringBuilder name = new StringBuilder(128); GetClassName(info.focus, name, name.Capacity);
                string control = name.ToString();
                if (control.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0 || control == "ComboBox") return IntPtr.Zero;
            }
            return hwnd;
        }
        internal static bool IsFullscreen(IntPtr hwnd)
        {
            Rect r; if (IsIconic(hwnd) || !GetWindowRect(hwnd, out r)) return false;
            Rectangle screen = Screen.FromHandle(hwnd).Bounds;
            return Math.Abs(r.left - screen.Left) <= 1 && Math.Abs(r.top - screen.Top) <= 1 &&
                   Math.Abs(r.right - screen.Right) <= 1 && Math.Abs(r.bottom - screen.Bottom) <= 1;
        }
        internal static void Seek(IntPtr hwnd, int seconds)
        {
            long position, duration;
            if (!Get(hwnd, GetPosition, out position) || !Get(hwnd, GetDuration, out duration) || duration <= 0) return;
            Set(hwnd, SetPosition, Math.Max(0, Math.Min(duration - 1, position + seconds * 1000L)));
        }
        internal static void Volume(IntPtr hwnd, int step)
        {
            long value; if (Get(hwnd, GetVolume, out value)) SetVolumePercent(hwnd, (int)value + step);
        }
        static readonly Dictionary<IntPtr, int> lastVolume = new Dictionary<IntPtr, int>();
        internal static void SetVolumePercent(IntPtr hwnd, int value)
        {
            value = Math.Max(0, Math.Min(100, value));
            long previous;
            if (Get(hwnd, GetVolume, out previous) && previous > 0) lastVolume[hwnd] = (int)previous;
            if (value > 0) lastVolume[hwnd] = value;
            Set(hwnd, SetVolume, value); Set(hwnd, SetMute, value == 0 ? 1 : 0);
        }
        internal static void ToggleMute(IntPtr hwnd)
        {
            long muted, value;
            if (!Get(hwnd, GetMute, out muted) || !Get(hwnd, GetVolume, out value)) return;
            if (muted != 0 || value == 0)
            {
                int remembered;
                if (value == 0) Set(hwnd, SetVolume, lastVolume.TryGetValue(hwnd, out remembered) ? remembered : 50);
                Set(hwnd, SetMute, 0);
            }
            else { lastVolume[hwnd] = (int)value; Set(hwnd, SetMute, 1); }
        }
        internal static Rectangle VolumeButtonBounds(IntPtr hwnd)
        {
            Rectangle row = ControlRowBounds(hwnd);
            if (row.IsEmpty) return Rectangle.Empty;
            double scale = Math.Max(96, GetDpiForWindow(hwnd)) / 96.0;
            return ControlLayout.Volume(row, scale);
        }
        internal static Rectangle ClientBounds(IntPtr hwnd)
        {
            Rect client; Point origin = new Point();
            if (!GetClientRect(hwnd, out client) || !ClientToScreen(hwnd, ref origin)) return Rectangle.Empty;
            return new Rectangle(origin.x, origin.y, client.right, client.bottom);
        }
        internal static List<IntPtr> PlayerWindows()
        {
            List<IntPtr> players = new List<IntPtr>();
            EnumWindows(delegate(IntPtr window, IntPtr data) { if (IsPlayer(window)) players.Add(window); return true; }, IntPtr.Zero);
            return players;
        }
        internal static bool ControlBarVisible(IntPtr player)
        { return !ControlRowBounds(player).IsEmpty; }
        internal static Rectangle ControlRowBounds(IntPtr player)
        {
            if (!IsWindowVisible(player) || IsIconic(player)) return Rectangle.Empty;
            IntPtr row = ControlRowWindow(player);
            Rect bounds;
            if (row != IntPtr.Zero && GetWindowRect(row, out bounds)) return Rectangle.FromLTRB(bounds.left, bounds.top, bounds.right, bounds.bottom);
            return IsFullscreen(player) ? Rectangle.Empty : ControlLayout.Row(ClientBounds(player), Math.Max(96, GetDpiForWindow(player)) / 96.0, false);
        }
        internal static IntPtr ControlRowWindow(IntPtr player)
        {
            if (!IsWindowVisible(player) || IsIconic(player)) return IntPtr.Zero;
            Rectangle client = ClientBounds(player); IntPtr found = IntPtr.Zero; int foundWidth = 0;
            double scale = Math.Max(96, GetDpiForWindow(player)) / 96.0;
            int rowHeight = (int)Math.Round(40 * scale);
            // PotPlayer draws the controls in a native child window, including its fullscreen OSC.
            // Read its actual rectangle rather than infer visibility from foreground focus or occlusion.
            EnumChildWindows(player, delegate(IntPtr window, IntPtr data)
            {
                if (!IsWindowVisible(window)) return true;
                Rect bounds;
                if (!GetWindowRect(window, out bounds) || Math.Abs(bounds.bottom - bounds.top - rowHeight) > 2 ||
                    bounds.right - bounds.left < Math.Min(client.Width / 2, 160 * scale) || Math.Abs(client.Bottom - bounds.bottom) > 24 * scale) return true;
                StringBuilder name = new StringBuilder(64); GetClassName(window, name, name.Capacity);
                if (name.ToString().StartsWith("AfxWnd", StringComparison.Ordinal) && bounds.right - bounds.left > foundWidth)
                { found = window; foundWidth = bounds.right - bounds.left; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
        internal static bool IsPlayerSurface(IntPtr player, Point point)
        {
            IntPtr surface = WindowFromPoint(point);
            if (GetAncestor(surface, 2) == player) return true;
            // The fullscreen control bar is a separate window owned by the player process.
            uint playerPid, surfacePid;
            GetWindowThreadProcessId(player, out playerPid);
            GetWindowThreadProcessId(surface, out surfacePid);
            return playerPid != 0 && surfacePid == playerPid;
        }
        internal static Rectangle SpeedButtonBounds(IntPtr hwnd)
        {
            Rectangle volume = VolumeButtonBounds(hwnd);
            return ControlLayout.Speed(volume, Math.Max(96, GetDpiForWindow(hwnd)) / 96.0);
        }
        internal static Rectangle SubtitleButtonBounds(IntPtr hwnd)
        { return ControlLayout.Subtitle(VolumeButtonBounds(hwnd), Math.Max(96, GetDpiForWindow(hwnd)) / 96.0); }
        internal static bool Pressed(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
        internal static bool IsVideoSurface(IntPtr player, Point point)
        {
            IntPtr surface = WindowFromPoint(point), video = VideoSurface(player);
            if (video == IntPtr.Zero || !ClientBounds(video).Contains(point.x, point.y)) return false;
            return surface == video || IsChild(video, surface) ||
                (GetWindowLong(surface, -20) & 0x80020) == 0x80020 && GetAncestor(surface, 2) == player;
        }
        internal static IntPtr VideoSurface(IntPtr player)
        {
            IntPtr found = IntPtr.Zero; Rectangle client = ClientBounds(player); long largest = 0;
            double scale = Math.Max(96, GetDpiForWindow(player)) / 96.0;
            EnumChildWindows(player, delegate(IntPtr child, IntPtr data) {
                if (!IsWindowVisible(child)) return true;
                StringBuilder name = new StringBuilder(128); GetClassName(child, name, name.Capacity); Rect bounds;
                if (name.ToString().StartsWith("Afx:", StringComparison.Ordinal) && GetWindowRect(child, out bounds) &&
                    bounds.right - bounds.left > 96 * scale && bounds.bottom - bounds.top > client.Height / 2 && Math.Abs(bounds.left - client.Left) < 24 * scale)
                { long area = (long)(bounds.right - bounds.left) * (bounds.bottom - bounds.top); if (area > largest) { found = child; largest = area; } }
                return true;
            }, IntPtr.Zero); return found;
        }
        internal static bool AnyPlayerRunning()
        {
            int session;
            using (Process current = Process.GetCurrentProcess()) session = current.SessionId;
            foreach (string name in new string[] { "PotPlayerMini64", "PotPlayer64" })
            {
                Process[] players = Process.GetProcessesByName(name);
                try
                {
                    foreach (Process player in players)
                        try { if (!player.HasExited && player.SessionId == session) return true; }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                }
                finally { foreach (Process player in players) player.Dispose(); }
            }
            return false;
        }
    }
}
