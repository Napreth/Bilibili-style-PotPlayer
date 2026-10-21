using System;

namespace BilibiliPotPlayer
{
    internal sealed class ChromeActivity
    {
        internal const int IdleMilliseconds = 3000;
        bool initialized, wasInside, wasTimed;
        int x, y;
        long moved;
        internal bool Visible(long now, int px, int py, bool inside, bool timed, bool interacting)
        {
            if (!initialized || px != x || py != y || inside && !wasInside || timed != wasTimed) moved = now;
            initialized = true; x = px; y = py; wasInside = inside; wasTimed = timed;
            if (interacting) { moved = now; return true; }
            return inside && (!timed || now - moved < IdleMilliseconds);
        }
    }
    internal enum KeyRoute { Native, Mapped, Blocked }
    internal static class InputPolicy
    {
        internal static KeyRoute Route(int key, bool shift, bool ctrl, bool alt, bool windows)
        {
            if (windows || key == 0x10 || key == 0x11 || key == 0x12 || key >= 0xA0 && key <= 0xA5)
                return KeyRoute.Native;
            if (alt && (key == 0x09 || key == 0x73 || key == 0x1B || key == 0x20) || ctrl && key == 0x1B)
                return KeyRoute.Native;
            if (key >= 0x70 && key <= 0x7B) return key == 0x74 ? KeyRoute.Blocked : KeyRoute.Native;
            if (ctrl || alt) return KeyRoute.Blocked;
            if (shift) return key == 0x31 || key == 0x32 ? KeyRoute.Mapped : KeyRoute.Blocked;
            if (key == 0x25 || key == 0x27 || key == 0x26 || key == 0x28 || key == 0x20 ||
                key == 0xBC || key == 0xDB || key == 0xBE || key == 0xDD || key == 0x1B ||
                key == 0x43 || key == 0x53 || key == 0x44 || key == 0x46 || key == 0x4D || key == 0x58 || key == 0x0D) return KeyRoute.Mapped;
            return KeyRoute.Blocked;
        }
    }
    internal enum ClickAction { None, Single, Double }
    internal static class CameraPolicy
    {
        internal static bool Enabled(bool forced, bool automatic, bool spherical) { return forced || automatic && spherical; }
    }
    internal sealed class CameraPlaybackGuard
    {
        int expected;
        long until;
        internal bool Active { get { return expected != 0; } }
        internal void Arm(int status, long now, int delay)
        { expected = status == 1 || status == 2 ? status : 0; until = now + delay; }
        internal int Restore(int current, long now)
        {
            if (now > until || current == 0) { Cancel(); return 0; }
            return expected != 0 && (current == 1 || current == 2) && current != expected ? expected : 0;
        }
        internal void Cancel() { expected = 0; }
    }
    internal sealed class CameraGesture
    {
        internal bool Down { get; private set; }
        internal bool Moved { get; private set; }
        internal int StartX, StartY, X, Y;
        internal void Begin(int x, int y) { Down = true; Moved = false; StartX = X = x; StartY = Y = y; }
        internal void Move(int x, int y)
        { X = x; Y = y; if (Down && (Math.Abs(x - StartX) > 6 || Math.Abs(y - StartY) > 6)) Moved = true; }
        internal void End(int x, int y) { Move(x, y); Down = false; }
        internal void Cancel() { Down = Moved = false; }
    }
    internal sealed class VideoClickGesture
    {
        readonly int delay, tolerance;
        bool down, moved, pending;
        int x, y, lastX, lastY;
        long deadline;
        internal VideoClickGesture(int delay, int tolerance) { this.delay = delay; this.tolerance = tolerance; }
        internal void Down(int px, int py) { down = true; moved = false; x = px; y = py; }
        internal void Move(int px, int py)
        { if (down && (Math.Abs(px - x) > tolerance || Math.Abs(py - y) > tolerance)) moved = true; }
        internal ClickAction Up(int px, int py, long now)
        {
            Move(px, py); if (!down) return ClickAction.None; down = false;
            if (moved) return ClickAction.None;
            if (pending && now <= deadline && Math.Abs(px - lastX) <= tolerance && Math.Abs(py - lastY) <= tolerance)
            { pending = false; return ClickAction.Double; }
            pending = true; deadline = now + delay; lastX = px; lastY = py; return ClickAction.None;
        }
        internal ClickAction Tick(long now)
        { if (!pending || down || now < deadline) return ClickAction.None; pending = false; return ClickAction.Single; }
        internal ClickAction ResolvePending()
        { bool single = pending && !down; Cancel(); return single ? ClickAction.Single : ClickAction.None; }
        internal void Cancel() { down = pending = false; }
    }
    // No Windows APIs here: the transitions can be checked without injecting input.
    public enum RightAction { None, SeekForward, StartSpeed, RestoreSpeed }

    public sealed class RightGesture
    {
        readonly int holdMilliseconds;
        bool down, accelerated;
        long started;
        public RightGesture(int holdMilliseconds) { this.holdMilliseconds = holdMilliseconds; }
        public void Down(long now) { if (!down) { down = true; started = now; } }
        public RightAction Tick(long now)
        {
            if (down && !accelerated && now - started >= holdMilliseconds)
            { accelerated = true; return RightAction.StartSpeed; }
            return RightAction.None;
        }
        public RightAction Up(long now)
        {
            if (!down) return RightAction.None;
            down = false;
            if (accelerated) { accelerated = false; return RightAction.RestoreSpeed; }
            return now - started < holdMilliseconds ? RightAction.SeekForward : RightAction.None;
        }
        public RightAction Cancel()
        {
            down = false;
            if (!accelerated) return RightAction.None;
            accelerated = false;
            return RightAction.RestoreSpeed;
        }
    }

    public sealed class RewindGesture
    {
        readonly int delay, interval;
        bool down;
        long next;
        public RewindGesture(int delay, int interval) { this.delay = delay; this.interval = interval; }
        public bool Down(long now)
        {
            if (down) return false;
            down = true; next = now + delay; return true;
        }
        public bool Tick(long now)
        {
            if (!down || now < next) return false;
            // Avoid a burst of queued seeks after the player/UI has been busy.
            next = now + interval; return true;
        }
        public void Cancel() { down = false; }
    }
}
