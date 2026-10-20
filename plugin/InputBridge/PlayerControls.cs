using System;

namespace BilibiliPotPlayer
{
    // Each player has its own persistent controls. Focus only gates interaction and hover menus.
    internal sealed class PlayerControls : IDisposable
    {
        internal readonly IntPtr Player;
        internal readonly VolumePopup Volume = new VolumePopup();
        internal readonly SpeedButton Speed = new SpeedButton();
        internal readonly SpeedPopup SpeedMenu;
        internal readonly StreamControls Streams = new StreamControls();
        internal readonly SubtitleButton Subtitles = new SubtitleButton();
        internal readonly SubtitlePopup SubtitleMenu;
        internal readonly CaptionCatalog Captions;
        internal readonly TimedOverlay Overlay;
        internal readonly CameraControl Camera;
        internal readonly PlaylistLayout Playlist;
        internal readonly MinimalControls Minimal;
        internal PlayerStage Stage;
        internal PlayerControls(IntPtr player, Action<IntPtr, int> selectRate)
        { Player = player; Playlist = new PlaylistLayout(player); SpeedMenu = new SpeedPopup(Speed, selectRate); Captions = new CaptionCatalog(player, Streams); SubtitleMenu = new SubtitlePopup(Subtitles, Captions); Overlay = new TimedOverlay(Captions); Minimal = new MinimalControls(SubtitleMenu.ToggleDanmaku); Camera = new CameraControl(player, Streams); }
        internal IntPtr FocusedPlayer()
        {
            if (Volume.FocusedPlayer() == Player || SpeedMenu.FocusedPlayer() == Player ||
                Speed.FocusedPlayer() == Player || Streams.FocusedPlayer() == Player || SubtitleMenu.FocusedPlayer() == Player || Camera.PendingPlayer == Player) return Player;
            return IntPtr.Zero;
        }
        internal bool IsOver(Native.Point point)
        { return Minimal.IsOver(point) || Volume.IsOver(point) || SpeedMenu.IsOver(point) || Speed.IsOver(point) || Streams.IsOver(Player, point) || Subtitles.IsOver(point) || SubtitleMenu.IsOver(point); }
        internal void Update(long now, IntPtr focused)
        {
            Playlist.Update();
            System.Drawing.Rectangle client = Native.ClientBounds(Player);
            Stage = ResponsivePolicy.Stage(client.Width, client.Height, Math.Max(96, Native.GetDpiForWindow(Player)) / 96.0);
            if (Stage != PlayerStage.Full && PlaylistLayout.IsOpen(Player)) Playlist.Close();
            bool simple = Stage == PlayerStage.Minimal;
            if (!simple) { Speed.UpdatePlayer(Player, now); Subtitles.UpdatePlayer(Player); }
            else { Speed.Dismiss(); Subtitles.Dismiss(); SpeedMenu.Dismiss(); SubtitleMenu.Dismiss(); }
            Streams.UpdatePlayer(Player, now, Stage == PlayerStage.Full && Speed.Visible);
            Minimal.UpdatePlayer(Player, simple, Captions.Options.DanmakuEnabled);
            Captions.Tick(now); Overlay.MenuWindow = SubtitleMenu.Visible ? SubtitleMenu.Handle : IntPtr.Zero; Overlay.Update(Player, now);
            IntPtr interactive = focused == Player && Native.ControlBarVisible(Player) ? Player : IntPtr.Zero;
            Native.Point pointer;
            if (interactive != IntPtr.Zero && Native.GetCursorPos(out pointer))
            {
                if (SubtitleMenu.IsHoverTarget(Player, pointer)) { Volume.Dismiss(); SpeedMenu.Dismiss(); Streams.DismissPopups(); }
                else if (Streams.IsHoverTarget(Player, pointer)) { Volume.Dismiss(); SpeedMenu.Dismiss(); SubtitleMenu.Dismiss(); }
                else if (SpeedMenu.IsHoverTarget(Player, pointer)) { Volume.Dismiss(); Streams.DismissPopups(); SubtitleMenu.Dismiss(); }
                else if (Volume.IsOver(pointer) || Native.VolumeButtonBounds(Player).Contains(pointer.x, pointer.y) && Native.IsPlayerSurface(Player, pointer))
                { SpeedMenu.Dismiss(); Streams.DismissPopups(); SubtitleMenu.Dismiss(); }
            }
            Volume.UpdateHover(interactive, now);
            SpeedMenu.UpdateHover(interactive, now);
            Streams.UpdateHover(interactive, now);
            SubtitleMenu.UpdateHover(interactive, now);
            Camera.Tick(now, focused == Player);
        }
        public void Dispose()
        { Minimal.Dispose(); Camera.Dispose(); Overlay.Dispose(); Captions.Dispose(); Volume.Dispose(); SpeedMenu.Dispose(); Speed.Dispose(); Streams.Dispose(); SubtitleMenu.Dispose(); Subtitles.Dispose(); }
    }
}
