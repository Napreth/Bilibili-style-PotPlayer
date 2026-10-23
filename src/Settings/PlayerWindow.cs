using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal class PlayerWindow : Form
    {
        sealed class OwnerHandle : IWin32Window { readonly IntPtr value; internal OwnerHandle(IntPtr value) { this.value = value; } public IntPtr Handle { get { return value; } } }
        internal static readonly Color Background = Color.FromArgb(28, 29, 33), Surface = Color.FromArgb(38, 39, 44), Muted = Color.FromArgb(158, 160, 170), Pink = Color.FromArgb(251, 114, 153);
        internal readonly IntPtr Player;
        internal readonly Panel Body = new Panel { Dock = DockStyle.Fill };
        bool topmost, minimized, restoring;
        protected override bool ShowWithoutActivation { get { return restoring; } }
        internal PlayerWindow(IntPtr player, string title, Size size)
        {
            Player = player; Text = title; BackColor = Background; ForeColor = Color.FromArgb(232, 232, 236);
            Font = new Font("Microsoft YaHei UI", 10, FontStyle.Regular); AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            Size = size; MinimumSize = new Size(Math.Min(660, size.Width), Math.Min(450, size.Height));
            Panel header = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Surface };
            Label caption = new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(22, 0, 0, 0), Font = new Font(Font.FontFamily, 12, FontStyle.Bold) };
            Button close = Button("×"); close.Dock = DockStyle.Right; close.Width = 54; close.Font = new Font(Font.FontFamily, 19); close.AccessibleName = "关闭";
            close.Click += delegate { Close(); };
            MouseEventHandler move = delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } };
            caption.MouseDown += move; header.MouseDown += move;
            header.Controls.Add(caption); header.Controls.Add(close); Controls.Add(Body); Controls.Add(header);
            DoubleBuffered = true; KeyPreview = true;
        }
        internal static Button Button(string text)
        {
            Button button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Color.FromArgb(232, 232, 236), Height = 34, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = Color.FromArgb(65, 66, 74); button.FlatAppearance.MouseOverBackColor = Color.FromArgb(65, 48, 58); return button;
        }
        internal static Label Label(string text, int height)
        { return new Label { Text = text, Height = height, Dock = DockStyle.Top, ForeColor = Muted, Padding = new Padding(22, 12, 22, 0) }; }
        internal void Present()
        {
            if (!Visible)
            {
                Rectangle area = Screen.FromHandle(Player).WorkingArea; Native.Rect owner; Native.GetWindowRect(Player, out owner);
                Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
                Location = new Point(Math.Max(area.Left + 12, Math.Min(area.Right - Width - 12, (owner.left + owner.right - Width) / 2)), Math.Max(area.Top + 12, Math.Min(area.Bottom - Height - 12, (owner.top + owner.bottom - Height) / 2)));
                Show(new OwnerHandle(Player));
            }
            SyncOwner(); Activate();
        }
        internal bool Foreground { get { return IsHandleCreated && Native.GetForegroundWindow() == Handle; } }
        internal void SyncOwner()
        {
            if (!Native.IsWindow(Player)) { Close(); return; }
            bool iconic = Native.IsIconic(Player);
            if (iconic && !minimized) { minimized = true; Hide(); }
            else if (!iconic && minimized) { minimized = false; restoring = true; try { Show(); } finally { restoring = false; } }
            bool value = (Native.GetWindowLong(Player, -20) & 8) != 0;
            if (value != topmost) { topmost = value; Native.SetWindowPos(Handle, value ? (IntPtr)(-1) : (IntPtr)(-2), 0, 0, 0, 0, 0x13); }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        { if (keyData == Keys.Escape) { Close(); return true; } return base.ProcessCmdKey(ref msg, keyData); }
        protected override void WndProc(ref Message msg)
        {
            base.WndProc(ref msg);
            if (msg.Msg == 0x84 && msg.Result == (IntPtr)1) { Point p = PointToClient(new Point(unchecked((short)msg.LParam.ToInt64()), unchecked((short)(msg.LParam.ToInt64() >> 16)))); if (p.X >= Width - 12 && p.Y >= Height - 12) msg.Result = (IntPtr)17; }
        }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (Pen pen = new Pen(Color.FromArgb(71, 72, 80))) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); }
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
    }
}
