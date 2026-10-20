using System;
using System.Drawing;

namespace BilibiliPotPlayer
{
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
        internal readonly WindowMode Mode;
        internal readonly WindowLayout Layout;
        internal readonly PlaybackChrome Chrome;
        internal PlayerStage Stage { get { return Mode.Stage; } }
        internal PlayerControls(IntPtr player, Action<IntPtr, int> selectRate)
        {
            Player = player; Playlist = new PlaylistLayout(player); Layout = new WindowLayout(player);
            Mode = new WindowMode(player, Streams); SpeedMenu = new SpeedPopup(Speed, selectRate);
            Captions = new CaptionCatalog(player, Streams); SubtitleMenu = new SubtitlePopup(Subtitles, Captions);
            Overlay = new TimedOverlay(Captions); Chrome = new PlaybackChrome(this); Camera = new CameraControl(player, Streams);
        }
        internal IntPtr FocusedPlayer()
        {
            if (Volume.FocusedPlayer() == Player || SpeedMenu.FocusedPlayer() == Player ||
                Chrome.FocusedPlayer() == Player || Streams.FocusedPlayer() == Player || SubtitleMenu.FocusedPlayer() == Player || Camera.PendingPlayer == Player) return Player;
            return IntPtr.Zero;
        }
        internal bool IsOver(Native.Point point)
        { return Chrome.IsOver(point) || Volume.IsOver(point) || SpeedMenu.IsOver(point) || Streams.IsOver(Player, point) || SubtitleMenu.IsOver(point); }
        internal void Update(long now, IntPtr focused)
        {
            Mode.Update();
            Rectangle client = Native.ClientBounds(Player);
            Layout.Update(Stage == PlayerStage.Small, Stage == PlayerStage.Small && Mode.AspectLocked ? Mode.Ratio : 0);
            if (Layout.Available) Playlist.Update(Stage == PlayerStage.Normal && ResponsivePolicy.SidebarAllowed(client.Width, client.Height, Math.Max(96, Native.GetDpiForWindow(Player)) / 96.0));
            Streams.UpdatePlayer(Player, now, false, true);
            Captions.Tick(now);
            Overlay.MenuWindow = SubtitleMenu.Visible ? SubtitleMenu.Handle : IntPtr.Zero; Overlay.Update(Player, now);
            Chrome.UpdatePlayer(now);
            IntPtr interactive = focused == Player && Chrome.ControlsVisible ? Player : IntPtr.Zero;
            Native.Point pointer;
            if (interactive != IntPtr.Zero && Native.GetCursorPos(out pointer))
            {
                if (SubtitleMenu.IsHoverTarget(Player, pointer)) { Volume.Dismiss(); SpeedMenu.Dismiss(); Streams.DismissPopups(); }
                else if (Streams.IsHoverTarget(Player, pointer)) { Volume.Dismiss(); SpeedMenu.Dismiss(); SubtitleMenu.Dismiss(); }
                else if (SpeedMenu.IsHoverTarget(Player, pointer)) { Volume.Dismiss(); Streams.DismissPopups(); SubtitleMenu.Dismiss(); }
                else if (Volume.IsOver(pointer) || Volume.ButtonAnchor.Contains(pointer.x, pointer.y) && Native.IsPlayerSurface(Player, pointer))
                { SpeedMenu.Dismiss(); Streams.DismissPopups(); SubtitleMenu.Dismiss(); }
            }
            Volume.UpdateHover(interactive, now); SpeedMenu.UpdateHover(interactive, now);
            Streams.UpdateHover(interactive, now); SubtitleMenu.UpdateHover(interactive, now);
            Camera.Tick(now, focused == Player);
        }
        public void Dispose()
        { Chrome.Dispose(); Camera.Dispose(); Overlay.Dispose(); Captions.Dispose(); Volume.Dispose(); SpeedMenu.Dispose(); Speed.Dispose(); Streams.Dispose(); SubtitleMenu.Dispose(); Subtitles.Dispose(); Layout.Dispose(); }
    }
}
