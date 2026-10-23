using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal sealed class EffectsWindow : PlayerWindow
    {
        readonly PlayerEffects effects;
        readonly Label feedback;
        readonly Dictionary<Button, PlayerEffects.Command> commands = new Dictionary<Button, PlayerEffects.Command>();
        readonly bool sound;
        internal EffectsWindow(IntPtr player, bool audio) : base(player, audio ? "视频音效调节" : "视频色彩调整", new Size(660, audio ? 650 : 450))
        {
            effects = new PlayerEffects(player); sound = audio;
            FlowLayoutPanel flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24, 18, 24, 18) };
            flow.Controls.Add(new Label { Text = audio ? "使用 PotPlayer 内置音效。点击按钮切换对应效果。" : "调整当前视频的图像属性。每次变化 1%，当前值显示在播放器 OSD 中。", Size = new Size(590, 56), ForeColor = Muted });
            feedback = new Label { Text = "", Size = new Size(590, 48), ForeColor = Pink };
            if (!audio)
            {
                string[] names = { "亮度", "对比度", "饱和度" };
                for (int i = 0; i < names.Length; i++)
                {
                    Panel row = new Panel { Size = new Size(590, 58) };
                    row.Controls.Add(new Label { Text = names[i], Location = new Point(0, 10), Size = new Size(150, 34) });
                    Add(row, "− 1%", effects.At(names[i] + " −1%", 0, 11, 17 + i * 2), new Rectangle(180, 4, 160, 38));
                    Add(row, "+ 1%", effects.At(names[i] + " +1%", 0, 11, 18 + i * 2), new Rectangle(360, 4, 160, 38)); flow.Controls.Add(row);
                }
                Button reset = Make("重置图像属性", effects.At("重置图像属性", 0, 11, 16)); reset.Width = 180; flow.Controls.Add(reset);
            }
            else
            {
                FlowLayoutPanel presets = new FlowLayoutPanel { Size = new Size(590, 130), AutoSize = true, WrapContents = true };
                foreach (PlayerEffects.Command command in effects.Presets()) { Button b = Make(command.Text, command); b.Width = 176; b.Height = 36; b.Margin = new Padding(0, 0, 14, 10); presets.Controls.Add(b); }
                if (presets.Controls.Count > 0) flow.Controls.Add(presets);
                FlowLayoutPanel processing = new FlowLayoutPanel { Size = new Size(590, 170), AutoSize = true, WrapContents = true };
                string[] names = { "规格化", "晶化", "降噪", "混响", "语音消除", "语音增强", "左右声道互换" };
                for (int i = 0; i < names.Length; i++) { PlayerEffects.Command command = effects.At(names[i], 0, 12, 18, i); Button b = Make(names[i], command); b.Size = new Size(176, 36); b.Margin = new Padding(0, 0, 14, 10); processing.Controls.Add(b); }
                PlayerEffects.Command eqCommand = effects.At("均衡器", 0, 12, 14); Button eq = Make("均衡器", eqCommand); eq.Size = new Size(176, 36); processing.Controls.Add(eq); flow.Controls.Add(processing);
                flow.Controls.Add(new Label { Text = "效果是否可用取决于声音处理滤镜；音频直通时部分效果不生效。\r\n高亮表示播放器当前启用的预设或效果。", Size = new Size(590, 70), ForeColor = Muted });
            }
            flow.Controls.Add(feedback); Body.Controls.Add(flow); RefreshEffects();
        }
        void Add(Control parent, string text, PlayerEffects.Command command, Rectangle bounds)
        { Button b = Make(text, command); b.Bounds = bounds; parent.Controls.Add(b); }
        Button Make(string title, PlayerEffects.Command command)
        {
            Button b = Button(title); b.Enabled = effects.Enabled(command); commands.Add(b, command);
            b.Click += delegate { feedback.Text = effects.Run(command) ? "已发送：" + command.Text : "当前播放器版本不支持此操作。"; }; return b;
        }
        internal void RefreshEffects()
        {
            effects.Refresh();
            foreach (KeyValuePair<Button, PlayerEffects.Command> item in commands)
            { item.Key.Enabled = effects.Enabled(item.Value); item.Key.BackColor = sound && effects.Checked(item.Value) ? Color.FromArgb(125, 52, 79) : Surface; }
        }
        protected override void Dispose(bool disposing) { if (disposing) effects.Dispose(); base.Dispose(disposing); }
    }
}
