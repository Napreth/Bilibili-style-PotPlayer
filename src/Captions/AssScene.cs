using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BilibiliPotPlayer
{
    internal sealed class AssScene : IDisposable
    {
        LibassRenderer renderer;
        string mediaPath;
        readonly List<string> keys = new List<string>();
        int revision = -1;
        internal static string Escape(string text)
        { return text.Replace("\\","＼").Replace("{","｛").Replace("}","｝").Replace("\r","").Replace("\n","\\N"); }
        static string N(double value) { return value.ToString("0.###",CultureInfo.InvariantCulture); }
        static string Time(double seconds)
        { long cs=(long)Math.Round(Math.Max(0,seconds)*100); return (cs/360000).ToString(CultureInfo.InvariantCulture)+":"+(cs/6000%60).ToString("00")+":"+(cs/100%60).ToString("00")+"."+(cs%100).ToString("00"); }
        static string Color(int rgb,int opacity)
        { int bgr=(rgb & 255)<<16 | rgb & 0xFF00 | (rgb>>16 & 255); return "&H"+((uint)(255-opacity*255/100)<<24 | (uint)bgr).ToString("X8",CultureInfo.InvariantCulture); }
        internal static string Header(int width,int height,CaptionOptions options)
        {
            return "[Script Info]\nScriptType: v4.00+\nPlayResX: "+width+"\nPlayResY: "+height+"\nScaledBorderAndShadow: yes\nWrapStyle: 0\n[V4+ Styles]\nFormat: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding\n"+
                Style("Subtitle",options.SubtitleFont,options.SubtitleSize*height/1080.0,options.SubtitleColor,options.SubtitleOpacity,options.SubtitleBold,options.SubtitleItalic,options.SubtitleOutline,options.SubtitleOutlineColor,options.SubtitleBackground,options.SubtitleBackgroundColor)+
                Style("Danmaku",options.DanmakuFont,25,options.DanmakuTextColor,options.Opacity,options.DanmakuBold,options.DanmakuItalic,options.DanmakuOutline,options.DanmakuOutlineColor,options.DanmakuBackground,options.DanmakuBackgroundColor)+
                "[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text\n";
        }
        static string Style(string name,string font,double size,int color,int opacity,bool bold,bool italic,int outline,int outlineColor,bool background,int backgroundColor)
        {
            double border=outline==3 || outline==2 ? 0 : Math.Max(1.5,size/(outline==0?8:12));
            return "Style: "+name+","+font+","+N(size)+","+Color(color,opacity)+","+Color(color,opacity)+","+Color(background?backgroundColor:outlineColor,opacity)+","+Color(background?backgroundColor:outlineColor,background?opacity*55/100:opacity)+","+(bold?-1:0)+","+(italic?-1:0)+",0,0,100,100,0,0,"+(background?3:1)+","+N(background?Math.Max(2,border):border)+","+N(outline==2?Math.Max(1,size/12):0)+",2,0,0,0,1\n";
        }
        internal static string Script(TextSource source,IEnumerable<TextCue> events)
        {
            StringBuilder script=new StringBuilder(source.AssHeader);
            foreach(TextCue cue in events.OrderBy(c=>c.AssOrder)) {
                if((cue.Effects & CueEffects.Protocol)!=0 || cue.AssFields.Length==0) continue;
                int comma=cue.AssFields.IndexOf(','); if(comma<0) continue;
                script.Append("Dialogue: ").Append(cue.AssFields.Substring(0,comma)).Append(',').Append(Time(cue.Start)).Append(',').Append(Time(cue.End)).Append(',').Append(cue.AssFields.Substring(comma+1)).Append('\n');
            }
            return script.ToString();
        }
        internal static bool Enabled(TextSource source,CaptionCatalog catalog)
        {
            if(source.Bitmap || !source.AssRenderable) return false;
            if(source.Category==TextCategory.Subtitle) return catalog.Options.SubtitlesEnabled && (source.Id==catalog.Slots.Main || source.Id==catalog.Slots.Secondary);
            if(source.Category==TextCategory.Danmaku) return source.Enabled && catalog.Options.DanmakuEnabled;
            return source.Category==TextCategory.Advanced && source.Enabled && catalog.Options.AssEffectsEnabled;
        }
        internal static bool Filter(TextCue cue,CaptionOptions options)
        { return (cue.Mode==4 || cue.Mode==5 ? options.Fixed : options.Scrolling) && (cue.Color==0xFFFFFF || options.Colored); }
        void Update(CaptionCatalog catalog)
        {
            if(revision==catalog.Revision) return;
            if(renderer==null || mediaPath!=catalog.MediaPath) { if(renderer!=null) renderer.Dispose(); renderer=new LibassRenderer(); mediaPath=catalog.MediaPath; }
            revision=catalog.Revision; renderer.Clear(); keys.Clear();
            var groups=catalog.Sources.Where(s=>Enabled(s,catalog)).GroupBy(s=>(s.Path??s.Title)+":"+s.Stream+":"+s.AssHeader);
            foreach(var group in groups) {
                foreach(TextSource source in group) renderer.AddFonts(source.Fonts);
                List<TextCue> events=new List<TextCue>();
                foreach(TextSource source in group) events.AddRange(source.Events.Where(c=>source.Category!=TextCategory.Danmaku || Filter(c,catalog.Options)));
                string key="source-"+keys.Count;
                try { renderer.SetTrack(key,Script(group.First(),events)); keys.Add(key); }
                catch(InvalidOperationException error) { catalog.Status=error.Message; }
            }
        }
        internal bool Render(Bitmap bitmap,CaptionCatalog catalog,long time,List<DanmakuPlacement> comments)
        {
            using(Graphics clear=Graphics.FromImage(bitmap)) clear.Clear(System.Drawing.Color.Transparent);
            Update(catalog);
            foreach(string key in keys) renderer.Render(bitmap,key,time);
            StringBuilder frame=new StringBuilder(Header(bitmap.Width,bitmap.Height,catalog.Options));
            foreach(DanmakuPlacement p in comments) {
                if(!catalog.Options.DanmakuEnabled || p.Cue.Mode>=7 || (p.Cue.Effects & CueEffects.Protocol)!=0) continue;
                frame.Append("Dialogue: 0,0:00:00.00,1:00:00.00,Danmaku,,0,0,0,,{\\an7\\pos(").Append(N(p.X)).Append(',').Append(N(p.Y)).Append(")\\fs").Append(N(p.Size)).Append("\\1c").Append(Color(DanmakuSettings.ColorFor(p.Cue,catalog.Options),catalog.Options.Opacity)).Append('}').Append(Escape(p.Cue.Text.Replace('\n',' '))).Append('\n');
            }
            TextSource main=catalog.Find(catalog.Slots.Main), second=catalog.Find(catalog.Slots.Secondary);
            double bottom=bitmap.Height*(1-catalog.Options.SubtitleBottom/100.0);
            if(catalog.Options.SubtitlesEnabled) { bottom=PlainCaption(frame,second,time/1000.0,bitmap.Width,bitmap.Height,bottom,.9,catalog.Options); PlainCaption(frame,main,time/1000.0,bitmap.Width,bitmap.Height,bottom,1,catalog.Options); }
            renderer.SetTrack("frame",frame.ToString()); renderer.Render(bitmap,"frame",0);
            return keys.Count>0 || comments.Count>0 || main!=null || second!=null;
        }
        static double PlainCaption(StringBuilder frame,TextSource source,double time,int width,int height,double bottom,double ratio,CaptionOptions options)
        {
            if(source==null || source.Bitmap || source.AssRenderable) return bottom;
            string text=String.Join("\n",DanmakuLayout.Active(source.Cues,time).Select(c=>c.Text)); if(text.Length==0) return bottom;
            double size=Math.Max(12,options.SubtitleSize*height/1080.0*ratio);
            int margin=(int)(width*.05);
            frame.Append("Dialogue: 1,0:00:00.00,1:00:00.00,Subtitle,,").Append(margin).Append(',').Append(margin).Append(",0,,{\\an2\\pos(").Append(width/2).Append(',').Append(N(bottom)).Append(")\\fs").Append(N(size)).Append('}').Append(Escape(text)).Append('\n');
            return bottom-size*1.5*text.Split('\n').Length-4;
        }
        public void Dispose() { if(renderer!=null) renderer.Dispose(); }
    }
}
