using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;


namespace BilibiliPotPlayer
{
    internal sealed class InputController : IDisposable
    {
        readonly Profile profile;
        readonly RightGesture right;
        readonly RewindGesture left;
        readonly Queue<Action> queue = new Queue<Action>();
        readonly HashSet<int> pressed = new HashSet<int>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Native.HookProc keyboardProc, mouseProc;
        readonly IDictionary<IntPtr, IInputSession> controls;
        IntPtr keyboardHook, mouseHook, gesturePlayer;
        long savedSpeed;
        bool speedApplied;
        int wheelRemainder;
        IntPtr muteMouseDown;
        bool speedMouseDown;
        readonly VideoClickGesture videoClick = new VideoClickGesture(SystemInformation.DoubleClickTime, 6);
        IntPtr videoClickPlayer, leftMouseDown;
        IntPtr middleMouseDown;

        internal InputController(Profile profile, IDictionary<IntPtr, IInputSession> controls)
        {
            this.profile = profile; this.controls = controls;
            right = new RightGesture(profile.HoldMilliseconds);
            left = new RewindGesture(profile.HoldMilliseconds, profile.RewindIntervalMilliseconds);
            keyboardProc = Keyboard; mouseProc = Mouse;
            IntPtr module = Native.GetModuleHandle(null);
            keyboardHook = Native.SetWindowsHookEx(13, keyboardProc, module, 0);
            mouseHook = Native.SetWindowsHookEx(14, mouseProc, module, 0);
            if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
            { Dispose(); throw new InvalidOperationException("Could not install input hooks"); }
        }
        IntPtr Keyboard(int code, IntPtr message, IntPtr data)
        {
            if (code < 0) return Native.CallNextHookEx(keyboardHook, code, message, data);
            int msg = message.ToInt32(); bool down = msg == 0x100 || msg == 0x104;
            bool up = msg == 0x101 || msg == 0x105;
            if (!down && !up) return Native.CallNextHookEx(keyboardHook, code, message, data);
            Native.Keyboard input = (Native.Keyboard)Marshal.PtrToStructure(data, typeof(Native.Keyboard));
            int key = (int)input.vk;
            IntPtr hwnd = FocusedPlayer();
            long eventTime = clock.ElapsedMilliseconds;
            if (up && pressed.Remove(key))
            {
                if (key == 0x27) queue.Enqueue(delegate
                {
                    if (hwnd == gesturePlayer) DoRight(right.Up(eventTime));
                    else CancelGestures();
                });
                if (key == 0x25) queue.Enqueue(delegate { left.Cancel(); });
                return (IntPtr)1;
            }
            if (hwnd == IntPtr.Zero || !down) return Native.CallNextHookEx(keyboardHook, code, message, data);
            bool shift = Native.Pressed(0x10), ctrl = Native.Pressed(0x11), alt = Native.Pressed(0x12);
            KeyRoute route = InputPolicy.Route(key, shift, ctrl, alt, Native.Pressed(0x5B) || Native.Pressed(0x5C));
            if (route == KeyRoute.Native)
                return Native.CallNextHookEx(keyboardHook, code, message, data);
            if (route == KeyRoute.Blocked) { pressed.Add(key); return (IntPtr)1; }
            bool first = pressed.Add(key);
            if (!first && key != 0x26 && key != 0x28) return (IntPtr)1;
            queue.Enqueue(delegate
            {
                if (gesturePlayer != IntPtr.Zero && gesturePlayer != hwnd) CancelGestures();
                if (key == 0x27) { gesturePlayer = hwnd; right.Down(eventTime); }
                else if (key == 0x25) { gesturePlayer = hwnd; if (left.Down(eventTime)) Native.Seek(hwnd, -profile.SeekSeconds); }
                else if (key == 0x26) Native.Volume(hwnd, profile.VolumeStepPercent);
                else if (key == 0x28) Native.Volume(hwnd, -profile.VolumeStepPercent);
                else if (key == 0x20) TogglePlayback(hwnd);
                else if (key == 0xBC || key == 0xDB) Native.Set(hwnd, Native.SetPlayOrder, 0);
                else if (key == 0xBE || key == 0xDD) Native.Set(hwnd, Native.SetPlayOrder, 1);
                else if (key == 0x46) Native.Fullscreen(hwnd);
                else if (key == 0x1B) { if (Native.IsFullscreen(hwnd)) Native.Fullscreen(hwnd); }
                else if (key == 0x4D) Native.ToggleMute(hwnd);
                else if (key == 0x58) { IInputSession owner; if (controls.TryGetValue(hwnd, out owner)) owner.ToggleSidebar(); }
                else if (key == 0x43) { IInputSession subtitleOwner; if (controls.TryGetValue(hwnd, out subtitleOwner)) subtitleOwner.ToggleSubtitles(); }
                else if (key == 0x53) { IInputSession modeOwner; if (controls.TryGetValue(hwnd, out modeOwner)) modeOwner.ToggleMode(); }
                else if (key == 0x44) { IInputSession captionOwner; if (controls.TryGetValue(hwnd, out captionOwner)) captionOwner.ToggleDanmaku(); }
                else if (shift && key == 0x31) SetBaseSpeed(hwnd, 1000);
                else if (shift && key == 0x32) SetBaseSpeed(hwnd, 2000);
                // Enter intentionally has no action.
            });
            return (IntPtr)1;
        }
        IntPtr Mouse(int code, IntPtr message, IntPtr data)
        {
            if (code < 0) return Native.CallNextHookEx(mouseHook, code, message, data);
            int msg = message.ToInt32();
            Native.Mouse input = (Native.Mouse)Marshal.PtrToStructure(data, typeof(Native.Mouse));
            if (input.extra.ToUInt64() == Native.VirtualMouseTag) return Native.CallNextHookEx(mouseHook, code, message, data);
            IntPtr hwnd = FocusedPlayer();
            if (middleMouseDown != IntPtr.Zero && (msg == 0x200 || msg == 0x208))
            {
                IntPtr middlePlayer = middleMouseDown; IInputSession cameraOwner;
                if (controls.TryGetValue(middlePlayer, out cameraOwner))
                    queue.Enqueue(delegate { if (hwnd != middlePlayer) cameraOwner.CancelCamera(); else if (msg == 0x208) cameraOwner.CameraUp(input.point.x, input.point.y); else cameraOwner.CameraMove(input.point.x, input.point.y); });
                if (msg == 0x208) { middleMouseDown = IntPtr.Zero; return (IntPtr)1; }
            }
            if (msg == 0x200 && leftMouseDown != IntPtr.Zero)
            { queue.Enqueue(delegate { videoClick.Move(input.point.x, input.point.y); }); }
            if (msg == 0x202 && leftMouseDown != IntPtr.Zero)
            {
                IntPtr clicked = leftMouseDown; leftMouseDown = IntPtr.Zero; long now = clock.ElapsedMilliseconds;
                queue.Enqueue(delegate {
                    if (hwnd != clicked || !Native.IsVideoSurface(clicked, input.point)) { videoClick.Cancel(); return; }
                    if (videoClick.Up(input.point.x, input.point.y, now) == ClickAction.Double) Native.Fullscreen(clicked);
                });
                return (IntPtr)1;
            }
            if (msg == 0x202 && speedMouseDown) { speedMouseDown = false; return (IntPtr)1; }
            if (msg == 0x202 && muteMouseDown != IntPtr.Zero)
            {
                IntPtr clicked = muteMouseDown; muteMouseDown = IntPtr.Zero;
                if (hwnd == clicked && Native.VolumeButtonBounds(hwnd).Contains(input.point.x, input.point.y))
                    queue.Enqueue(delegate { Native.ToggleMute(clicked); });
                return (IntPtr)1;
            }
            if (hwnd == IntPtr.Zero) return Native.CallNextHookEx(mouseHook, code, message, data);
            IInputSession owner;
            bool overPopup = controls.TryGetValue(hwnd, out owner) && owner.IsOver(input.point.x, input.point.y);
            // Only consume input physically over this player, not another app under the pointer.
            if (!overPopup && !Native.IsPlayerSurface(hwnd, input.point))
                return Native.CallNextHookEx(mouseHook, code, message, data);
            Native.Rect r; Native.GetWindowRect(hwnd, out r);
            if (!overPopup && (input.point.x < r.left || input.point.x >= r.right || input.point.y < r.top || input.point.y >= r.bottom))
                return Native.CallNextHookEx(mouseHook, code, message, data);
            if (msg == 0x207 && !overPopup && Native.IsVideoSurface(hwnd, input.point) && owner != null)
            {
                middleMouseDown = hwnd; long now = clock.ElapsedMilliseconds;
                queue.Enqueue(delegate { if (videoClickPlayer == hwnd && videoClick.ResolvePending() == ClickAction.Single) TogglePlayback(hwnd); owner.CameraDown(input.point.x, input.point.y, now); }); return (IntPtr)1;
            }
            if ((msg == 0x201 || msg == 0x203) && owner != null) queue.Enqueue(delegate { owner.UserPlaybackAction(); });
            if ((msg == 0x201 || msg == 0x203) && !overPopup && Native.IsVideoSurface(hwnd, input.point))
            {
                leftMouseDown = hwnd;
                queue.Enqueue(delegate { if (videoClickPlayer != hwnd) videoClick.Cancel(); videoClickPlayer = hwnd; videoClick.Down(input.point.x, input.point.y); });
                return (IntPtr)1;
            }
            if (owner == null && !overPopup && (msg == 0x201 || msg == 0x203) && Native.VolumeButtonBounds(hwnd).Contains(input.point.x, input.point.y))
            { muteMouseDown = hwnd; return (IntPtr)1; }
            if (owner == null && !overPopup && (msg == 0x201 || msg == 0x203) && Native.SpeedButtonBounds(hwnd).Contains(input.point.x, input.point.y))
            { speedMouseDown = true; return (IntPtr)1; }
            if (msg == 0x20A)
            {
                if (owner != null && owner.IsOverCaptionMenu(input.point.x, input.point.y)) return Native.CallNextHookEx(mouseHook, code, message, data);
                if (!Native.Pressed(0x10) && !Native.Pressed(0x11) && !Native.Pressed(0x12))
                {
                    int delta = unchecked((short)(input.data >> 16));
                    queue.Enqueue(delegate
                    {
                        wheelRemainder += delta;
                        int steps = wheelRemainder / 120; wheelRemainder %= 120;
                        if (steps != 0) Native.Volume(hwnd, steps * profile.VolumeStepPercent);
                    });
                }
                return (IntPtr)1;
            }
            // Right, middle, X buttons and horizontal wheel have no player actions.
            if (msg >= 0x204 && msg <= 0x209 || msg >= 0x20B && msg <= 0x20E) return (IntPtr)1;
            return Native.CallNextHookEx(mouseHook, code, message, data);
        }
        void TogglePlayback(IntPtr hwnd)
        { IInputSession owner; if (controls.TryGetValue(hwnd, out owner)) owner.UserPlaybackAction(); Native.Set(hwnd, Native.SetPlayStatus, 0); }
        internal void SetBaseSpeed(IntPtr hwnd, int speed)
        {
            if (speedApplied && hwnd == gesturePlayer) savedSpeed = speed;
            else Native.Set(hwnd, Native.SetSpeed, speed);
        }
        internal IntPtr FocusedPlayer()
        {
            IntPtr player = Native.FocusedPlayer();
            if (player != IntPtr.Zero) return player;
            foreach (IInputSession owner in controls.Values)
            { player = owner.FocusedPlayer(); if (player != IntPtr.Zero) return player; }
            return IntPtr.Zero;
        }
        void DoRight(RightAction action)
        {
            if (action == RightAction.SeekForward) Native.Seek(gesturePlayer, profile.SeekSeconds);
            else if (action == RightAction.StartSpeed)
            {
                long speed;
                if (Native.Get(gesturePlayer, Native.GetSpeed, out speed) && speed >= 200 && speed <= 12000)
                { savedSpeed = speed; speedApplied = true; Native.Set(gesturePlayer, Native.SetSpeed, 2000); }
            }
            else if (action == RightAction.RestoreSpeed && speedApplied)
            { Native.Set(gesturePlayer, Native.SetSpeed, savedSpeed); speedApplied = false; }
        }
        void CancelGestures()
        {
            DoRight(right.Cancel()); left.Cancel(); pressed.Clear(); wheelRemainder = 0; gesturePlayer = IntPtr.Zero;
        }
        internal void Tick()
        {
            while (queue.Count > 0) queue.Dequeue()();
            IntPtr player = FocusedPlayer();
            long now = clock.ElapsedMilliseconds;
            if (videoClickPlayer != player) videoClick.Cancel();
            else if (videoClick.Tick(now) == ClickAction.Single) TogglePlayback(videoClickPlayer);
            if (gesturePlayer != IntPtr.Zero && FocusedPlayer() != gesturePlayer) { CancelGestures(); return; }
            DoRight(right.Tick(clock.ElapsedMilliseconds));
            if (left.Tick(clock.ElapsedMilliseconds)) Native.Seek(gesturePlayer, -profile.SeekSeconds);
        }
        public void Dispose()
        {
            CancelGestures();
            videoClick.Cancel();
            if (keyboardHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
            if (mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        }
    }
}
