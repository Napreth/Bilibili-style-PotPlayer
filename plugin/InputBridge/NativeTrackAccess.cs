using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace BilibiliPotPlayer
{
    // PotPlayer exposes its stream selection through native menus rather than WM_USER getters.
    // Capture that menu only in response to hovering our selector, then dismiss it and draw ours.
    internal sealed class NativeTrackAccess : IDisposable
    {
        internal sealed class Entry { internal int Command; internal bool Selected; internal string Text; }
        internal sealed class Selection
        {
            internal List<Entry> Entries = new List<Entry>();
            internal int Selected = -1;
            internal bool Loading;
        }
        internal readonly Selection Audio = new Selection(), Video = new Selection();
        internal IntPtr Player { get; private set; }
        internal IntPtr PendingPlayer { get { return pending || Now < settlingUntil ? Player : IntPtr.Zero; } }
        internal long Now { get; private set; }
        readonly EventProc callback;
        IntPtr hook;
        bool pending, videoRequest;
        long deadline, settlingUntil;
        long refreshAt;
        bool refreshVideo;
        List<MediaTrack> refreshTracks;
        internal bool Ready { get { return !pending && Now >= settlingUntil; } }
        List<MediaTrack> requested;
        int audioCommand, videoCommand;
        internal NativeTrackAccess()
        {
            callback = OnEvent;
            hook = SetWinEventHook(6, 6, IntPtr.Zero, callback, 0, 0, 2);
        }
        internal void Reset(IntPtr player)
        {
            Player = player; pending = false; settlingUntil = 0; refreshTracks = null;
            Audio.Entries.Clear(); Video.Entries.Clear(); Audio.Selected = Video.Selected = -1;
            Audio.Loading = Video.Loading = false;
            if (player != IntPtr.Zero && (audioCommand == 0 || videoCommand == 0)) ReadCommands(player);
        }
        void ReadCommands(IntPtr player)
        {
            uint pid; Native.GetWindowThreadProcessId(player, out pid);
            try
            {
                string library;
                using (Process process = Process.GetProcessById((int)pid)) library = Path.Combine(Path.GetDirectoryName(process.MainModule.FileName), "PotPlayer64.dll");
                IntPtr module = LoadLibraryEx(library, IntPtr.Zero, 0x22); // Data/resources only; never execute or inject it.
                if (module == IntPtr.Zero) return;
                try
                {
                    IntPtr resource = FindResource(module, (IntPtr)157, (IntPtr)9);
                    if (resource == IntPtr.Zero) return;
                    int size = (int)SizeofResource(module, resource);
                    if (size <= 0 || size > 16384 || size % 8 != 0) return;
                    IntPtr bytes = LockResource(LoadResource(module, resource));
                    if (bytes == IntPtr.Zero) return;
                    byte[] entries = new byte[size]; Marshal.Copy(bytes, entries, 0, size);
                    for (int offset = 0; offset < size; offset += 8)
                    {
                        int flags = BitConverter.ToUInt16(entries, offset), key = BitConverter.ToUInt16(entries, offset + 2), command = BitConverter.ToUInt16(entries, offset + 4);
                        if ((flags & 0x1D) != 1) continue; // Virtual key, no Shift/Ctrl/Alt.
                        if (key == 0x41) audioCommand = command;
                        if (key == 0x56) videoCommand = command;
                    }
                }
                finally { FreeLibrary(module); }
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
        }
        internal bool Request(bool video, List<MediaTrack> tracks, long now)
        {
            Selection selection = video ? Video : Audio;
            // Only an explicit hover may inspect the native menu. Never interrupt loading,
            // an existing menu/dialog, or another foreground application to obtain it.
            if (tracks.Count <= 1 || !Ready || hook == IntPtr.Zero || !Native.IsPlayer(Player) ||
                Native.FocusedPlayer() != Player || (video ? videoCommand : audioCommand) == 0) return false;
            pending = true; videoRequest = video; requested = tracks; deadline = now + 600;
            selection.Loading = true;
            // Dispatch the native A / V menu command without selecting any menu item.
            IntPtr target = Player; int command = video ? videoCommand : audioCommand;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                long unused; Native.TrySend(target, Native.WM_COMMAND, command | 0x10000, 0, out unused);
            });
            return true;
        }
        internal void Tick(long now)
        {
            Now = now;
            if (refreshTracks != null && Ready && now >= refreshAt && Native.FocusedPlayer() == Player)
            { List<MediaTrack> tracks = refreshTracks; refreshTracks = null; Request(refreshVideo, tracks, now); }
            if (pending)
            {
                uint pid; uint thread = Native.GetWindowThreadProcessId(Player, out pid);
                EnumThreadWindows(thread, delegate(IntPtr window, IntPtr data)
                {
                    StringBuilder name = new StringBuilder(64); Native.GetClassName(window, name, name.Capacity);
                    if (name.ToString() == "#32768" && Native.IsWindowVisible(window))
                    { OnEvent(hook, 6, window, 0, 0, thread, 0); return false; }
                    return true;
                }, IntPtr.Zero);
            }
            if (!pending || now < deadline) return;
            pending = false; settlingUntil = Now + 120; (videoRequest ? Video : Audio).Loading = false;
            PostMessage(Player, 0x1F, IntPtr.Zero, IntPtr.Zero); // Cancel only our requested menu.
        }
        void OnEvent(IntPtr eventHook, uint eventId, IntPtr window, int objectId, int child, uint thread, uint time)
        {
            if (!pending || !Native.IsWindow(Player)) return;
            uint playerPid, windowPid;
            uint playerThread = Native.GetWindowThreadProcessId(Player, out playerPid);
            Native.GetWindowThreadProcessId(window, out windowPid);
            if (windowPid != playerPid || thread != playerThread) return;
            StringBuilder name = new StringBuilder(64); Native.GetClassName(window, name, name.Capacity);
            if (name.ToString() != "#32768") return;
            long menu;
            // The popup is immediately hidden; its checked state and command IDs remain readable.
            ShowWindow(window, 0);
            menu = Native.GetMenu(window).ToInt64();
            if (menu == 0) Native.TrySend(window, 0x1E1, 0, 0, out menu);
            if (menu != 0)
            {
                List<Entry> items = ReadMenu((IntPtr)menu);
                List<Entry> matched = Match(items, requested);
                Selection selection = videoRequest ? Video : Audio;
                if (matched != null)
                {
                    selection.Entries = matched;
                    selection.Selected = matched.FindIndex(delegate(Entry item) { return item.Selected; });
                }
            }
            pending = false; settlingUntil = Now + 120; (videoRequest ? Video : Audio).Loading = false;
            PostMessage(Player, 0x1F, IntPtr.Zero, IntPtr.Zero);
        }
        static List<Entry> ReadMenu(IntPtr menu)
        {
            List<Entry> items = new List<Entry>();
            int count = Math.Min(GetMenuItemCount(menu), 512);
            for (int i = 0; i < count; i++)
            {
                uint state = GetMenuState(menu, (uint)i, 0x400);
                uint command = GetMenuItemID(menu, i);
                StringBuilder text = new StringBuilder(1024); GetMenuString(menu, (uint)i, text, text.Capacity, 0x400);
                // Keep separators so independent groups cannot accidentally be joined.
                items.Add(new Entry { Command = command == UInt32.MaxValue || (state & 0x810) != 0 ? -1 : unchecked((int)command),
                    Selected = (state & 8) != 0, Text = text.ToString() });
            }
            return items;
        }
        internal static List<Entry> Match(List<Entry> items, List<MediaTrack> tracks)
        {
            // Reject incomplete menus, ambiguous matches, or multiple checked items.
            List<Entry> result = null; int best = 0;
            for (int start = 0; start + tracks.Count <= items.Count; start++)
            {
                if (start > 0 && items[start - 1].Command + 1 == items[start].Command) continue;
                int end = start + tracks.Count;
                if (end < items.Count && items[end - 1].Command + 1 == items[end].Command) continue;
                int score = 0, selected = 0; bool valid = true;
                for (int i = 0; i < tracks.Count; i++)
                {
                    Entry item = items[start + i];
                    if (item.Command < 1 || i > 0 && item.Command != items[start].Command + i) { valid = false; break; }
                    if (item.Selected) selected++;
                    string text = Normalize(item.Text), codec = Normalize(tracks[i].Codec);
                    if (codec.Length > 1 && text.Contains(codec)) score += 2;
                    else if (codec == "H264" && text.Contains("AVC") || codec == "H265" && text.Contains("HEVC")) score += 2;
                    string label = Normalize(tracks[i].Label);
                    if (label.Length > 1 && text.Contains(label)) score += 3;
                }
                if (!valid || selected != 1 || score == 0) continue;
                if (score == best) result = null;
                else if (score > best) { best = score; result = items.GetRange(start, tracks.Count); }
            }
            return result;
        }
        static string Normalize(string text)
        {
            StringBuilder result = new StringBuilder();
            foreach (char c in text.ToUpperInvariant()) if (Char.IsLetterOrDigit(c)) result.Append(c);
            return result.ToString();
        }
        internal bool Select(bool video, int index)
        {
            Selection selection = video ? Video : Audio;
            if (!Native.IsPlayer(Player) || index < 0 || index >= selection.Entries.Count) return false;
            if (!PostMessage(Player, Native.WM_COMMAND, (IntPtr)selection.Entries[index].Command, IntPtr.Zero)) return false;
            // Refresh from the checked native item on the next hover; do not claim a failed switch.
            return true;
        }
        internal void RefreshAfterSwitch(bool video, List<MediaTrack> tracks)
        { refreshVideo = video; refreshTracks = tracks; refreshAt = Now + 200; }
        public void Dispose() { if (hook != IntPtr.Zero) UnhookWinEvent(hook); }
        delegate void EventProc(IntPtr hook, uint eventId, IntPtr window, int objectId, int child, uint thread, uint time);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll")] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
        [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
        [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr dll, EventProc callback, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int show);
        [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, Native.EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr menu);
        [DllImport("user32.dll")] static extern uint GetMenuItemID(IntPtr menu, int position);
        [DllImport("user32.dll")] static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int count, uint flags);
    }
}
