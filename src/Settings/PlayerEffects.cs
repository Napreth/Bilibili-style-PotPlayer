using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BilibiliPotPlayer
{
    // Resolve commands from the installed player's resources, rather than assuming version-specific IDs.
    // Loading as data cannot execute player code in the behavior host.
    internal sealed class PlayerEffects : IDisposable
    {
        internal sealed class Command { internal string Text; internal int Id; internal IntPtr Menu; }
        readonly IntPtr player;
        IntPtr module, menu;
        readonly Dictionary<string, string> labels = new Dictionary<string, string>();
        readonly Dictionary<int, int> accelerators = new Dictionary<int, int>();
        internal PlayerEffects(IntPtr player)
        {
            this.player = player;
            try
            {
                uint pid; Native.GetWindowThreadProcessId(player, out pid); string root;
                using (Process process = Process.GetProcessById((int)pid)) root = Path.GetDirectoryName(process.MainModule.FileName);
                string language = Path.Combine(root, "Language", "Chinese(Simplified).ini");
                if (File.Exists(language)) foreach (string line in File.ReadAllLines(language, Encoding.UTF8))
                { int at = line.IndexOf('='); if (at > 0 && line.StartsWith("101_", StringComparison.Ordinal)) labels[line.Substring(0, at)] = line.Substring(at + 1).Split('\t')[0]; }
                module = LoadLibraryEx(Path.Combine(root, "PotPlayer64.dll"), IntPtr.Zero, 0x22);
                if (module != IntPtr.Zero) { menu = LoadMenu(module, (IntPtr)101); ReadAccelerators(); }
                Refresh();
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        }
        IntPtr Submenu(params int[] path)
        { IntPtr current = menu; foreach (int index in path) { if (current == IntPtr.Zero) break; current = GetSubMenu(current, index); } return current; }
        void ReadAccelerators()
        {
            IntPtr resource = FindResource(module, (IntPtr)157, (IntPtr)9); if (resource == IntPtr.Zero) return;
            int size = (int)SizeofResource(module, resource); if (size <= 0 || size > 65536 || size % 8 != 0) return;
            IntPtr data = LockResource(LoadResource(module, resource)); if (data == IntPtr.Zero) return;
            byte[] bytes = new byte[size]; Marshal.Copy(data, bytes, 0, size);
            for (int i = 0; i < size; i += 8) accelerators[BitConverter.ToUInt16(bytes, i + 4)] = BitConverter.ToUInt16(bytes, i + 2) | (BitConverter.ToUInt16(bytes, i) & 0x1D) << 8;
        }
        bool Expected(uint id, int[] path)
        {
            int chord;
            if (id == 0 || id > UInt16.MaxValue || !accelerators.TryGetValue((int)id, out chord)) return false;
            if (path.Length == 3 && path[0] == 0 && path[1] == 11 && path[2] >= 16 && path[2] <= 22)
                return chord == (new int[] { 0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55 }[path[2] - 16] | 1 << 8);
            if (path.Length == 3 && path[0] == 0 && path[1] == 12 && path[2] == 14) return chord == (0x45 | 5 << 8);
            if (path.Length == 4 && path[0] == 0 && path[1] == 12 && path[2] == 18 && path[3] >= 0 && path[3] <= 6)
                return chord == (new int[] { 0x4E, 0x43, 0x44, 0x52, 0x56, 0x41, 0x54 }[path[3]] | 5 << 8);
            return false;
        }
        internal Command At(string fallback, params int[] path)
        {
            if (path.Length == 0) return new Command { Text = fallback };
            int[] parent = new int[path.Length - 1]; Array.Copy(path, parent, parent.Length);
            IntPtr group = Submenu(parent); uint id = group == IntPtr.Zero ? UInt32.MaxValue : GetMenuItemID(group, path[path.Length - 1]);
            return new Command { Text = fallback, Id = Expected(id, path) ? (int)id : 0, Menu = group };
        }
        internal void Refresh()
        {
            if (!Native.IsPlayer(player)) return;
            foreach (IntPtr group in new IntPtr[] { Submenu(0, 11), Submenu(0, 12), Submenu(0, 12, 15), Submenu(0, 12, 18) })
            { long unused; if (group != IntPtr.Zero) Native.TrySend(player, 0x117, unchecked((int)group.ToInt64()), 0, out unused); }
        }
        internal bool Checked(Command command) { return command.Menu != IntPtr.Zero && (GetMenuState(command.Menu, (uint)command.Id, 0) & 8) != 0; }
        internal bool Enabled(Command command) { uint state = command.Menu == IntPtr.Zero ? UInt32.MaxValue : GetMenuState(command.Menu, (uint)command.Id, 0); return command.Id != 0 && state != UInt32.MaxValue && (state & 3) == 0; }
        internal List<Command> Presets()
        {
            List<Command> result = new List<Command>(); IntPtr presets = Submenu(0, 12, 15); if (presets == IntPtr.Zero || At("", 0, 12, 14).Id == 0) return result;
            int count = Math.Min(64, GetMenuItemCount(presets));
            for (int i = 0; i < count; i++)
            {
                uint id = GetMenuItemID(presets, i); if (id == 0 || id > UInt16.MaxValue) continue;
                StringBuilder text = new StringBuilder(256); GetMenuString(presets, (uint)i, text, text.Capacity, 0x400);
                string localized; if (!labels.TryGetValue("101_0_12_15_" + i, out localized)) localized = text.ToString().Split('\t')[0];
                if (localized.Length > 0) result.Add(new Command { Text = localized.Replace("&", ""), Id = (int)id, Menu = presets });
            }
            return result;
        }
        internal bool Run(Command command)
        { return command != null && command.Id > 0 && Native.IsPlayer(player) && PostMessage(player, Native.WM_COMMAND, (IntPtr)command.Id, IntPtr.Zero); }
        public void Dispose() { if (menu != IntPtr.Zero) DestroyMenu(menu); if (module != IntPtr.Zero) FreeLibrary(module); menu = module = IntPtr.Zero; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll")] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
        [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
        [DllImport("user32.dll", EntryPoint = "LoadMenuW")] static extern IntPtr LoadMenu(IntPtr module, IntPtr name);
        [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] static extern IntPtr GetSubMenu(IntPtr menu, int index);
        [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr menu);
        [DllImport("user32.dll")] static extern uint GetMenuItemID(IntPtr menu, int index);
        [DllImport("user32.dll")] static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int count, uint flags);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    }
}
