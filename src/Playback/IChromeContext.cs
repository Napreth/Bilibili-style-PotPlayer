using System;
namespace BilibiliPotPlayer
{
    // Runtime adapts feature state; playback never depends on captions/windowing.
    internal interface IChromeContext
    {
        IntPtr Player { get; }
        VolumePopup Volume { get; }
        SpeedButton Speed { get; }
        SpeedPopup SpeedMenu { get; }
        LayeredSurface Subtitles { get; }
        StreamControls Streams { get; }
        bool Small { get; }
        bool Sizing { get; }
        bool AspectLocked { get; }
        bool SubtitlesEnabled { get; }
        bool DanmakuEnabled { get; }
        IntPtr CaptionMenuHandle { get; }
        bool IsOverCaptionMenu(Native.Point point);
        void ToggleMode();
        void ToggleAspect();
        void ToggleSubtitles();
        void ToggleDanmaku();
        void UserPlaybackAction();
    }
}
