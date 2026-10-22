using System;
namespace BilibiliPotPlayer
{
    internal interface IInputSession
    {
        IntPtr FocusedPlayer();
        bool IsOver(int x, int y);
        bool IsOverCaptionMenu(int x, int y);
        void ToggleSidebar();
        void ToggleSubtitles();
        void ToggleMode();
        void ToggleDanmaku();
        void CameraDown(int x, int y, long now);
        void CameraMove(int x, int y);
        void CameraUp(int x, int y);
        void CancelCamera();
        void UserPlaybackAction();
    }
}
