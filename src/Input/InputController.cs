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
        readonly Dictionary<int, PlayerAction> heldActions = new Dictionary<int, PlayerAction>();
        readonly ShortcutStore shortcuts;
        int shortcutRevision;
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
        IntPtr middleMouseDown, rightMouseDown;

        internal InputController(Profile profile, IDictionary<IntPtr, IInputSession> controls, ShortcutStore shortcuts)
        {
            this.profile = profile; this.controls = controls;
            this.shortcuts = shortcuts;
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
                PlayerAction held; heldActions.TryGetValue(key, out held); heldActions.Remove(key);
                if (held == PlayerAction.SeekForward) queue.Enqueue(delegate
                {
                    if (hwnd == gesturePlayer) DoRight(right.Up(eventTime));
                    else CancelGestures();
                });
                if (held == PlayerAction.SeekBack) queue.Enqueue(delegate { left.Cancel(); });
                return (IntPtr)1;
            }
            if (hwnd == IntPtr.Zero || !down) return Native.CallNextHookEx(keyboardHook, code, message, data);
            bool shift = Native.Pressed(0x10), ctrl = Native.Pressed(0x11), alt = Native.Pressed(0x12);
            KeyRoute route = InputPolicy.Route(key, shift, ctrl, alt, Native.Pressed(0x5B) || Native.Pressed(0x5C), shortcuts.Current);
            if (route == KeyRoute.Native)
                return Native.CallNextHookEx(keyboardHook, code, message, data);
            if (route == KeyRoute.Blocked) { pressed.Add(key); return (IntPtr)1; }
            bool first = pressed.Add(key);
            PlayerAction action = shortcuts.Current.Find(key, (shift ? 1 : 0) | (ctrl ? 2 : 0) | (alt ? 4 : 0));
            if (first) heldActions[key] = action;
            if (!first && action != PlayerAction.VolumeUp && action != PlayerAction.VolumeDown) return (IntPtr)1;
            queue.Enqueue(delegate
            {
                if (FocusedPlayer() != hwnd) return;
                if (gesturePlayer != IntPtr.Zero && gesturePlayer != hwnd) CancelGestures();
                IInputSession owner; controls.TryGetValue(hwnd, out owner);
                if (action == PlayerAction.SeekForward) { gesturePlayer = hwnd; right.Down(eventTime); }
                else if (action == PlayerAction.SeekBack) { gesturePlayer = hwnd; if (left.Down(eventTime)) Native.Seek(hwnd, -profile.SeekSeconds); }
                else if (action == PlayerAction.VolumeUp) Native.Volume(hwnd, profile.VolumeStepPercent);
                else if (action == PlayerAction.VolumeDown) Native.Volume(hwnd, -profile.VolumeStepPercent);
                else if (action == PlayerAction.PlayPause) TogglePlayback(hwnd);
                else if (action == PlayerAction.Previous) Native.Set(hwnd, Native.SetPlayOrder, 0);
                else if (action == PlayerAction.Next) Native.Set(hwnd, Native.SetPlayOrder, 1);
                else if (action == PlayerAction.Fullscreen) Native.Fullscreen(hwnd);
                else if (action == PlayerAction.ExitFullscreen) { if (Native.IsFullscreen(hwnd)) Native.Fullscreen(hwnd); }
                else if (action == PlayerAction.Mute) Native.ToggleMute(hwnd);
                else if (action == PlayerAction.Sidebar && owner != null) owner.ToggleSidebar();
                else if (action == PlayerAction.Subtitles && owner != null) owner.ToggleSubtitles();
                else if (action == PlayerAction.SmallWindow && owner != null) owner.ToggleMode();
                else if (action == PlayerAction.Danmaku && owner != null) owner.ToggleDanmaku();
                else if (action == PlayerAction.NormalSpeed) SetBaseSpeed(hwnd, 1000);
                else if (action == PlayerAction.DoubleSpeed) SetBaseSpeed(hwnd, 2000);
                else if (action == PlayerAction.Settings && owner != null) owner.OpenSettings();
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
            if (hwnd == IntPtr.Zero)
                foreach (KeyValuePair<IntPtr, IInputSession> entry in controls)
                    if (entry.Value.DialogForeground && Native.IsPlayerSurface(entry.Key, input.point))
                    { hwnd = entry.Key; if (msg == 0x201 || msg == 0x204 || msg == 0x207) Native.SetForegroundWindow(hwnd); break; }
            if (msg == 0x205 && rightMouseDown != IntPtr.Zero)
            {
                IntPtr clicked = rightMouseDown; rightMouseDown = IntPtr.Zero; IInputSession menuOwner;
                if (hwnd == clicked && Native.IsPlayerSurface(clicked, input.point) && controls.TryGetValue(clicked, out menuOwner))
                    queue.Enqueue(delegate { CancelGestures(); videoClick.Cancel(); menuOwner.ShowContextMenu(input.point.x, input.point.y); });
                return (IntPtr)1;
            }
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
            if (msg == 0x204 && !overPopup && owner != null)
            { rightMouseDown = hwnd; return (IntPtr)1; }
            if (overPopup && msg >= 0x204 && msg <= 0x206) return Native.CallNextHookEx(mouseHook, code, message, data);
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
            DoRight(right.Cancel()); left.Cancel(); pressed.Clear(); heldActions.Clear(); wheelRemainder = 0; gesturePlayer = IntPtr.Zero;
        }
        internal void Tick()
        {
            if (shortcutRevision != shortcuts.Revision) { queue.Clear(); CancelGestures(); videoClick.Cancel(); shortcutRevision = shortcuts.Revision; }
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
