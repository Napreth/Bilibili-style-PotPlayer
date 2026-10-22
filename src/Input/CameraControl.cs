using System;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    // Keep PotPlayer's projection, field of view, pitch limits and camera sensitivity.
    // Its camera accepts left-drag input; the bridge supplies that input for a middle drag.
    internal sealed class CameraControl : IDisposable
    {
        readonly IntPtr player;
        readonly IMediaContext streams;
        readonly NativeMenuSnapshot menu = new NativeMenuSnapshot();
        readonly CameraGesture gesture = new CameraGesture();
        readonly CameraPlaybackGuard playback = new CameraPlaybackGuard();
        string source;
        bool pending, ended, modeKnown, enabled, preview, dragging;
        bool forceMode, autoMode;
        int dragPhase;
        long phaseAt, lastTick;
        internal CameraControl(IntPtr player, IMediaContext streams) { this.player = player; this.streams = streams; }
        internal IntPtr PendingPlayer
        { get { return pending && (menu.Loading || !menu.Ready) && Native.GetForegroundWindow() == player ? player : IntPtr.Zero; } }
        internal void Down(Native.Point point, long now)
        {
            if (pending || dragging) return;
            lastTick = now;
            gesture.Begin(point.x, point.y); ended = false; pending = true;
            if (!modeKnown && !menu.Request(player, 21104, now)) { Cancel(); return; }
        }
        internal void Move(Native.Point point) { if (pending || dragging) gesture.Move(point.x, point.y); }
        internal void Up(Native.Point point) { if (pending || dragging) { gesture.End(point.x, point.y); ended = true; } }
        internal void Tick(long now, bool focused)
        {
            lastTick = now;
            menu.Tick(now);
            if (source != streams.SourcePath)
            { Cancel(); playback.Cancel(); modeKnown = false; source = streams.SourcePath; return; }
            RestorePlayback();
            if (!focused) { Cancel(); modeKnown = false; return; }
            if (!pending && !dragging) return;
            if (!modeKnown)
            {
                if (!menu.Ready) return;
                NativeMenuSnapshot.Item force = menu.Items.Find(item => item.Command == 11001);
                NativeMenuSnapshot.Item auto = menu.Items.Find(item => item.Command == 11002);
                NativeMenuSnapshot.Item views = menu.Items.Find(item => item.Command == 11003);
                forceMode = force != null && force.Checked && force.Enabled; autoMode = auto != null && auto.Checked && auto.Enabled;
                enabled = CameraPolicy.Enabled(forceMode, autoMode, streams.CameraCandidate);
                preview = views != null && views.Checked; modeKnown = true;
                // The preset thumbnails are a native renderer option, not skin buttons.
            }
            // Metadata may finish after a very early first press; do not cache an empty catalog.
            enabled = CameraPolicy.Enabled(forceMode, autoMode, streams.CameraCandidate);
            if (!enabled) { Cancel(); return; }
            if (preview) { HidePreview(); preview = false; }
            if (gesture.Moved && !dragging)
            {
                Native.Point current = new Native.Point { x = gesture.X, y = gesture.Y };
                if (!Native.IsVideoSurface(player, current)) return;
                Native.SetCursorPos(gesture.StartX, gesture.StartY);
                PreservePlayback();
                Native.VirtualLeft(2);
                dragging = true; pending = false; dragPhase = 1; phaseAt = now + 15;
                return;
            }
            // Preserve down/move/up ordering even if a fast first drag was released
            // while its native mode menu was still being captured.
            if (dragging && dragPhase != 0)
            {
                if (now < phaseAt) return;
                if (dragPhase == 1) { Native.SetCursorPos(gesture.X, gesture.Y); dragPhase = 2; phaseAt = now + 15; return; }
                dragPhase = 0;
            }
            if (ended)
            {
                if (dragging) EndDrag();
                else if (!gesture.Moved) Center();
                pending = ended = false; gesture.Cancel();
            }
        }
        void Command(int command) { long unused; Native.TrySend(player, Native.WM_COMMAND, command, 0, out unused); }
        void HidePreview()
        {
            long osd; bool restore = Native.Get(player, Native.GetOsd, out osd);
            try { if (restore) Native.Set(player, Native.SetOsd, 0); Command(11003); }
            finally { if (restore) Native.Set(player, Native.SetOsd, osd); }
        }
        void Center()
        {
            IntPtr surface = Native.VideoSurface(player); Native.Rect bounds; Native.Point pointer;
            if (surface == IntPtr.Zero || !Native.GetClientRect(surface, out bounds) || !Native.GetCursorPos(out pointer)) return;
            PreservePlayback();
            // Native 4x90 previews are centered vertically. The first preview selects 0°.
            // Dispatch the selection synchronously, so no preview panel or fake click remains.
            int x = (int)(30 * Math.Max(96, Native.GetDpiForWindow(player)) / 96f), y = (int)(bounds.bottom * .26);
            Native.Point preset = new Native.Point { x = x, y = y }; Native.ClientToScreen(surface, ref preset);
            long osd; bool restore = Native.Get(player, Native.GetOsd, out osd);
            if (restore) Native.Set(player, Native.SetOsd, 0);
            Command(11003);
            try
            {
                Native.SetCursorPos(preset.x, preset.y);
                long unused, location = (y << 16) | x;
                Native.TrySend(surface, 0x201, 1, location, out unused);
                Native.TrySend(surface, 0x202, 0, location, out unused);
            }
            finally { Command(11003); Native.SetCursorPos(pointer.x, pointer.y); if (restore) Native.Set(player, Native.SetOsd, osd); RestorePlayback(); }
        }
        void PreservePlayback()
        { long status; if (Native.Get(player, Native.GetPlayStatus, out status)) playback.Arm((int)status, lastTick, SystemInformation.DoubleClickTime + 100); }
        void RestorePlayback()
        { if (!playback.Active) return; long status; if (Native.Get(player, Native.GetPlayStatus, out status)) { int desired = playback.Restore((int)status, lastTick); if (desired != 0) Native.Set(player, Native.SetPlayStatus, desired); } }
        internal void UserPlaybackAction() { playback.Cancel(); }
        void EndDrag() { if (dragging) { PreservePlayback(); Native.VirtualLeft(4); dragging = false; } dragPhase = 0; }
        internal void Cancel() { EndDrag(); pending = ended = false; gesture.Cancel(); }
        public void Dispose() { Cancel(); menu.Dispose(); }
    }
}
