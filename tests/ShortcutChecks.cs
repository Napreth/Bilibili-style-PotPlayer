using System;
using System.IO;
using BilibiliPotPlayer;

internal static class ShortcutChecks
{
    internal static void Run(Action<bool, bool, string> equal)
    {
        ShortcutOptions options = ShortcutOptions.Defaults();
        equal(true, options.Validate() == null, "default shortcuts are valid");
        options.Bindings.RemoveAll(b => b.Action == PlayerAction.PlayPause);
        options.Bindings.Add(new ShortcutBinding { Action = PlayerAction.PlayPause, Key = 0x50, Modifiers = 2 });
        equal(true, InputPolicy.Route(0x50, false, true, false, false, options) == KeyRoute.Mapped, "custom Ctrl P routes to player");
        equal(true, options.Find(0x50, 2) == PlayerAction.PlayPause, "custom chord resolves playback action");
        equal(true, InputPolicy.Route(0x20, false, false, false, false, options) == KeyRoute.Blocked, "removed default no longer triggers native action");
        equal(true, InputPolicy.Route(0x74, false, false, false, false, options) == KeyRoute.Mapped, "F5 always opens settings");
        options.Bindings.Add(new ShortcutBinding { Action = PlayerAction.Mute, Key = 0x50, Modifiers = 2 });
        equal(true, options.Validate() != null, "duplicate chords rejected"); options.Bindings.RemoveAt(options.Bindings.Count - 1);
        foreach (int key in new int[] { 0x09, 0x73, 0x1B, 0x20 }) equal(true, ShortcutOptions.Reserved(key, 4), "Alt system shortcut reserved " + key);
        equal(true, ShortcutOptions.Reserved(0x1B, 3), "task manager chord reserved");
        equal(true, ShortcutOptions.Reserved(0x2E, 6), "secure attention chord reserved");
        ShortcutOptions broken = options.Copy(); broken.Bindings.Add(new ShortcutBinding { Action = PlayerAction.Mute, Key = 0x74 });
        equal(true, broken.Validate() != null, "fixed settings entry cannot be overwritten");
        string root = Path.Combine(Path.GetTempPath(), "BilibiliShortcuts-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "options.json"); ShortcutStore store = new ShortcutStore(path); store.Apply(options);
            equal(true, store.Revision == 1 && File.Exists(path), "applying persists and increments revision");
            ShortcutStore restarted = new ShortcutStore(path);
            equal(true, restarted.Current.Find(0x50, 2) == PlayerAction.PlayPause, "custom shortcut survives a new process store");
            equal(true, restarted.Current.Find(0x20, 0) == PlayerAction.None, "unassigned key stays unassigned after reload");
            options.Bindings.RemoveAll(b => b.Action == PlayerAction.PlayPause); store.Apply(options);
            equal(true, new ShortcutStore(path).Current.Find(0x50, 2) == PlayerAction.None, "atomic replacement persists removal");
            File.WriteAllText(path, "invalid json"); equal(true, ShortcutOptions.Load(path).Find(0x20, 0) == PlayerAction.PlayPause, "corrupt file falls back safely");
            File.WriteAllText(path, "{\"Version\":99,\"Bindings\":[]}"); equal(true, ShortcutOptions.Load(path).Bindings.Count > 0, "unknown schema falls back safely");
            string blocked = Path.Combine(root, "directory.json"); Directory.CreateDirectory(blocked); ShortcutStore failed = new ShortcutStore(blocked);
            bool rejected = false; try { failed.Apply(options); } catch (IOException) { rejected = true; } catch (UnauthorizedAccessException) { rejected = true; }
            equal(true, rejected && failed.Revision == 0 && failed.Current.Find(0x20, 0) == PlayerAction.PlayPause, "failed save does not change active bindings");
        }
        finally { Directory.Delete(root, true); }
    }
}
