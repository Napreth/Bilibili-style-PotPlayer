using System;
using System.Drawing;
using System.Runtime.InteropServices;
namespace BilibiliPotPlayer
{
    internal static class LayeredBitmap
    {
        [StructLayout(LayoutKind.Sequential)] struct Size2 { internal int Width, Height; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { internal byte Op, Flags, Alpha, Format; }
        internal static void Present(IntPtr handle, Bitmap image)
        {
            IntPtr screen = GetDC(IntPtr.Zero), memory = CreateCompatibleDC(screen), bitmap = image.GetHbitmap(Color.FromArgb(0)), previous = SelectObject(memory, bitmap);
            try
            {
                Size2 size = new Size2 { Width = image.Width, Height = image.Height };
                Native.Point origin = new Native.Point(); Blend blend = new Blend { Alpha = 255, Format = 1 };
                UpdateLayeredWindow(handle, screen, IntPtr.Zero, ref size, memory, ref origin, 0, ref blend, 2);
            }
            finally { SelectObject(memory, previous); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
        }
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr window, IntPtr dst, IntPtr position, ref Size2 size, IntPtr src, ref Native.Point origin, uint key, ref Blend blend, uint flags);
    }
}
