using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace BilibiliPotPlayer
{
    // Capture a native menu only in response to user interaction, then paint our own menu.
    internal sealed class NativeMenuSnapshot : IDisposable
    {
        internal sealed class Item { internal int Command; internal bool Checked, Enabled; internal string Text; }
        internal readonly List<Item> Items = new List<Item>();
        internal bool Loading { get; private set; }
        internal bool Ready { get { return !Loading && now >= settled; } }
        internal IntPtr Player { get; private set; }
        readonly EventProc callback;
        IntPtr hook;
        long now, deadline, settled;
        internal NativeMenuSnapshot()
        { callback = OnMenu; hook = SetWinEventHook(6, 6, IntPtr.Zero, callback, 0, 0, 2); }
        internal bool Request(IntPtr player, int command, long time)
        {
            Tick(time);
            if (!Ready || hook == IntPtr.Zero || Native.FocusedPlayer() != player) return false;
            Player = player; Items.Clear(); soundSubtitles = null; Loading = true; deadline = time + 700;
            if (command == 21021) PostMessage(player, Native.WM_COMMAND, (IntPtr)command, IntPtr.Zero);
            else ThreadPool.QueueUserWorkItem(delegate { long unused; Native.TrySend(player, Native.WM_COMMAND, command, 0, out unused); });
            return true;
        }
        internal void Tick(long time)
        {
            now = time;
            if (Loading)
            {
                uint pid; uint thread = Native.GetWindowThreadProcessId(Player, out pid);
                EnumThreadWindows(thread, delegate(IntPtr window, IntPtr data)
                {
                    StringBuilder name = new StringBuilder(64); Native.GetClassName(window, name, name.Capacity);
                    if (name.ToString() == "#32768" && Native.IsWindowVisible(window))
                    { OnMenu(hook, 6, window, 0, 0, thread, 0); return false; }
                    return true;
                }, IntPtr.Zero);
            }
            if (Loading && now >= deadline) Finish();
        }
        void Finish()
        {
            Loading = false; settled = now + 100;
            PostMessage(Player, 0x1F, IntPtr.Zero, IntPtr.Zero);
        }
        void OnMenu(IntPtr eventHook, uint eventId, IntPtr window, int objectId, int child, uint thread, uint timestamp)
        {
            if (!Loading || !Native.IsWindow(Player)) return;
            uint playerPid, menuPid;
            uint playerThread = Native.GetWindowThreadProcessId(Player, out playerPid);
            Native.GetWindowThreadProcessId(window, out menuPid);
            StringBuilder name = new StringBuilder(64); Native.GetClassName(window, name, name.Capacity);
            if (menuPid != playerPid || thread != playerThread || name.ToString() != "#32768") return;
            ShowWindow(window, 0);
            long menu = Native.GetMenu(window).ToInt64();
            if (menu == 0) Native.TrySend(window, 0x1E1, 0, 0, out menu);
            if (menu != 0) Read((IntPtr)menu, 0);
            Finish();
        }
        string languageFolder, languageFile, soundSubtitles;
        internal string LocalizedString(int id)
        {
            if (id == 4779 && soundSubtitles != null) return soundSubtitles;
            uint pid; Native.GetWindowThreadProcessId(Player, out pid);
            try
            {
                using (Process process = Process.GetProcessById((int)pid))
                    languageFolder = Path.Combine(Path.GetDirectoryName(process.MainModule.FileName), "Language");
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\DAUM\\PotPlayerMini64\\Settings"))
                    languageFile = key == null ? "English.ini" : Path.GetFileName(Convert.ToString(key.GetValue("LanguageIni", "English.ini")));
                foreach (string file in new string[] { languageFile, "English.ini" })
                {
                    string path = Path.Combine(languageFolder, file);
                    if (!File.Exists(path)) continue;
                    string prefix = id + "=";
                    foreach (string line in File.ReadLines(path))
                        if (line.StartsWith(prefix, StringComparison.Ordinal))
                        { string value = line.Substring(prefix.Length).Trim(); if (id == 4779) soundSubtitles = value; return value; }
                }
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            catch (IOException) { }
            return "Real-time sound subtitles";
        }
        void Read(IntPtr menu, int depth)
        {
            if (depth > 5) return;
            int count = Math.Min(512, GetMenuItemCount(menu));
            for (int i = 0; i < count && Items.Count < 2048; i++)
            {
                uint state = GetMenuState(menu, (uint)i, 0x400), command = GetMenuItemID(menu, i);
                StringBuilder text = new StringBuilder(1024); GetMenuString(menu, (uint)i, text, text.Capacity, 0x400);
                if (command != UInt32.MaxValue && command != 0)
                    Items.Add(new Item { Command = (int)command, Checked = (state & 8) != 0, Enabled = (state & 3) == 0, Text = text.ToString().Split('\t')[0].Replace("&", "").Trim() });
                IntPtr sub = GetSubMenu(menu, i); if (sub != IntPtr.Zero) Read(sub, depth + 1);
            }
        }
        public void Dispose()
        { if (Loading) Finish(); if (hook != IntPtr.Zero) UnhookWinEvent(hook); hook = IntPtr.Zero; }
        delegate void EventProc(IntPtr hook, uint eventId, IntPtr window, int objectId, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr dll, EventProc callback, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, Native.EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int show);
        [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr menu);
        [DllImport("user32.dll")] static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
        [DllImport("user32.dll")] static extern uint GetMenuItemID(IntPtr menu, int position);
        [DllImport("user32.dll")] static extern IntPtr GetSubMenu(IntPtr menu, int position);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int count, uint flags);
    }
}
