using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal sealed class PlayerMenus : IDisposable
    {
        sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return PlayerWindow.Background; } }
            public override Color ImageMarginGradientBegin { get { return PlayerWindow.Background; } }
            public override Color ImageMarginGradientMiddle { get { return PlayerWindow.Background; } }
            public override Color ImageMarginGradientEnd { get { return PlayerWindow.Background; } }
            public override Color MenuItemSelected { get { return Color.FromArgb(65, 45, 56); } }
            public override Color MenuItemBorder { get { return PlayerWindow.Pink; } }
            public override Color MenuBorder { get { return Color.FromArgb(65, 66, 74); } }
            public override Color SeparatorDark { get { return Color.FromArgb(57, 58, 65); } }
        }
        readonly IntPtr player;
        readonly CaptionCatalog captions;
        readonly StreamControls streams;
        readonly ShortcutStore shortcuts;
        readonly Profile profile;
        readonly ContextMenuStrip menu;
        readonly List<PlayerWindow> windows = new List<PlayerWindow>();
        SettingsWindow settings;
        EffectsWindow color, audio;
        DetailsWindow details;
        long nextDetails;
        internal PlayerMenus(IntPtr player, CaptionCatalog captions, StreamControls streams, ShortcutStore shortcuts, Profile profile)
        {
            this.player = player; this.captions = captions; this.streams = streams; this.shortcuts = shortcuts; this.profile = profile;
            menu = new ContextMenuStrip { ShowImageMargin = false, BackColor = PlayerWindow.Background, ForeColor = Color.FromArgb(232, 232, 236), Font = new Font("Microsoft YaHei UI", 10), Renderer = new ToolStripProfessionalRenderer(new MenuColors()), Padding = new Padding(6), MinimumSize = new Size(252, 0) };
            Add("视频色彩调整", delegate { OpenEffects(false); }); Add("视频音效调节", delegate { OpenEffects(true); }); menu.Items.Add(new ToolStripSeparator());
            Add("视频详情", OpenDetails); menu.Items.Add(new ToolStripSeparator()); ToolStripMenuItem item = Add("设置", delegate { OpenSettings(0); }); item.ShortcutKeyDisplayString = "F5";
        }
        ToolStripMenuItem Add(string title, Action action)
        { ToolStripMenuItem item = new ToolStripMenuItem(title) { AutoSize = false, Size = new Size(244, 42) }; item.Click += delegate { action(); }; menu.Items.Add(item); return item; }
        internal bool MenuVisible { get { return menu.Visible; } }
        internal bool DialogForeground { get { return windows.Exists(w => !w.IsDisposed && w.Foreground); } }
        internal void Show(int x, int y)
        { if (!Native.IsPlayer(player)) return; Native.SetForegroundWindow(player); Rectangle area = Screen.FromPoint(new Point(x, y)).WorkingArea; Size size = menu.GetPreferredSize(Size.Empty); menu.Show(new Point(Math.Max(area.Left, Math.Min(x, area.Right - size.Width)), Math.Max(area.Top, Math.Min(y, area.Bottom - size.Height)))); }
        internal void OpenSettings(int page)
        {
            if (settings == null || settings.IsDisposed) { settings = new SettingsWindow(player, shortcuts, captions, profile); Track(settings); }
            settings.SelectPage(page); settings.Present();
        }
        void OpenEffects(bool sound)
        {
            EffectsWindow window = sound ? audio : color;
            if (window == null || window.IsDisposed) { window = new EffectsWindow(player, sound); Track(window); if (sound) audio = window; else color = window; }
            window.Present();
        }
        void OpenDetails() { if (details == null || details.IsDisposed) { details = new DetailsWindow(player, streams, captions); Track(details); } details.RefreshDetails(); details.Present(); }
        void Track(PlayerWindow window) { windows.RemoveAll(w => w.IsDisposed); windows.Add(window); }
        internal void Tick(long now)
        {
            foreach (PlayerWindow window in windows.ToArray()) if (!window.IsDisposed) window.SyncOwner();
            if (now >= nextDetails) { nextDetails = now + 700; if (details != null && !details.IsDisposed && details.Visible) details.RefreshDetails(); if (audio != null && !audio.IsDisposed && audio.Visible) audio.RefreshEffects(); if (color != null && !color.IsDisposed && color.Visible) color.RefreshEffects(); }
            if (menu.Visible && Native.GetForegroundWindow() != player && Native.GetForegroundWindow() != menu.Handle) menu.Close();
        }
        public void Dispose() { menu.Dispose(); foreach (PlayerWindow window in windows) window.Dispose(); windows.Clear(); }
    }
}
