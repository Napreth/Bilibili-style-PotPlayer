using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BilibiliPotPlayer
{
    internal enum PlayerAction { None, SeekBack, SeekForward, VolumeUp, VolumeDown, PlayPause, Previous, Next, Fullscreen, ExitFullscreen, Mute, Sidebar, Subtitles, SmallWindow, Danmaku, NormalSpeed, DoubleSpeed, Settings }
    internal sealed class ShortcutBinding
    {
        public PlayerAction Action;
        public int Key, Modifiers; // Shift=1, Control=2, Alt=4. Win combinations belong to Windows.
        internal ShortcutBinding Copy() { return new ShortcutBinding { Action = Action, Key = Key, Modifiers = Modifiers }; }
    }
    internal sealed class ShortcutOptions
    {
        public int Version = 1;
        public List<ShortcutBinding> Bindings = new List<ShortcutBinding>();
        internal static string SettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BilibiliPotPlayer", "shortcut-options.json"); } }
        internal static ShortcutOptions Defaults()
        {
            ShortcutOptions value = new ShortcutOptions();
            PlayerAction[] actions = { PlayerAction.SeekBack, PlayerAction.SeekForward, PlayerAction.VolumeUp, PlayerAction.VolumeDown, PlayerAction.PlayPause, PlayerAction.Previous, PlayerAction.Previous, PlayerAction.Next, PlayerAction.Next, PlayerAction.Fullscreen, PlayerAction.ExitFullscreen, PlayerAction.Mute, PlayerAction.Sidebar, PlayerAction.Subtitles, PlayerAction.SmallWindow, PlayerAction.Danmaku, PlayerAction.NormalSpeed, PlayerAction.DoubleSpeed, PlayerAction.Settings };
            int[] keys = { 0x25, 0x27, 0x26, 0x28, 0x20, 0xBC, 0xDB, 0xBE, 0xDD, 0x46, 0x1B, 0x4D, 0x58, 0x43, 0x53, 0x44, 0x31, 0x32, 0x74 };
            for (int i = 0; i < keys.Length; i++) value.Bindings.Add(new ShortcutBinding { Action = actions[i], Key = keys[i], Modifiers = i == 16 || i == 17 ? 1 : 0 });
            return value;
        }
        internal PlayerAction Find(int key, int modifiers)
        {
            if (key == 0x74 && modifiers == 0) return PlayerAction.Settings;
            if (key == 0x1B && modifiers == 0) return PlayerAction.ExitFullscreen;
            foreach (ShortcutBinding binding in Bindings) if (binding.Key == key && binding.Modifiers == modifiers) return binding.Action;
            return PlayerAction.None;
        }
        internal static bool Reserved(int key, int modifiers)
        {
            return key < 8 || key > 254 || key == 0x10 || key == 0x11 || key == 0x12 || key >= 0xA0 && key <= 0xA5 || key == 0x5B || key == 0x5C ||
                (modifiers & ~7) != 0 || (modifiers & 4) != 0 && (key == 9 || key == 0x73 || key == 0x1B || key == 0x20) ||
                (modifiers & 2) != 0 && key == 0x1B || (modifiers & 6) == 6 && key == 0x2E;
        }
        internal string Validate()
        {
            if (Version != 1 || Bindings == null || Bindings.Count > 64) return "快捷键配置格式无效。";
            HashSet<int> keys = new HashSet<int>();
            foreach (ShortcutBinding b in Bindings)
            {
                if (b == null || b.Action <= PlayerAction.None || b.Action > PlayerAction.Settings || Reserved(b.Key, b.Modifiers)) return "此按键组合由系统保留，不能用作播放器快捷键。";
                if (b.Key == 0x74 && b.Modifiers == 0 && b.Action != PlayerAction.Settings || b.Key == 0x1B && b.Modifiers == 0 && b.Action != PlayerAction.ExitFullscreen) return "F5 和 Esc 是固定入口，请使用其他按键。";
                if (!keys.Add(b.Key | b.Modifiers << 8)) return "此快捷键已被其他操作使用，请先清除原来的绑定。";
            }
            return null;
        }
        internal ShortcutOptions Copy()
        { ShortcutOptions value = new ShortcutOptions(); foreach (ShortcutBinding b in Bindings) value.Bindings.Add(b.Copy()); return value; }
        internal static ShortcutOptions Load(string path)
        {
            try
            {
                if (File.Exists(path)) { ShortcutOptions value = new JavaScriptSerializer().Deserialize<ShortcutOptions>(File.ReadAllText(path)); if (value != null && value.Validate() == null) return value; }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { } catch (InvalidOperationException) { }
            return Defaults();
        }
        internal void Save(string path)
        {
            string error = Validate(); if (error != null) throw new ArgumentException(error);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    internal sealed class ShortcutStore
    {
        internal ShortcutOptions Current { get; private set; }
        internal int Revision { get; private set; }
        readonly string path;
        internal ShortcutStore() : this(ShortcutOptions.SettingsPath) { }
        internal ShortcutStore(string path) { this.path = path; Current = ShortcutOptions.Load(path); }
        internal void Apply(ShortcutOptions value) { value.Save(path); Current = value.Copy(); Revision++; }
    }
}
