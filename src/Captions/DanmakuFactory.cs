using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace BilibiliPotPlayer
{
    internal static class DanmakuFactory
    {
        [DllImport("BilibiliDanmakuFactory.dll",CallingConvention=CallingConvention.Cdecl)] static extern int bili_danmaku_abi();
        [DllImport("BilibiliDanmakuFactory.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr bili_danmaku_create();
        [DllImport("BilibiliDanmakuFactory.dll",CallingConvention=CallingConvention.Cdecl)] static extern void bili_danmaku_destroy(IntPtr context);
        [DllImport("BilibiliDanmakuFactory.dll",CallingConvention=CallingConvention.Cdecl)] static extern int bili_danmaku_add(IntPtr context,int time,int mode,int size,int color,byte[] text,double[] special,byte[] font);
        [DllImport("BilibiliDanmakuFactory.dll",CallingConvention=CallingConvention.Cdecl)] static extern int bili_danmaku_write(IntPtr context,byte[] output);
        sealed class Prepared { internal TextCue Cue; internal string Text,Font=""; internal double[] Special; }
        static readonly object sync=new object();
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        internal static int Abi { get { return bili_danmaku_abi(); } }
        static double Number(object value,double min,double max)
        {
            double n;
            if(!Double.TryParse(Convert.ToString(value,Inv),NumberStyles.Float,Inv,out n) || Double.IsNaN(n) || Double.IsInfinity(n) || n<min || n>max) throw new InvalidDataException("Mode 7 参数超出转换范围");
            return n;
        }
        static Prepared Prepare(TextCue cue)
        {
            if(cue.Mode>=1 && cue.Mode<=6) {
                if(Encoding.UTF8.GetByteCount(cue.Text)>900 || cue.Text.IndexOf('\0')>=0) throw new InvalidDataException("弹幕文字超过转换限制");
                return new Prepared {Cue=cue,Text=cue.Text};
            }
            if(cue.Mode!=7) throw new InvalidDataException("Mode 8 / 9 暂不支持转换");
            string raw=cue.Origin.StartsWith("Bilibili XML",StringComparison.Ordinal) ? cue.RawContent : cue.Text;
            if(raw.Length>8192) throw new InvalidDataException("Mode 7 内容超过转换限制");
            var json=new JavaScriptSerializer {MaxJsonLength=8192,RecursionLimit=16};
            object[] p=json.DeserializeObject(raw) as object[];
            if(p==null || p.Length<5 || !(p[4] is string)) throw new InvalidDataException("Mode 7 数据不是有效的参数数组");
            double x=Number(p[0],-100000,100000), y=Number(p[1],-100000,100000), life=Number(p[3],.01,3600);
            string[] alpha=Convert.ToString(p[2],Inv).Split('-');
            if(alpha.Length<1 || alpha.Length>2) throw new InvalidDataException("Mode 7 透明度格式无效");
            string font=p.Length>12 && p[12] is string ? (string)p[12] : "Microsoft YaHei";
            if(font.IndexOfAny(new[] {'\0','\r','\n','"',',','{','}','\\'})>=0 || Encoding.UTF8.GetByteCount(font)>120) throw new InvalidDataException("Mode 7 字体名称无效");
            double[] special={x,y,Number(alpha[0],0,1),Number(alpha[alpha.Length-1],0,1),life*1000,p.Length>5?Number(p[5],-36000,36000):0,p.Length>6?Number(p[6],-36000,36000):0,
                p.Length>7?Number(p[7],-100000,100000):x,p.Length>8?Number(p[8],-100000,100000):y,
                p.Length>9?Number(p[9],0,life*1000):life*1000,p.Length>10?Number(p[10],0,life*1000):0};
            string text=(string)p[4];
            if(text.IndexOf('\0')>=0) throw new InvalidDataException("Mode 7 文字无效");
            text=AssScene.Escape(text.Replace("/n","\n"));
            if(Encoding.UTF8.GetByteCount(text)>1000) throw new InvalidDataException("Mode 7 文字超过转换限制");
            return new Prepared {Cue=cue,Text=text,Font=font,Special=special};
        }
        internal static TextSource ConvertCues(IList<TextCue> cues,string title)
        {
            if(cues.Count==0 || cues.Count>TimedText.MaxCues) throw new InvalidDataException("没有可转换的弹幕或条目过多");
            List<Prepared> prepared=new List<Prepared>(); long bytes=0;
            foreach(TextCue cue in cues) { Number(cue.Start,0,604800); Prepared item=Prepare(cue); prepared.Add(item); bytes+=Encoding.UTF8.GetByteCount(item.Text); }
            if(bytes>TimedText.MaxBytes) throw new InvalidDataException("弹幕转换内容超过大小限制");
            string folder=Path.Combine(Path.GetTempPath(),"BilibiliPotPlayer","danmaku-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try {
                string output=Path.Combine(folder,"output.ass"); int result=0;
                lock(sync) {
                    IntPtr context=bili_danmaku_create(); if(context==IntPtr.Zero) throw new InvalidOperationException("DanmakuFactory 初始化失败");
                    try {
                        foreach(Prepared item in prepared) {
                            TextCue cue=item.Cue;
                            result=bili_danmaku_add(context,(int)Math.Round(cue.Start*1000),cue.Mode,Math.Max(12,Math.Min(64,cue.Size)),cue.Color&0xFFFFFF,Encoding.UTF8.GetBytes(item.Text+"\0"),item.Special,Encoding.UTF8.GetBytes(item.Font+"\0"));
                            if(result!=0) break;
                        }
                        if(result==0) result=bili_danmaku_write(context,Encoding.UTF8.GetBytes(output+"\0"));
                    } finally { bili_danmaku_destroy(context); }
                }
                if(result!=0) throw new InvalidDataException("DanmakuFactory 转换失败（"+result+"）");
                if(!File.Exists(output) || new FileInfo(output).Length>TimedText.MaxBytes) throw new InvalidDataException("转换后的 ASS 超过大小限制");
                TextSource source=new TextSource {Title=title,Format="DanmakuFactory / ASS",Loaded=true,Converted=true};
                AssEvents.Parse(File.ReadAllText(output,Encoding.UTF8).TrimStart('\uFEFF'),source);
                if(source.Events.Count==0) throw new InvalidDataException("DanmakuFactory 没有生成可播放的 ASS 事件");
                source.Format="DanmakuFactory / ASS"; source.OriginalEvents.AddRange(cues);
                source.Category=source.AutoCategory=TextCategory.Advanced; source.IntrinsicAdvanced=true; source.Cues.Clear();
                source.Analysis=CaptionClassifier.Analyze(new List<TextCue>(cues)); source.Analysis.Confidence=1; source.Analysis.Reason="DanmakuFactory 转换的高级弹幕";
                source.Detail=cues.Count+" 条高级弹幕 · DanmakuFactory";
                return source;
            } finally { try { Directory.Delete(folder,true); } catch(IOException) { } catch(UnauthorizedAccessException) { } }
        }
        // Conversion runs once in the catalog's loading worker. Keep unsupported
        // and failed events separate, so one bad event cannot disable valid Mode 7.
        internal static List<TextSource> Adapt(List<TextSource> sources)
        {
            List<TextSource> result=new List<TextSource>();
            foreach(TextSource original in sources) {
                if(original.AssRenderable || original.Category!=TextCategory.Advanced || !original.Events.Exists(c=>c.Mode==7)) { result.Add(original); continue; }
                List<TextCue> supported=new List<TextCue>(), blocked=new List<TextCue>(); string error="";
                foreach(TextCue cue in original.Events) {
                    if(cue.Mode!=7) { blocked.Add(cue); continue; }
                    try { Prepare(cue); supported.Add(cue); }
                    catch(Exception ex) { if(!(ex is ArgumentException || ex is InvalidOperationException || ex is InvalidDataException)) throw; blocked.Add(cue); error=ex.Message; }
                }
                if(supported.Count>0) {
                    try { result.Add(ConvertCues(supported,original.Title)); }
                    catch(Exception ex) { if(!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is BadImageFormatException)) throw; blocked.AddRange(supported); error=ex.Message; }
                }
                if(blocked.Count>0) {
                    TextSource unavailable=new TextSource {Title=original.Title,Format=original.Format,Category=TextCategory.Advanced,AutoCategory=TextCategory.Advanced,IntrinsicAdvanced=true,ProtocolAdvanced=true,Loaded=true,Error=error};
                    unavailable.Events.AddRange(blocked); unavailable.Analysis=CaptionClassifier.Analyze(blocked); unavailable.Analysis.Confidence=1;
                    unavailable.Detail=blocked.Count+" 条 · 暂不播放"; result.Add(unavailable);
                }
            }
            return result;
        }
    }
}
