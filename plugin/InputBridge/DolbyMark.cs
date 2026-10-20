using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal static class DolbyMark
    {
        static readonly Dictionary<string, Rectangle> inkCache = new Dictionary<string, Rectangle>();
        static readonly SvgGlyph glyph = new SvgGlyph("BilibiliPotPlayer.DolbyDoubleD.svg");
        // Keep the official SVG paths and their proportions.
        // Size against visible glyph ink, rather than the font's line box and leading.
        static Rectangle TextInk(Font font, string text, int rowHeight)
        {
            string key = font.Name + ":" + font.Size.ToString(CultureInfo.InvariantCulture) + ":" + font.Style + ":" + rowHeight + ":" + text;
            Rectangle bounds;
            if (inkCache.TryGetValue(key, out bounds)) return bounds;
            int width = Math.Max(1, TextRenderer.MeasureText(text, font).Width + 4);
            using (Bitmap bitmap = new Bitmap(width, Math.Max(1, rowHeight)))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);
                TextRenderer.DrawText(graphics, text, font, new Rectangle(0, 0, width, rowHeight), Color.White,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                int top = rowHeight, bottom = -1;
                for (int y = 0; y < rowHeight; y++) for (int x = 0; x < width; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    if (Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) >= 64) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
                }
                bounds = bottom >= top ? new Rectangle(0, top, 0, bottom - top + 1) :
                    new Rectangle(0, Math.Max(0, (rowHeight - (int)font.Size) / 2), 0, Math.Max(1, (int)font.Size));
            }
            if (inkCache.Count >= 64) inkCache.Clear();
            inkCache[key] = bounds; return bounds;
        }
        internal static int Advance(Font font, string text, int rowHeight, float scale)
        { return (int)Math.Ceiling(TextInk(font, text, rowHeight).Height * glyph.Aspect + 4 * scale); }
        internal static int Draw(Graphics graphics, float x, int rowTop, int rowHeight, Font font, string text, float scale, Color color)
        {
            Rectangle ink = TextInk(font, text, rowHeight);
            float height = ink.Height, y = rowTop + ink.Top;
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                glyph.Draw(graphics, x, y, height, color);
            }
            finally { graphics.Restore(state); }
            return (int)Math.Ceiling(height * glyph.Aspect + 4 * scale);
        }
    }
}
