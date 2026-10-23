using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal sealed class SettingsWindow : PlayerWindow
    {
        readonly ShortcutStore shortcuts;
        readonly CaptionCatalog captions;
        readonly Profile profile;
        readonly Panel content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        readonly List<Button> navigation = new List<Button>();
        ShortcutOptions draft;
        int editRevision, page = -1, captureSlot;
        PlayerAction captureAction;
        Label status;
        readonly Dictionary<Button, ShortcutBinding> keyButtons = new Dictionary<Button, ShortcutBinding>();
        internal SettingsWindow(IntPtr player, ShortcutStore shortcuts, CaptionCatalog captions, Profile profile) : base(player, "设置", new Size(920, 800))
        {
            this.shortcuts = shortcuts; this.captions = captions; this.profile = profile;
            Panel nav = new Panel { Dock = DockStyle.Left, Width = 164, BackColor = Surface, Padding = new Padding(12, 20, 12, 12) };
            string[] titles = { "通用", "快捷键", "字幕", "弹幕", "软件更新" };
            for (int i = titles.Length - 1; i >= 0; i--)
            {
                int index = i; Button button = Button(titles[i]); button.Dock = DockStyle.Top; button.Height = 52; button.FlatAppearance.BorderSize = 0; button.TextAlign = ContentAlignment.MiddleLeft; button.Padding = new Padding(16, 0, 0, 0);
                button.Click += delegate { SelectPage(index); }; nav.Controls.Add(button); navigation.Insert(0, button);
            }
            Body.Controls.Add(content); Body.Controls.Add(nav); SelectPage(0);
        }
        internal void SelectPage(int index)
        {
            if (page == index) return;
            CancelCapture(); page = index;
            foreach (Control control in new List<Control>(ControlsOf(content))) control.Dispose(); content.Controls.Clear(); content.AutoScrollPosition = Point.Empty;
            for (int i = 0; i < navigation.Count; i++) { navigation[i].ForeColor = i == index ? Pink : ForeColor; navigation[i].BackColor = i == index ? Color.FromArgb(61, 43, 54) : Surface; }
            if (index == 0) General(); else if (index == 1) Shortcuts(); else if (index == 2 || index == 3) CaptionPage(index == 3); else Updates();
        }
        static IEnumerable<Control> ControlsOf(Control parent) { foreach (Control c in parent.Controls) yield return c; }
        FlowLayoutPanel Page(string title, string detail)
        {
            FlowLayoutPanel flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24, 18, 24, 24) };
            flow.Controls.Add(new Label { Text = title, Size = new Size(620, 38), Font = new Font(Font.FontFamily, 15, FontStyle.Bold), ForeColor = ForeColor });
            flow.Controls.Add(new Label { Text = detail, Size = new Size(620, 64), ForeColor = Muted }); content.Controls.Add(flow); return flow;
        }
        void TextRow(FlowLayoutPanel flow, string title, string value)
        { flow.Controls.Add(new Label { Text = title + "\r\n" + value, Size = new Size(620, 70), ForeColor = ForeColor, Padding = new Padding(0, 8, 0, 0) }); }
        void General()
        {
            FlowLayoutPanel flow = Page("通用", "设置绑定当前播放器窗口。可以继续操作播放器；播放器关闭时，设置窗口也会关闭。");
            TextRow(flow, "播放行为", "快进 / 后退 " + profile.SeekSeconds + " 秒；长按 " + profile.HoldMilliseconds + " 毫秒触发手势；音量步进 " + profile.VolumeStepPercent + "%");
            TextRow(flow, "窗口", "F5 打开设置 · Esc 关闭设置\r\n支持全屏、窗口及小窗模式。");
            TextRow(flow, "配置保存", "快捷键点击“保存快捷键”后生效；字幕和弹幕设置自动保存。");
            TextRow(flow, "Bilibili-style-PotPlayer", "pre-alpha  " + typeof(SettingsWindow).Assembly.GetName().Version);
        }
        static readonly string[] ActionNames = { "", "后退 / 长按持续后退", "快进 / 长按临时倍速", "音量增加", "音量减少", "播放 / 暂停", "上一项", "下一项", "切换全屏", "退出全屏", "静音", "显示 / 隐藏侧栏", "显示 / 隐藏字幕", "切换小窗", "显示 / 隐藏普通弹幕", "正常速度", "两倍速度", "打开设置" };
        internal static string Chord(ShortcutBinding b)
        {
            if (b == null) return "未设置";
            string name;
            switch (b.Key) { case 0xBC: name = ","; break; case 0xBE: name = "."; break; case 0xDB: name = "["; break; case 0xDD: name = "]"; break; case 0x20: name = "Space"; break; case 0x1B: name = "Esc"; break; default: name = ((Keys)b.Key).ToString(); break; }
            return ((b.Modifiers & 2) != 0 ? "Ctrl + " : "") + ((b.Modifiers & 4) != 0 ? "Alt + " : "") + ((b.Modifiers & 1) != 0 ? "Shift + " : "") + name;
        }
        void Shortcuts()
        {
            if (draft == null) { draft = shortcuts.Current.Copy(); editRevision = shortcuts.Revision; } keyButtons.Clear();
            FlowLayoutPanel flow = Page("快捷键", "点击按键框后按下新组合键。Backspace / Delete 清除，Esc 取消录入。\r\nF5 和 Esc 保留为固定入口；鼠标点击、滚轮和中键手势保持不变。");
            status = new Label { Text = "每个操作最多可设置两个快捷键。", ForeColor = Muted, Size = new Size(620, 48) }; flow.Controls.Add(status);
            for (int i = 1; i <= (int)PlayerAction.Settings; i++)
            {
                PlayerAction action = (PlayerAction)i;
                Panel row = new Panel { Size = new Size(620, 42), Margin = new Padding(0) };
                row.Controls.Add(new Label { Text = ActionNames[i], Location = new Point(0, 6), Size = new Size(274, 30), TextAlign = ContentAlignment.MiddleLeft });
                for (int slot = 0; slot < 2; slot++)
                {
                    int currentSlot = slot; Button key = Button(""); key.Bounds = new Rectangle(280 + slot * 166, 2, 158, 34);
                    key.AccessibleName = ActionNames[i] + (slot == 0 ? " 主快捷键" : " 备用快捷键");
                    bool locked = slot == 0 && (action == PlayerAction.Settings || action == PlayerAction.ExitFullscreen);
                    key.TabStop = !locked;
                    key.Click += delegate { if (locked) { status.Text = "此入口固定保留，可以在右侧添加备用快捷键。"; return; } CancelCapture(); captureAction = action; captureSlot = currentSlot; key.Text = "请按组合键…"; key.ForeColor = Pink; status.Text = "正在录入：" + ActionNames[(int)action]; };
                    row.Controls.Add(key); keyButtons.Add(key, new ShortcutBinding { Action = action, Key = slot });
                }
                flow.Controls.Add(row);
            }
            Panel footer = new Panel { Size = new Size(620, 58), Margin = new Padding(0, 16, 0, 0) };
            Button reset = Button("恢复默认"); reset.SetBounds(0, 4, 120, 36); reset.Click += delegate { CancelCapture(); draft = ShortcutOptions.Defaults(); RefreshKeys(); status.Text = "已恢复默认，点击保存后生效。"; };
            Button save = Button("保存快捷键"); save.BackColor = Color.FromArgb(125, 52, 79); save.SetBounds(444, 4, 170, 36);
            save.Click += delegate
            {
                CancelCapture();
                if (editRevision != shortcuts.Revision) { draft = shortcuts.Current.Copy(); editRevision = shortcuts.Revision; RefreshKeys(); status.Text = "其他窗口已修改快捷键，已载入最新配置，请重新修改。"; return; }
                try { shortcuts.Apply(draft); editRevision = shortcuts.Revision; status.ForeColor = Pink; status.Text = "已保存，所有播放器实例即时生效。"; save.Text = "已保存快捷键"; }
                catch (Exception error) { if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is ArgumentException)) throw; status.Text = "保存失败：" + error.Message; }
            };
            footer.Controls.Add(reset); footer.Controls.Add(save); flow.Controls.Add(footer); RefreshKeys();
        }
        void RefreshKeys()
        {
            foreach (KeyValuePair<Button, ShortcutBinding> item in keyButtons)
            {
                List<ShortcutBinding> values = draft.Bindings.FindAll(b => b.Action == item.Value.Action);
                bool fixedEntry = item.Value.Key == 0 && (item.Value.Action == PlayerAction.Settings || item.Value.Action == PlayerAction.ExitFullscreen);
                item.Key.Text = Chord(item.Value.Key < values.Count ? values[item.Value.Key] : null) + (fixedEntry ? "（固定）" : ""); item.Key.ForeColor = fixedEntry ? Muted : ForeColor;
            }
        }
        void CancelCapture() { captureAction = PlayerAction.None; if (draft != null) RefreshKeys(); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (captureAction != PlayerAction.None)
            {
                int key = (int)(keyData & Keys.KeyCode), mods = ((keyData & Keys.Shift) != 0 ? 1 : 0) | ((keyData & Keys.Control) != 0 ? 2 : 0) | ((keyData & Keys.Alt) != 0 ? 4 : 0);
                if (Native.Pressed(0x5B) || Native.Pressed(0x5C)) { status.Text = "Windows 组合键由系统保留。"; return true; }
                if (key == 0x1B && mods == 0) { CancelCapture(); status.Text = "已取消录入。"; return true; }
                if (key == 0x10 || key == 0x11 || key == 0x12 || key >= 0xA0 && key <= 0xA5) return true;
                ShortcutOptions value = draft.Copy(); List<ShortcutBinding> same = value.Bindings.FindAll(b => b.Action == captureAction);
                if (captureSlot < same.Count) value.Bindings.Remove(same[captureSlot]);
                if (!((key == 8 || key == 0x2E) && mods == 0))
                {
                    ShortcutBinding binding = new ShortcutBinding { Action = captureAction, Key = key, Modifiers = mods };
                    int position = captureSlot == 0 ? value.Bindings.FindIndex(b => b.Action == captureAction) : -1;
                    if (position >= 0) value.Bindings.Insert(position, binding); else value.Bindings.Add(binding);
                }
                string error = value.Validate();
                if (error != null) { status.Text = error; return true; }
                draft = value; CancelCapture(); status.Text = "已修改，点击保存快捷键后生效。"; return true;
            }
            if (keyData == Keys.F5) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }
        void CaptionPage(bool danmaku)
        {
            Panel host = new Panel { Dock = DockStyle.Top, Height = 846 };
            CaptionOptions o = captions.Options;
            FlowLayoutPanel common = new FlowLayoutPanel { Dock = DockStyle.Top, Height = danmaku ? 190 : 62, Padding = new Padding(24, 16, 16, 8), WrapContents = true };
            Action changed = delegate { o.Save(); captions.Revision++; };
            Action<string, bool, Action<bool>> toggle = delegate(string title, bool initial, Action<bool> apply)
            {
                CheckBox box = new CheckBox { Text = title, Checked = initial, AutoSize = true, Margin = new Padding(0, 0, 22, 14), ForeColor = ForeColor };
                box.CheckedChanged += delegate { apply(box.Checked); changed(); }; common.Controls.Add(box);
            };
            toggle(danmaku ? "显示普通弹幕" : "显示字幕", danmaku ? o.DanmakuEnabled : o.SubtitlesEnabled, v => { if (danmaku) o.DanmakuEnabled = v; else o.SubtitlesEnabled = v; });
            if (danmaku)
            {
                toggle("滚动弹幕", o.Scrolling, v => o.Scrolling = v); toggle("固定弹幕", o.Fixed, v => o.Fixed = v); toggle("彩色弹幕", o.Colored, v => o.Colored = v);
                toggle("高级 / ASS 特效", o.AssEffectsEnabled, v => o.AssEffectsEnabled = v); toggle("避让字幕", o.AvoidSubtitles, v => o.AvoidSubtitles = v); toggle("随画面缩放", o.ScaleWithScreen, v => o.ScaleWithScreen = v);
                common.SetFlowBreak(common.Controls[common.Controls.Count - 1], true);
                AddChoice(common, "区域", new string[] { "10%", "25%", "50%", "75%", "100%" }, new int[] { 10, 25, 50, 75, 100 }, o.Region, v => { o.Region = v; changed(); });
                AddChoice(common, "速度", new string[] { "40%", "70%", "100%", "130%", "160%" }, DanmakuSettings.Speeds, o.Speed, v => { o.Speed = v; changed(); });
                common.SetFlowBreak(common.Controls[common.Controls.Count - 1], true);
                common.Controls.Add(new Label { Text = "不透明度", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
                TrackBar opacity = new TrackBar { Minimum = 10, Maximum = 100, Value = o.Opacity, TickStyle = TickStyle.None, Width = 280, Height = 34, SmallChange = 5, LargeChange = 10 };
                Label percent = new Label { Text = o.Opacity + "%", Width = 60, Padding = new Padding(0, 6, 0, 0) }; opacity.ValueChanged += delegate { o.Opacity = opacity.Value; percent.Text = o.Opacity + "%"; changed(); }; common.Controls.Add(opacity); common.Controls.Add(percent);
            }
            Panel editorHost = new Panel { Dock = DockStyle.Top, Height = 656 };
            SubtitlePopup editor = new SubtitlePopup(null, captions); editor.AttachEditor(editorHost, Player, danmaku);
            editor.EditorReset = delegate { BeginInvoke((Action)delegate { int current = page; page = -1; SelectPage(current); }); };
            editor.SizeChanged += delegate { editorHost.Height = editor.Height; host.Height = common.Height + editor.Height; }; editorHost.Height = editor.Height; host.Height = common.Height + editor.Height;
            host.Controls.Add(editorHost); host.Controls.Add(common); content.Controls.Add(host);
        }
        static void AddChoice(FlowLayoutPanel panel, string text, string[] labels, int[] values, int current, Action<int> apply)
        {
            panel.Controls.Add(new Label { Text = text, AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            ComboBox combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105, BackColor = Surface, ForeColor = Color.White };
            combo.Items.AddRange(labels); combo.SelectedIndex = Math.Max(0, Array.IndexOf(values, current)); combo.SelectedIndexChanged += delegate { apply(values[combo.SelectedIndex]); }; panel.Controls.Add(combo);
        }
        void Updates()
        {
            FlowLayoutPanel flow = Page("软件更新", "更新功能将在后续版本接入。本页目前展示版本及计划中的选项。");
            TextRow(flow, "当前版本", "Bilibili-style-PotPlayer  " + typeof(SettingsWindow).Assembly.GetName().Version + "  (pre-alpha)");
            TextRow(flow, "更新状态", "尚未接入在线检查、下载与自动安装。");
            flow.Controls.Add(new CheckBox { Text = "同时管理 PotPlayer 更新（后续版本，可选）", AutoSize = true, Enabled = false, ForeColor = Muted });
        }
    }
}
