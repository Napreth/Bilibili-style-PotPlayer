using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using BilibiliPotPlayer;

internal static class DanmakuFactoryChecks
{
    const string Payload="[0.1,0.3,\"1-1\",8,\"Mode 7 中文\",0,0,0.8,0.7,6000,500,true,\"Microsoft YaHei\",1]";
    static string Event(int mode,string text)
    { return "<d p='0,"+mode+",40,16777215'>"+System.Security.SecurityElement.Escape(text)+"</d>"; }
    static byte[] Pixels(Bitmap bitmap)
    {
        BitmapData data=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
        try { byte[] bytes=new byte[data.Stride*bitmap.Height]; Marshal.Copy(data.Scan0,bytes,0,bytes.Length); return bytes; } finally { bitmap.UnlockBits(data); }
    }
    static Rectangle Bounds(Bitmap bitmap)
    {
        int left=bitmap.Width,right=-1,top=bitmap.Height,bottom=-1;
        for(int y=0;y<bitmap.Height;y++) for(int x=0;x<bitmap.Width;x++) if(bitmap.GetPixel(x,y).A>5) {left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
        return right<0?Rectangle.Empty:Rectangle.FromLTRB(left,top,right+1,bottom+1);
    }
    internal static void Run(Action<bool,bool,string> equal)
    {
        Action<bool,string> check=(v,m)=>equal(true,v,m);
        string path=Environment.GetEnvironmentVariable("PATH"), temporary=Environment.GetEnvironmentVariable("TMP");
        string root=Path.Combine(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..")),".local","转换-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            Environment.SetEnvironmentVariable("PATH",""); Environment.SetEnvironmentVariable("TMP",root);
            check(DanmakuFactory.Abi==1,"bundled DanmakuFactory DLL loads without developer tools on PATH");
            string xml="<i>"+Event(1,"ordinary")+Event(7,Payload)+Event(8,"script must remain blocked")+Event(9,"BAS must remain blocked")+"</i>";
            string file=Path.Combine(root,"混合.xml"); File.WriteAllText(file,xml,new UTF8Encoding(false));
            var raw=TimedText.Read(file);
            check(raw.Last().Events[0].Mode==7 && raw.Last().Events[0].Size==40,"protocol classification retains Mode 7 source fields before conversion");
            byte[] before=File.ReadAllBytes(file);
            var sources=CaptionCatalog.ReadFile(file); var converted=sources.Single(s=>s.Converted); var blocked=sources.Single(s=>s.Category==TextCategory.Advanced && !s.Converted);
            check(sources.Count==3 && sources.Single(s=>s.Category==TextCategory.Danmaku).Cues.Count==1,"catalog keeps ordinary comments separate from converted and unsupported advanced tracks");
            check(converted.AssRenderable && CaptionCatalog.CanUse(converted) && converted.Category==TextCategory.Advanced && converted.Cues.Count==0,"Mode 7 becomes playable ASS while retaining its advanced category");
            check(converted.OriginalEvents.Count==1 && converted.OriginalEvents[0].RawContent==Payload,"converted source retains original Mode 7 payload for later upgrades");
            check(converted.AssHeader.Contains("PlayResX: 1920") && converted.Events.Exists(c=>c.AssFields.Contains("\\move")),"DanmakuFactory emits authored ASS coordinates and movement");
            check(blocked.Events.Select(c=>c.Mode).SequenceEqual(new[] {8,9}) && !CaptionCatalog.CanUse(blocked),"Mode 8 and Mode 9 remain blocked instead of entering the converter");
            check(before.SequenceEqual(File.ReadAllBytes(file)),"conversion never rewrites the source XML");
            check(!Directory.Exists(Path.Combine(root,"BilibiliPotPlayer")) || Directory.GetDirectories(Path.Combine(root,"BilibiliPotPlayer"),"danmaku-*").Length==0,"temporary conversion files are removed after a successful load");
            using(var catalog=new CaptionCatalog(IntPtr.Zero,null,new CaptionOptions(),null,delegate {})) using(var scene=new AssScene()) using(var bitmap=new Bitmap(480,270,PixelFormat.Format32bppPArgb)) {
                converted.Enabled=true; catalog.Sources.Add(converted); catalog.Revision++;
                scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>()); Rectangle early=Bounds(bitmap); byte[] paused=Pixels(bitmap);
                check(!early.IsEmpty,"converted Mode 7 produces actual libass glyph pixels");
                scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>()); check(paused.SequenceEqual(Pixels(bitmap)),"converted animation is stable while paused");
                scene.Render(bitmap,catalog,5000,new List<DanmakuPlacement>()); Rectangle late=Bounds(bitmap);
                check(late.Left>early.Left+100 && late.Top>early.Top+30,"converted Mode 7 moves in both axes on the media clock");
                scene.Render(bitmap,catalog,10000,new List<DanmakuPlacement>()); check(Bounds(bitmap).IsEmpty,"seeking beyond the converted event clears it");
                catalog.Options.AssEffectsEnabled=false; catalog.Revision++; scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>());
                check(Bounds(bitmap).IsEmpty,"advanced filter disables converted Mode 7 pixels");
                catalog.Options.AssEffectsEnabled=true; converted.Enabled=false; catalog.Revision++; scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>());
                check(Bounds(bitmap).IsEmpty,"converted source off switch clears its pixels");
            }
            var malformed=DanmakuFactory.Adapt(TimedText.Parse("<i>"+Event(7,Payload)+Event(7,"invalid JSON")+Event(9,"BAS")+"</i>","mixed.xml",".xml"));
            check(malformed.Count==2 && malformed[0].Converted && malformed[1].Events.Count==2 && malformed[1].Error.Length>0,"malformed Mode 7 cannot suppress a valid converted event");
            foreach(int mode in new[] {8,9}) {
                bool rejected=false; try { DanmakuFactory.ConvertCues(new[] {new TextCue {Mode=mode,Text="payload"}},"blocked"); } catch(InvalidDataException) {rejected=true;}
                check(rejected,"direct converter API rejects unsupported Mode "+mode);
            }
            var json=new JavaScriptSerializer();
            string doc=json.Serialize(new Dictionary<string,object>{{"danmaku",new object[] {new Dictionary<string,object>{{"time",0},{"mode",7},{"text",Payload}}}}});
            check(DanmakuFactory.Adapt(TimedText.Parse(doc,"danmaku.json",".json"))[0].Converted,"declared JSON Mode 7 is normalized through the same typed adapter");
            object[] content=(object[])json.DeserializeObject(Payload); content[4]="He said \"你好\" {\\p1} \\N\nsecond line";
            var literal=DanmakuFactory.ConvertCues(new[] {new TextCue {Mode=7,Text=json.Serialize(content)}},"literal");
            check(literal.Events[0].Text.Contains("He said \"你好\"") && literal.Events[0].AssFields.Contains("｛＼p1｝") && !literal.Events[0].AssFields.Contains("{\\p1}"),"quoted Unicode Mode 7 text remains literal and cannot inject ASS overrides");
            content[12]="Arial}\\p1{";
            var unsafeFont=DanmakuFactory.Adapt(TimedText.Parse("<i>"+Event(7,json.Serialize(content))+"</i>","font.xml",".xml"));
            check(!unsafeFont[0].AssRenderable && unsafeFont[0].Error.Length>0,"Mode 7 font names cannot escape into ASS control tags");
            var ordinary=DanmakuFactory.ConvertCues(new[] {1,4,5,6}.Select(m=>new TextCue {Mode=m,Text="ordinary "+m,Start=m}).ToList(),"ordinary");
            check(ordinary.Events.Count==4 && ordinary.AssHeader.Contains("[V4+ Styles]"),"native conversion adapter also supports standard scrolling and fixed comment types");
        } finally {
            Environment.SetEnvironmentVariable("PATH",path); Environment.SetEnvironmentVariable("TMP",temporary);
            Directory.Delete(root,true);
        }
    }
}
