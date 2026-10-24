using System;
using System.Collections.Generic;

namespace BilibiliPotPlayer
{
    // Admission and appearance belong to a live comment, not to a reconstructed
    // historical frame. Rejected or newly enabled past events are never backfilled.
    internal sealed class DanmakuPlayback
    {
        readonly List<DanmakuPlacement> active = new List<DanmakuPlacement>();
        bool initialized;
        double previous;
        int epoch;
        internal void Reset() { active.Clear(); initialized = false; }
        static bool Fixed(TextCue cue) { return cue.Mode == 4 || cue.Mode == 5; }
        static double Age(DanmakuPlacement p, double media, double animation)
        { return (p.Appearance.DanmakuSpeedSync ? media : animation) - p.Start; }
        static void Position(DanmakuPlacement p, double media, double animation)
        { p.X = p.FromX + (p.ToX - p.FromX) * (float)Math.Max(0, Math.Min(1, Age(p, media, animation) / p.Life)); }
        static void Geometry(DanmakuPlacement p, int width, int height, Func<TextCue,float,CaptionOptions,float> measure)
        {
            if (p.CanvasWidth != width || p.CanvasHeight != height)
            {
                if (p.Appearance.ScaleWithScreen) p.Size *= height / (float)p.CanvasHeight;
                p.Width = measure(p.Cue, p.Size, p.Appearance);
                p.Y *= height / (float)p.CanvasHeight;
                p.ClipHeight = (int)Math.Round(p.ClipHeight * height / (double)p.CanvasHeight);
                p.CanvasWidth = width; p.CanvasHeight = height;
            }
            p.FromX = Fixed(p.Cue) ? (width - p.Width) / 2 : p.Cue.Mode == 6 ? -p.Width : width;
            p.ToX = Fixed(p.Cue) ? p.FromX : p.Cue.Mode == 6 ? width : -p.Width;
        }
        bool Free(DanmakuPlacement next, double media, double animation, double rate)
        {
            foreach (DanmakuPlacement old in active)
            {
                if (next.Y >= old.Y + old.Size * 1.4f || old.Y >= next.Y + next.Size * 1.4f) continue;
                if (Fixed(next.Cue) || Fixed(old.Cue) || next.Cue.Mode != old.Cue.Mode) return false;
                double gap = next.Cue.Mode == 6 ? old.X - 12 : next.CanvasWidth - old.X - old.Width - 12;
                if (gap < 0) return false;
                double oldRate = old.Appearance.DanmakuSpeedSync ? rate : 1, newRate = next.Appearance.DanmakuSpeedSync ? rate : 1;
                double oldVelocity = (old.CanvasWidth + old.Width) / old.Life * oldRate;
                double newVelocity = (next.CanvasWidth + next.Width) / next.Life * newRate;
                double remaining = (old.Life - Age(old, media, animation)) / oldRate;
                if ((newVelocity - oldVelocity) * remaining >= gap) return false;
            }
            return true;
        }
        internal List<DanmakuPlacement> Frame(List<TextCue> timeline, double media, int width, int height, CaptionOptions options,
            Func<TextCue,float,CaptionOptions,float> measure, double animation, int clockEpoch = 0, double rate = 1)
        {
            if (width <= 0 || height <= 0) return new List<DanmakuPlacement>();
            bool first = !initialized || epoch != clockEpoch;
            if (first) { active.Clear(); previous = media; initialized = true; epoch = clockEpoch; }
            for (int i = active.Count - 1; i >= 0; i--)
            {
                DanmakuPlacement p = active[i];
                if (!options.DanmakuEnabled || Age(p, media, animation) >= p.Life) { active.RemoveAt(i); continue; }
                Geometry(p, width, height, measure); Position(p, media, animation);
            }
            int start = DanmakuLayout.LowerBound(timeline, first ? media : previous + .0000001);
            if (options.DanmakuEnabled)
            {
                CaptionOptions appearance = null;
                for (int i = start; i < timeline.Count && timeline[i].Start <= media; i++)
                {
                    TextCue cue = timeline[i];
                    if (!AssScene.Filter(cue, options) || cue.Mode >= 7 || (cue.Effects & (CueEffects.Animation | CueEffects.Drawing | CueEffects.Protocol)) != 0 || active.Count >= 72) continue;
                    int area = DanmakuLayout.AreaHeight(height, options); if (area <= 0) continue;
                    if (appearance == null) appearance = options.Snapshot();
                    float scale = appearance.ScaleWithScreen ? height / (float)Math.Max(1, cue.CanvasHeight) : 1;
                    float size = Math.Max(12, Math.Min(512, cue.Size * scale * appearance.Size / 100f));
                    double life = 8.0 * 100 / appearance.Speed; if (Fixed(cue)) life = Math.Min(5, life);
                    DanmakuPlacement p = new DanmakuPlacement { Cue=cue, Appearance=appearance, Size=size, Width=measure(cue,size,appearance),
                        CanvasWidth=width, CanvasHeight=height, ClipHeight=area, Start=appearance.DanmakuSpeedSync?media:animation, Life=life, Timed=true };
                    Geometry(p, width, height, measure); Position(p, media, animation);
                    float line = Math.Max(22, 34 * (appearance.ScaleWithScreen ? height / 1080f : 1) * appearance.Size / 100f);
                    int rows = Math.Max(1, Math.Min(100, (int)Math.Ceiling(area / line)));
                    for (int row = 0; row < rows; row++)
                    {
                        p.Y = Fixed(cue) && cue.Mode == 4 ? Math.Max(0, area - size * 1.4f - row * line) : row * line;
                        if (p.Y > 0 && p.Y + size * 1.4f > area) continue;
                        if (!Free(p, media, animation, rate)) continue;
                        active.Add(p); break;
                    }
                }
            }
            previous = Math.Max(previous, media);
            return new List<DanmakuPlacement>(active);
        }
    }
}
