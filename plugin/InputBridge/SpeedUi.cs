using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal abstract class SpeedSurface : Form
    {
        internal IntPtr Player { get; set; }
        IntPtr attachedOwner;
        readonly bool embedded;
        IntPtr embeddedParent;
        bool virtualVisible;
        Rectangle virtualBounds;
        IntPtr virtualSurface;
        internal new bool Visible { get { return virtualVisible || base.Visible; } }
        internal void SetAnchor(IntPtr player, Rectangle bounds, IntPtr surface, bool visible)
        { if (base.Visible) Hide(); Player = player; virtualBounds = bounds; virtualSurface = surface; virtualVisible = visible && !bounds.IsEmpty; }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams; p.ExStyle = (p.ExStyle | 0x08000000) & ~0x00040080;
                if (embedded)
                {
                    p.Style = (p.Style & ~unchecked((int)0x80000000)) | 0x44000000;
                    p.ExStyle &= ~8; p.Parent = embeddedParent;
                }
                return p;
            }
        }
        protected SpeedSurface(string title, bool embedded = false)
        {
            this.embedded = embedded;
            if (embedded) TopLevel = false;
            Text = title; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = true; TopMost = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; DoubleBuffered = true; Cursor = Cursors.Hand;
            BackColor = Color.FromArgb(28, 29, 32);
            SetStyle(ControlStyles.ResizeRedraw, true);
        }
        protected override void CreateHandle()
        {
            // Match the native parent's window DPI context at creation; do not reparent windows
            // with incompatible awareness modes or change either process's DPI policy.
            IntPtr previous = IntPtr.Zero;
            if (embedded && Native.IsWindow(embeddedParent))
                previous = Native.SetThreadDpiAwarenessContext(Native.GetWindowDpiAwarenessContext(embeddedParent));
            try { base.CreateHandle(); }
            finally { if (previous != IntPtr.Zero) Native.SetThreadDpiAwarenessContext(previous); }
        }
        internal Rectangle ScreenBounds
        {
            get
            {
                if (virtualVisible) return virtualBounds;
                Native.Rect rect;
                return IsHandleCreated && Native.GetWindowRect(Handle, out rect) ? Rectangle.FromLTRB(rect.left, rect.top, rect.right, rect.bottom) : Rectangle.Empty;
            }
        }
        protected bool PlaceInControlBar(IntPtr player, Rectangle screenPosition)
        {
            IntPtr parent = Native.ControlRowWindow(player);
            return PlaceInParent(player, parent, screenPosition);
        }
        protected bool PlaceInPlayer(IntPtr player, Rectangle screenPosition)
        { return PlaceInParent(player, player, screenPosition); }
        bool PlaceInParent(IntPtr player, IntPtr parent, Rectangle screenPosition)
        {
            Player = player;
            if (parent == IntPtr.Zero) { Dismiss(); return false; }
            if (embeddedParent != parent)
            {
                Dismiss();
                if (IsHandleCreated) DestroyHandle();
                if (embeddedParent != IntPtr.Zero) ChildSurfaceHost.Release(embeddedParent);
                embeddedParent = parent; ChildSurfaceHost.Acquire(parent);
            }
            Rectangle position = ControlLayout.RelativeTo(screenPosition, Native.ClientBounds(parent));
            if (Bounds != position) Bounds = position;
            return true;
        }
        internal bool IsOver(Native.Point point)
        {
            if (!Visible || !ScreenBounds.Contains(point.x, point.y)) return false;
            IntPtr target = Native.WindowFromPoint(point);
            if (virtualVisible) return target == virtualSurface;
            return target == Handle || Native.IsChild(Handle, target);
        }
        internal IntPtr FocusedPlayer()
        {
            return Visible && IsHandleCreated && Native.GetForegroundWindow() == Handle && Native.IsPlayer(Player)
                ? Player : IntPtr.Zero;
        }
        internal void Dismiss()
        {
            virtualVisible = false;
            if (!Visible) return;
            bool focused = Native.GetForegroundWindow() == Handle;
            Hide();
            if (focused && Native.IsWindow(Player)) Native.SetForegroundWindow(Player);
        }
        protected void Reveal()
        {
            if (embedded)
            {
                if (!Native.IsWindow(embeddedParent)) { Dismiss(); return; }
                if (!Visible) Show();
                Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0013);
                return;
            }
            if (attachedOwner != Player && Native.IsWindow(Player))
            {
                Native.SetWindowLongPtr(Handle, -8, Player);
                attachedOwner = Player;
            }
            if (!Visible) Show();
            // A player can become topmost on entering fullscreen or through its own pin option.
            // Attaching an already-topmost owner does not reliably copy that style to a new form.
            bool ownerTopmost = (Native.GetWindowLong(Player, -20) & 8) != 0;
            if (ownerTopmost != ((Native.GetWindowLong(Handle, -20) & 8) != 0))
                Native.SetWindowPos(Handle, ownerTopmost ? (IntPtr)(-1) : (IntPtr)(-2), 0, 0, 0, 0, 0x0213);
            // ShowWithoutActivation can still raise a new window. Put a background surface
            // immediately above its owner, below any applications already above that player.
            IntPtr after = IntPtr.Zero;
            if (Native.FocusedPlayer() != Player && FocusedPlayer() != Player)
            {
                after = Native.GetWindow(Player, 3); // GW_HWNDPREV: window immediately above the player.
                if (after == Handle) after = Native.GetWindow(Handle, 3);
            }
            Native.SetWindowPos(Handle, after, 0, 0, 0, 0, 0x0213);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && embeddedParent != IntPtr.Zero)
            { ChildSurfaceHost.Release(embeddedParent); embeddedParent = IntPtr.Zero; }
            base.Dispose(disposing);
        }
        internal static string RateText(long speed)
        { return (speed / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "×"; }
    }

    internal sealed class SpeedButton : SpeedSurface
    {
        long speed = 1000, lastRead;
        internal SpeedButton() : base("倍速按钮", true) { BackColor = Color.FromArgb(30, 30, 30); }
        internal void UpdatePlayer(IntPtr player, long now)
        {
            if (player == IntPtr.Zero || !Native.ControlBarVisible(player)) { Dismiss(); return; }
            Rectangle position = Native.SpeedButtonBounds(player);
            if (!PlaceInControlBar(player, position)) return;
            if (now - lastRead >= 80 || !Visible)
            {
                long value;
                if (Native.Get(player, Native.GetSpeed, out value) && value != speed) { speed = value; Invalidate(); }
                lastRead = now;
            }
            Reveal();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Font font = new Font("Microsoft YaHei UI", 12 * Width / 58f, FontStyle.Regular, GraphicsUnit.Pixel))
                TextRenderer.DrawText(e.Graphics, speed == 1000 ? "倍速" : RateText(speed), font, ClientRectangle,
                    Color.FromArgb(218, 218, 218), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class SpeedPopup : SpeedSurface
    {
        // PotPlayer's native rate interface supports 0.2–12×; omit the requested unsupported 0.1×.
        internal static readonly int[] Rates = { 10000, 5000, 3000, 2000, 1000, 500, 250 };
        readonly SpeedButton button;
        readonly Action<IntPtr, int> selectRate;
        long speed = 1000, lastRead, lastInside, hoverSince;
        IntPtr hoverPlayer;
        int hovered = -1, pressedRow = -1;
        const int RowHeight = 30, Inset = 6, MenuWidth = 100;
        internal SpeedPopup(SpeedButton button, Action<IntPtr, int> selectRate) : base("倍速菜单")
        { this.button = button; this.selectRate = selectRate; }
        internal bool IsHoverTarget(IntPtr player, Native.Point point)
        {
            return player != IntPtr.Zero && (Player == player && IsOver(point) ||
                button.Player == player && button.IsOver(point));
        }
        internal void UpdateHover(IntPtr player, long now)
        {
            Native.Point pointer;
            if (player == IntPtr.Zero || !button.Visible || !Native.GetCursorPos(out pointer)) { Dismiss(); hoverPlayer = IntPtr.Zero; return; }
            Rectangle anchor = button.ScreenBounds;
            bool overButton = anchor.Contains(pointer.x, pointer.y) &&
                button.Player == player && button.IsOver(pointer);
            bool overPanel = Player == player && IsOver(pointer);
            if (overButton)
            {
                if (hoverPlayer != player) { hoverPlayer = player; hoverSince = now; }
                lastInside = now;
            }
            else hoverPlayer = IntPtr.Zero;
            if (Visible && Player != player) Dismiss();
            if (overPanel) lastInside = now;
            if (!Visible && (!overButton || now - hoverSince < 120)) return;
            if (Visible && !overButton && !overPanel && now - lastInside >= 250) { Dismiss(); return; }
            float scale = Math.Max(96, Native.GetDpiForWindow(player)) / 96f;
            Size size = new Size((int)Math.Round(MenuWidth * scale), (int)Math.Round((Rates.Length * RowHeight + 2 * Inset) * scale));
            Rectangle screen = Screen.FromHandle(player).Bounds;
            int left = anchor.Left + (anchor.Width - size.Width) / 2;
            Rectangle position = new Rectangle(Math.Max(screen.Left, Math.Min(screen.Right - size.Width, left)),
                Math.Max(screen.Top, anchor.Top - size.Height), size.Width, size.Height);
            if (Bounds != position) Bounds = position;
            Player = player;
            if (now - lastRead >= 80 || !Visible)
            {
                long value;
                if (Native.Get(player, Native.GetSpeed, out value) && value != speed) { speed = value; Invalidate(); }
                lastRead = now;
            }
            Reveal();
        }
        int RowAt(int y)
        {
            float scale = Width / (float)MenuWidth;
            int row = (int)Math.Floor((y / scale - Inset) / RowHeight);
            return row >= 0 && row < Rates.Length ? row : -1;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        { base.OnMouseMove(e); int row = RowAt(e.Y); if (row != hovered) { hovered = row; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e)
        { base.OnMouseLeave(e); hovered = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) pressedRow = RowAt(e.Y); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int row = RowAt(e.Y);
            if (row >= 0 && row == pressedRow) { selectRate(Player, Rates[row]); Dismiss(); }
            pressedRow = -1;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float scale = Width / (float)MenuWidth;
            using (Font font = new Font("Segoe UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font selectedFont = new Font("Segoe UI", 13 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Pen border = new Pen(Color.FromArgb(58, 59, 64), Math.Max(1, scale)))
            {
                for (int i = 0; i < Rates.Length; i++)
                {
                    Rectangle row = new Rectangle(1, (int)Math.Round((Inset + i * RowHeight) * scale), Width - 2, (int)Math.Round(RowHeight * scale));
                    if (i == hovered) using (Brush fill = new SolidBrush(Color.FromArgb(48, 50, 55))) e.Graphics.FillRectangle(fill, row);
                    Color color = speed == Rates[i] ? Color.FromArgb(0, 174, 236) : Color.White;
                    TextRenderer.DrawText(e.Graphics, RateText(Rates[i]), speed == Rates[i] ? selectedFont : font, row, color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
