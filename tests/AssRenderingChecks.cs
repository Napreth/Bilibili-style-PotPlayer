using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using BilibiliPotPlayer;

internal static class AssRenderingChecks
{
    const string Head="[Script Info]\nScriptType: v4.00+\nPlayResX: 320\nPlayResY: 180\n[V4+ Styles]\nFormat: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding\nStyle: Default,Arial,25,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1\n[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text\n";
    static string Line(string tags,string text,int layer=0) { return "Dialogue: "+layer+",0:00:00.00,0:00:02.00,Default,,0,0,0,,{"+tags+"}"+text+"\n"; }
    static byte[] Pixels(Bitmap bitmap)
    {
        BitmapData data=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
        try { byte[] bytes=new byte[data.Stride*bitmap.Height]; Marshal.Copy(data.Scan0,bytes,0,bytes.Length); return bytes; } finally { bitmap.UnlockBits(data); }
    }
    static Rectangle Bounds(Bitmap bitmap)
    {
        int left=bitmap.Width,right=-1,top=bitmap.Height,bottom=-1;
        for(int y=0;y<bitmap.Height;y++) for(int x=0;x<bitmap.Width;x++) if(bitmap.GetPixel(x,y).A>5) { left=Math.Min(left,x); right=Math.Max(right,x); top=Math.Min(top,y); bottom=Math.Max(bottom,y); }
        return right<0?Rectangle.Empty:Rectangle.FromLTRB(left,top,right+1,bottom+1);
    }
    static void Clear(Bitmap bitmap) { using(Graphics g=Graphics.FromImage(bitmap)) g.Clear(Color.Transparent); }
    static string EncodeFont(byte[] data)
    {
        StringBuilder text=new StringBuilder(); int column=0;
        for(int i=0;i<data.Length;i+=3) {
            int count=Math.Min(3,data.Length-i), bits=data[i]<<16 | (count>1?data[i+1]<<8:0) | (count>2?data[i+2]:0);
            for(int j=0;j<=count;j++) { text.Append((char)(((bits>>(18-j*6))&63)+33)); if(++column==80) { text.Append('\n'); column=0; } }
        }
        return text+"\n";
    }
    internal static void Run(Action<bool,bool,string> equal)
    {
        Action<bool,string> check=(v,m)=>equal(true,v,m);
        string oldPath=Environment.GetEnvironmentVariable("PATH");
        try {
            Environment.SetEnvironmentVariable("PATH","");
            check(LibassRenderer.Version>=0x01705000,"bundled libass loads without developer DLLs on PATH");
            using(var renderer=new LibassRenderer()) using(var bitmap=new Bitmap(320,180,PixelFormat.Format32bppPArgb)) {
                var attached=NativeTimedText.Read(System.IO.Path.Combine(System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..")),"tests","fixtures","ass-font.mkv"),0,"Fonts");
                check(attached[0].Fonts.Count==1 && attached[0].Fonts[0].Name.EndsWith(".ttf"),"font attachments are retained with container ASS metadata");
                string fontScript=Head.Replace(",Arial,",",AssFixtureFont,")+Line(@"\pos(20,30)","A");
                renderer.SetTrack("v",fontScript); renderer.Render(bitmap,"v",1000); byte[] fallback=Pixels(bitmap);
                renderer.AddFonts(attached[0].Fonts); renderer.SetTrack("v",fontScript); Clear(bitmap); renderer.Render(bitmap,"v",1000);
                check(!fallback.SequenceEqual(Pixels(bitmap)),"libass uses the original embedded test glyph instead of system fallback");
                string fontPath=System.IO.Path.Combine(System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..")),"tests","fixtures","ass-fixture.ttf");
                byte[] embedded=System.IO.File.ReadAllBytes(fontPath); int boundary=(embedded.Length+59)/60*60; Array.Resize(ref embedded,boundary+120); embedded[boundary]=0xE8;
                string encoded=EncodeFont(embedded);
                var inline=TimedText.Parse(fontScript+"[Fonts]\nfontname: ass-fixture.ttf\n"+encoded,"inline.ass",".ass")[0];
                check(encoded.Contains("\n[") && inline.AssHeader.Replace("\r","").Contains(encoded),"encoded font rows beginning with brackets survive ASS section parsing intact");
                using(var inlineRenderer=new LibassRenderer()) {
                    inlineRenderer.SetTrack("font",Head.Replace(",Arial,",",AssFixtureFont,")+Line(@"\pos(20,30)","A")); Clear(bitmap); inlineRenderer.Render(bitmap,"font",1000); byte[] withoutFont=Pixels(bitmap);
                    inlineRenderer.SetTrack("font",AssScene.Script(inline,inline.Events)); Clear(bitmap); inlineRenderer.Render(bitmap,"font",1000);
                    check(inline.AssHeader.Contains("[Fonts]") && !withoutFont.SequenceEqual(Pixels(bitmap)),"ASS font sections after Events survive parsing and replace a cached fallback");
                }
                Clear(bitmap);
                renderer.SetTrack("v",Head+Line(@"\p1\pos(20,30)\1c&H0000FF&","m 0 0 l 40 0 40 20 0 20"));
                check(renderer.Render(bitmap,"v",1000)>0 && bitmap.GetPixel(25,35).R>200 && bitmap.GetPixel(25,35).G<20,"native libass rasterizes a red ASS vector at its authored position");
                var ordered=TimedText.Parse(Head+Line(@"\p1\pos(20,30)\1c&H0000FF&","m 0 0 l 40 0 40 20 0 20",2)+Line(@"\p1\pos(20,30)\1c&H00FF00&","m 0 0 l 40 0 40 20 0 20",1),"layers.ass",".ass")[0];
                renderer.SetTrack("layers",AssScene.Script(ordered,ordered.Events.AsEnumerable().Reverse())); Clear(bitmap); renderer.Render(bitmap,"layers",1000);
                check(bitmap.GetPixel(25,35).R>200 && bitmap.GetPixel(25,35).G<20,"ASS Layer overrides source event order during composition");
                ordered=TimedText.Parse(Head+Line(@"\p1\pos(20,30)\1c&H0000FF&","m 0 0 l 40 0 40 20 0 20")+Line(@"\p1\pos(20,30)\1c&H00FF00&","m 0 0 l 40 0 40 20 0 20"),"order.ass",".ass")[0];
                renderer.SetTrack("layers",AssScene.Script(ordered,ordered.Events.AsEnumerable().Reverse())); Clear(bitmap); renderer.Render(bitmap,"layers",1000);
                check(bitmap.GetPixel(25,35).G>200 && bitmap.GetPixel(25,35).R<20,"same-Layer events retain their original ASS order after source grouping");
                Clear(bitmap); renderer.Render(bitmap,"v",1000);
                byte[] first=Pixels(bitmap); Clear(bitmap); renderer.Render(bitmap,"v",1000);
                check(first.SequenceEqual(Pixels(bitmap)),"paused timestamps render identical pixels");
                Clear(bitmap); renderer.Render(bitmap,"v",2500); check(Bounds(bitmap).IsEmpty,"seeking beyond an ASS event removes every pixel");
                renderer.SetTrack("v",Head+Line(@"\p1\move(20,30,120,80)","m 0 0 l 40 0 40 20 0 20"));
                Clear(bitmap); renderer.Render(bitmap,"v",200); Rectangle early=Bounds(bitmap);
                Clear(bitmap); renderer.Render(bitmap,"v",1500); Rectangle late=Bounds(bitmap);
                check(late.Left>early.Left+30 && late.Top>early.Top+20,"diagonal ASS animation moves in both axes");
                renderer.SetTrack("v",Head+Line(@"\p1\pos(100,100)\t(0,2000,\frz90\fscx200)","m 0 0 l 40 0 40 10 0 10"));
                Clear(bitmap); renderer.Render(bitmap,"v",100); first=Pixels(bitmap); Clear(bitmap); renderer.Render(bitmap,"v",1500);
                check(!first.SequenceEqual(Pixels(bitmap)),"rotation and scale transforms produce animated geometry");
                renderer.SetTrack("v",Head+Line(@"\p1\pos(20,30)\fad(1000,0)\1c&H0000FF&","m 0 0 l 40 0 40 20 0 20"));
                Clear(bitmap); renderer.Render(bitmap,"v",200); int faint=bitmap.GetPixel(25,35).A;
                byte[] partial=Pixels(bitmap); bool premultiplied=true;
                for(int i=0;i<partial.Length;i+=4) if(partial[i]>partial[i+3] || partial[i+1]>partial[i+3] || partial[i+2]>partial[i+3]) premultiplied=false;
                Clear(bitmap); renderer.Render(bitmap,"v",1200); check(faint>0 && bitmap.GetPixel(25,35).A>faint*3 && premultiplied,"fades compose premultiplied BGRA without dark-edge alpha errors");
                renderer.SetTrack("v",Head+Line(@"\p1\pos(20,30)\clip(0,0,40,180)","m 0 0 l 80 0 80 20 0 20"));
                Clear(bitmap); renderer.Render(bitmap,"v",1000); check(Bounds(bitmap).Right<=40 && !Bounds(bitmap).IsEmpty,"ASS clipping is applied to rasterized geometry");
                renderer.SetTrack("v",Head+Line(@"\pos(20,50)\kf100","ABC{\\kf100}DEF"));
                Clear(bitmap); renderer.Render(bitmap,"v",200); first=Pixels(bitmap); Clear(bitmap); renderer.Render(bitmap,"v",1600);
                check(!first.SequenceEqual(Pixels(bitmap)),"karaoke timing changes the actual glyph colors");
                renderer.SetTrack("v",Head+Line(@"\pos(10,50)","你好 العربية abc"));
                Clear(bitmap); renderer.Render(bitmap,"v",1000); check(!Bounds(bitmap).IsEmpty,"Unicode text passes through native font selection and shaping");
                string ssa="[Script Info]\nScriptType: v4.00\nPlayResX: 320\nPlayResY: 180\n[V4 Styles]\nFormat: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,TertiaryColour,BackColour,Bold,Italic,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,AlphaLevel,Encoding\nStyle: Default,Arial,25,16777215,255,0,0,0,0,1,1,0,2,10,10,10,0,1\n[Events]\nFormat: Marked,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text\nDialogue: Marked=0,0:00:00.00,0:00:02.00,Default,,0,0,0,,SSA subtitle\n";
                var legacy=TimedText.Parse(ssa,"legacy.ssa",".ssa")[0]; renderer.SetTrack("v",AssScene.Script(legacy,legacy.Events)); Clear(bitmap); renderer.Render(bitmap,"v",1000);
                check(!Bounds(bitmap).IsEmpty && Bounds(bitmap).Top>100,"SSA legacy style headers render at their authored alignment");
            }
            var parsed=TimedText.Parse(Head+Line(@"\p1\pos(20,30)","m 0 0 l 40 0 40 20 0 20"),"effect.ass",".ass")[0];
            check(parsed.AssRenderable && CaptionCatalog.CanUse(parsed) && parsed.Cues.Count==0,"standard ASS effects remain classified advanced but are libass-playable");
            foreach(int mode in new[] {7,8,9}) {
                TextSource source=TimedText.Parse("<i><d p='1,"+mode+",25,1'>payload</d></i>","x.xml",".xml")[0];
                check(!source.AssRenderable && !CaptionCatalog.CanUse(source),"Bilibili special protocol remains blocked: Mode "+mode);
            }
            using(var catalog=new CaptionCatalog(IntPtr.Zero,null,new CaptionOptions(),null,delegate {})) using(var scene=new AssScene()) using(var bitmap=new Bitmap(320,180,PixelFormat.Format32bppPArgb)) {
                parsed.Id="effect"; parsed.Path="same.ass"; parsed.Enabled=true; catalog.Sources.Add(parsed); catalog.Revision++;
                scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>()); check(!Bounds(bitmap).IsEmpty,"advanced ASS source renders in the player scene");
                catalog.Options.AssEffectsEnabled=false; catalog.Revision++; scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>());
                check(Bounds(bitmap).IsEmpty,"advanced filter clears the scene without enabling any Bilibili protocol");
                catalog.Options.AssEffectsEnabled=true; parsed.Enabled=false; catalog.Revision++; scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>());
                check(Bounds(bitmap).IsEmpty,"per-source advanced off switch removes its pixels");
                catalog.Options.Region=100; catalog.Options.AvoidSubtitles=false;
                var comment=new TextCue { Text=@"literal {\p1} and \N [Fonts] text",Mode=1 };
                var comments=new List<DanmakuPlacement> { new DanmakuPlacement { Cue=comment,X=10,Y=30,Size=25,Width=250 } };
                scene.Render(bitmap,catalog,1000,comments); check(!Bounds(bitmap).IsEmpty && !AssScene.Escape(comment.Text).Contains("\\p1"),"ordinary comments render as literal text through libass");
                byte[] literal=Pixels(bitmap); scene.Render(bitmap,catalog,1000,comments); check(literal.SequenceEqual(Pixels(bitmap)),"ordinary bracketed words are never treated as ASS font sections on subsequent frames");
                catalog.Options.DanmakuEnabled=false; scene.Render(bitmap,catalog,1000,comments); check(Bounds(bitmap).IsEmpty,"ordinary danmaku master switch clears rendered text");
                catalog.Options.DanmakuEnabled=true; comment.Mode=8; scene.Render(bitmap,catalog,1000,comments); check(Bounds(bitmap).IsEmpty,"raster pipeline rejects accidentally misrouted script modes");
                var sub=new TextSource { Id="sub",Category=TextCategory.Subtitle,Loaded=true }; sub.Cues.Add(new TextCue { Start=0,End=2,Text="Subtitle" }); catalog.Sources.Add(sub); catalog.Slots.Main="sub"; catalog.Options.SubtitleSize=64;
                scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>()); check(!Bounds(bitmap).IsEmpty,"plain subtitles use the same native renderer");
                catalog.Options.SubtitlesEnabled=false; scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>()); check(Bounds(bitmap).IsEmpty,"subtitle master switch clears plain subtitle pixels");
            }
            check(TimedOverlay.PictureBounds(new Rectangle(0,0,1000,800),16.0/9)==new Rectangle(0,119,1000,562),"ASS coordinates are fitted to the video picture inside letterbox bars");
        } finally { Environment.SetEnvironmentVariable("PATH",oldPath); }
    }
}
