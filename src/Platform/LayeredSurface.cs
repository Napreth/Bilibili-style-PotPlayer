using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;


namespace BilibiliPotPlayer
{
    internal abstract class LayeredSurface : Form
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
                    p.ExStyle &= ~8; if (embeddedParent != IntPtr.Zero) p.Parent = embeddedParent;
                }
                return p;
            }
        }
        protected LayeredSurface(string title, bool embedded = false)
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
        protected void Reveal(bool raise = true)
        {
            if (embedded)
            {
                if (!Native.IsWindow(embeddedParent)) { Dismiss(); return; }
                if (!Visible) Show();
                if (raise) Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0013);
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
}
