using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal static class DanmakuSettings
    {
        internal static readonly string[] Fonts = { "SimHei", "SimSun", "NSimSun", "FangSong", "Microsoft YaHei", "Microsoft YaHei UI", "Noto Sans CJK SC" };
        internal static readonly int[] Regions = { 10, 25, 50, 75, 100 }, Speeds = { 40, 70, 100, 130, 160 };
        internal static int Snap(int value, int[] steps)
        { int index = 0; for (int i = 1; i < steps.Length; i++) if (Math.Abs((long)value - steps[i]) < Math.Abs((long)value - steps[index])) index = i; return steps[index]; }
        internal static int Index(int value, int[] steps) { return Array.IndexOf(steps, Snap(value, steps)); }
        internal static int At(float ratio, int[] steps)
        { return steps[Math.Max(0, Math.Min(steps.Length - 1, (int)Math.Floor(ratio * (steps.Length - 1) + 0.5f)))]; }
        internal static Font FontFor(float size, CaptionOptions options) { return TextFont(size, options, true); }
        internal static Font TextFont(float size, CaptionOptions options, bool danmaku)
        {
            using (Font regular = new Font(danmaku ? options.DanmakuFont : options.SubtitleFont, size, GraphicsUnit.Pixel))
            {
                FontStyle style = FontStyle.Regular;
                if ((danmaku ? options.DanmakuBold : options.SubtitleBold) && regular.FontFamily.IsStyleAvailable(FontStyle.Bold)) style |= FontStyle.Bold;
                if ((danmaku ? options.DanmakuItalic : options.SubtitleItalic) && regular.FontFamily.IsStyleAvailable(FontStyle.Italic)) style |= FontStyle.Italic;
                if (!regular.FontFamily.IsStyleAvailable(style)) style = FontStyle.Regular;
                return new Font(regular.FontFamily, size, style, GraphicsUnit.Pixel);
            }
        }
        internal static int ColorFor(TextCue cue, CaptionOptions options) { return options.UseDanmakuColor ? options.DanmakuUniformColor ? options.DanmakuTextColor : cue.Color : 0xFFFFFF; }

    }
    internal sealed class DanmakuPlacement
    { internal TextCue Cue; internal float X, Y, Size, Width; }
    internal static class DanmakuLayout
    {
        sealed class Lease { internal TextCue Cue; internal float Width, Velocity; internal double End; }
        internal static List<DanmakuPlacement> Frame(List<TextCue> timeline, double position, int width, int height, CaptionOptions options, Func<TextCue, float, float> measure, double playbackRate = 1)
        {
            List<DanmakuPlacement> result = new List<DanmakuPlacement>();
            if (!options.DanmakuEnabled || width <= 0 || height <= 0) return result;
            double duration = 8.0 * 100 / options.Speed * (options.DanmakuSpeedSync ? 1 : Math.Max(0.1, Math.Min(10, playbackRate)));
            float scale = options.ScaleWithScreen ? height / 1080f : 1;
            float lineHeight = Math.Max(22, 34 * scale * options.Size / 100f);
            int availableHeight = height * options.Region / 100;
            if (options.AvoidSubtitles) availableHeight = Math.Min(availableHeight, height - (int)(height * options.SubtitleBottom / 100f + options.SubtitleSize * height / 1080f * 4 + 16));
            int lanes = Math.Max(1, Math.Min(100, (int)(availableHeight / lineHeight)));
            // All types share lanes, so fixed comments also stay clear of scrolling text.
            Lease[] occupied = new Lease[lanes];
            int start = LowerBound(timeline, position - duration * 2);
            for (int i = start; i < timeline.Count && timeline[i].Start <= position; i++)
            {
                TextCue cue = timeline[i]; bool fixedCue = cue.Mode == 4 || cue.Mode == 5;
                if (fixedCue && !options.Fixed || !fixedCue && !options.Scrolling || cue.Color != 0xFFFFFF && !options.Colored || cue.Mode >= 7) continue;
                float size = Math.Max(12, cue.Size * scale * options.Size / 100f), textWidth = measure(cue, size), velocity = (width + textWidth) / (float)duration;
                double life = fixedCue ? Math.Min(5, duration) : duration;
                int span = Math.Max(1, (int)Math.Ceiling(size * 1.4f / lineHeight)), lane = -1;
                for (int candidate = 0; candidate + span <= lanes; candidate++)
                {
                    int n = cue.Mode == 4 ? lanes - candidate - span : candidate; bool free = true;
                    for (int row = n; row < n + span; row++)
                    {
                        Lease previous = occupied[row];
                        if (previous == null || previous.End <= cue.Start) continue;
                        float gap = (float)(cue.Start - previous.Cue.Start) * previous.Velocity - previous.Width - 12;
                        if (!fixedCue && previous.Cue.Mode == cue.Mode && gap >= 0 && (velocity <= previous.Velocity || (velocity - previous.Velocity) * (previous.End - cue.Start) < gap)) continue;
                        free = false; break;
                    }
                    if (free) { lane = n; break; }
                }
                if (lane < 0) continue;
                Lease lease = new Lease { Cue = cue, Width = textWidth, Velocity = velocity, End = cue.Start + life };
                for (int row = lane; row < lane + span; row++) occupied[row] = lease;
                if (position >= cue.Start + life || result.Count >= 72) continue;
                float x = fixedCue ? (width - textWidth) / 2 : cue.Mode == 6 ? -textWidth + (float)(position - cue.Start) * velocity : width - (float)(position - cue.Start) * velocity;
                float y = lane * lineHeight;
                result.Add(new DanmakuPlacement { Cue = cue, X = x, Y = y, Size = size, Width = textWidth });
            }
            return result;
        }
        internal static int LowerBound(List<TextCue> cues, double time)
        { int low = 0, high = cues.Count; while (low < high) { int middle = (low + high) / 2; if (cues[middle].Start < time) low = middle + 1; else high = middle; } return low; }
        internal static List<TextCue> Active(List<TextCue> cues, double position)
        {
            List<TextCue> active = new List<TextCue>();
            int end = LowerBound(cues, position + 0.00001);
            // Subtitle cues may be much longer than danmaku; don't assume a short lookback.
            for (int i = end - 1; i >= 0; i--) if (cues[i].End > position) { active.Add(cues[i]); if (active.Count == 4) break; }
            active.Reverse(); return active;
        }
    }
    internal sealed class TimedOverlay : LayeredSurface
    {
        readonly CaptionCatalog catalog;
        readonly List<TextCue> timeline = new List<TextCue>();
        int revision = -1;
        long nextFrame;
        long paintedTime = -1;
        int paintedRevision = -1;
        Bitmap bitmap;
        AssScene scene;
        internal IntPtr MenuWindow;
        internal TimedOverlay(CaptionCatalog catalog) : base("字幕和弹幕画面", true) { this.catalog = catalog; Cursor = Cursors.Default; }
        protected override CreateParams CreateParams
        { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x80020; return p; } }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) { message.Result = (IntPtr)(-1); return; }
            base.WndProc(ref message);
        }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        internal void Update(IntPtr player, long now)
        {
            if (now < nextFrame) return; nextFrame = now + 33;
            if (Native.IsIconic(player) || !Native.IsWindowVisible(player)) { Dismiss(); return; }
            IntPtr video = Native.VideoSurface(player); Rectangle bounds = PictureBounds(Native.ClientBounds(video),catalog.DisplayRatio);
            if (bounds.IsEmpty || !PlaceInPlayer(player, bounds)) { Dismiss(); return; }
            long time, status; if (!Native.Get(player, Native.GetPosition, out time) || !Native.Get(player, Native.GetPlayStatus, out status) || status == 0) { Dismiss(); return; }
            if (revision != catalog.Revision)
            {
                revision = catalog.Revision; timeline.Clear();
                foreach (TextSource source in catalog.Sources) if (source.Category == TextCategory.Danmaku && source.Enabled && !source.IntrinsicAdvanced && !source.AssRenderable) timeline.AddRange(source.Cues);
                TimedText.Sort(timeline);
            }
            TextSource main = catalog.Find(catalog.Slots.Main), secondary = catalog.Find(catalog.Slots.Secondary);
            if (!catalog.Options.SubtitlesEnabled) main = secondary = null;
            if ((main == null || main.Bitmap || main.Cues.Count == 0) && (secondary == null || secondary.Cues.Count == 0) && (!catalog.Options.DanmakuEnabled || timeline.Count == 0) && !catalog.Sources.Exists(s => AssScene.Enabled(s,catalog))) { Dismiss(); return; }
            // PotPlayer raises or recreates its video child when changing fullscreen/renderers.
            // Restore our sibling order even when a paused frame needs no repaint.
            RevealBelowMenus();
            if (Visible && bitmap != null && bitmap.Width == Width && bitmap.Height == Height && paintedTime == time && paintedRevision == catalog.Revision) return;
            if (bitmap == null || bitmap.Width != Width || bitmap.Height != Height) { if (bitmap != null) bitmap.Dispose(); bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppPArgb); }
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent); graphics.SmoothingMode = SmoothingMode.AntiAlias;
                double position = time / 1000.0;
                long nativeSpeed; Native.Get(player, Native.GetSpeed, out nativeSpeed);
                List<DanmakuPlacement> comments = DanmakuLayout.Frame(timeline, position, Width, Height, catalog.Options, delegate(TextCue cue, float size)
                { using (Font font = DanmakuSettings.FontFor(size, catalog.Options)) return graphics.MeasureString(cue.Text.Replace('\n', ' '), font).Width; }, nativeSpeed > 0 ? nativeSpeed / 1000.0 : 1);
                // libass composites all text into the same premultiplied surface.
                // The native renderer is called after releasing GDI's bitmap lock.
                pendingComments = comments;
            }
            if (scene == null) scene = new AssScene();
            scene.Render(bitmap,catalog,time,pendingComments);
            if (!Visible) Reveal(false); Present(Handle, bitmap); paintedTime = time; paintedRevision = catalog.Revision;
        }
        List<DanmakuPlacement> pendingComments;
        internal static Rectangle PictureBounds(Rectangle viewport,double ratio)
        {
            if(ratio<=0 || Double.IsNaN(ratio) || Double.IsInfinity(ratio)) return viewport;
            int width=viewport.Width, height=(int)Math.Round(width/ratio);
            if(height>viewport.Height) { height=viewport.Height; width=(int)Math.Round(height*ratio); }
            return new Rectangle(viewport.Left+(viewport.Width-width)/2,viewport.Top+(viewport.Height-height)/2,width,height);
        }
        void RevealBelowMenus()
        {
            Reveal(false);
            // Keep the text plane immediately above the video, below all visible interactive
            // surfaces. Raising it to HWND_TOP every frame otherwise covers paused menus.
            if (MenuWindow != IntPtr.Zero && Native.IsWindowVisible(MenuWindow)) Native.SetWindowPos(Handle, MenuWindow, 0, 0, 0, 0, 0x0013);
        }
        [StructLayout(LayoutKind.Sequential)] struct Size2 { internal int Width, Height; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { internal byte Op, Flags, Alpha, Format; }
        internal static void Present(IntPtr handle, Bitmap image)
        {
            IntPtr screen = GetDC(IntPtr.Zero), memory = CreateCompatibleDC(screen), bitmapHandle = image.GetHbitmap(Color.FromArgb(0)), previous = SelectObject(memory, bitmapHandle);
            try
            {
                Size2 size = new Size2 { Width = image.Width, Height = image.Height }; Native.Point origin = new Native.Point(); Blend blend = new Blend { Alpha = 255, Format = 1 };
                UpdateLayeredWindow(handle, screen, IntPtr.Zero, ref size, memory, ref origin, 0, ref blend, 2);
            }
            finally { SelectObject(memory, previous); DeleteObject(bitmapHandle); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
        }
        protected override void Dispose(bool disposing) { if (disposing) { if(bitmap != null) bitmap.Dispose(); if(scene != null) scene.Dispose(); } base.Dispose(disposing); }
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr window, IntPtr dst, IntPtr position, ref Size2 size, IntPtr src, ref Native.Point origin, uint key, ref Blend blend, uint flags);
    }
}
