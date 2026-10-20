using System;
using System.Drawing;

namespace BilibiliPotPlayer
{
    internal static class ControlLayout
    {
        internal static Rectangle Row(Rectangle client, double scale, bool fullscreen)
        {
            // Both modes retain the bottom skin border; the fullscreen row has no side border.
            int border = Pixels(3, scale), side = fullscreen ? 0 : border;
            int height = Pixels(40, scale);
            return new Rectangle(client.Left + side, client.Bottom - border - height,
                Math.Max(0, client.Width - 2 * side), height);
        }
        internal static Rectangle Volume(Rectangle row, double scale)
        {
            int width = Pixels(41, scale);
            // Static LeftMargin/RightMargin inset the background image; its child buttons
            // dock to the control area's edge, immediately inside the outer skin border.
            return new Rectangle(row.Right - 2 * width, row.Top, width, row.Height);
        }
        internal static Rectangle Speed(Rectangle volume, double scale)
        {
            int width = Pixels(58, scale);
            return new Rectangle(volume.Left - Pixels(92, scale) - width, volume.Top, width, volume.Height);
        }
        internal static Rectangle Subtitle(Rectangle volume, double scale)
        { int width = Pixels(92, scale); return new Rectangle(volume.Left - width, volume.Top, width, volume.Height); }
        internal static Rectangle RelativeTo(Rectangle screen, Rectangle parent)
        { return new Rectangle(screen.Left - parent.Left, screen.Top - parent.Top, screen.Width, screen.Height); }
        static int Pixels(int logical, double scale)
        { return (int)Math.Round(logical * scale, MidpointRounding.AwayFromZero); }
    }
}
