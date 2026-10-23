using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BilibiliPotPlayer;

internal static class CaptionClassificationChecks
{
    internal const string Header = "[Script Info]\nPlayResX: 1920\nPlayResY: 1080\n[V4+ Styles]\nFormat: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,ScaleX,ScaleY,Angle,Alignment,MarginL,MarginR,MarginV\nStyle: Default,Arial,40,&H00FFFFFF,&H000000FF,&H0,&H0,100,100,0,2,20,20,30\n[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text\n";
    static string Time(double t) { return String.Format(CultureInfo.InvariantCulture,"0:{0:00}:{1:00.00}",(int)(t/60),t%60); }
    static string Line(int i, string tags, double start, double end)
    { return "Dialogue: 0,"+Time(start)+","+Time(end)+",Default,,0,0,0,,{"+tags+"}line "+i+"\n"; }
    static List<TextSource> Ass(string tags, int n = 20)
    { StringBuilder s = new StringBuilder(Header); for (int i = 0; i < n; i++) s.Append(Line(i,tags,i*4,i*4+3)); return TimedText.Parse(s.ToString(),"sample.ass",".ass"); }
    internal static void Run(Action<bool,bool,string> equal)
    {
        Action<bool,string> check = (v,m) => equal(true,v,m);
        check(Ass(@"\move(1920,100,-300,100)")[0].Category == TextCategory.Danmaku,"sparse horizontal constant-speed ASS is ordinary danmaku without title hints");
        check(Ass(@"\move(-300,100,1920,100)")[0].Cues[0].Mode == 6,"reverse ASS scrolling maps to Mode 6");
        check(Ass(@"\move(1920,100,-300,400)")[0].IntrinsicAdvanced,"diagonal ASS motion is advanced");
        check(Ass(@"\move(1920,100,-300,100,500,2500)")[0].IntrinsicAdvanced,"part-time movement with stationary intervals is advanced");
        check(Ass(@"\move(1920,100,-300,100,0,0)")[0].Category == TextCategory.Danmaku,"zero move times mean the whole event");
        check(Ass(@"\pos(960,1000)\t(0,2000,\frz360)")[0].IntrinsicAdvanced,"effective rotation transform is advanced");
        check(Ass(@"\t(\fscx150)")[0].IntrinsicAdvanced,"effective scale transform is advanced");
        check(Ass(@"\t(\1c&H0000FF&)")[0].IntrinsicAdvanced,"effective color transform is advanced");
        check(Ass(@"\t(\alpha&HFF&)")[0].IntrinsicAdvanced,"effective alpha transform is advanced");
        check(Ass(@"\fad(200,100)")[0].IntrinsicAdvanced,"effective fade is advanced");
        check(Ass(@"\fade(255,0,255,0,100,2000,3000)")[0].IntrinsicAdvanced,"seven-parameter fade is advanced");
        check(Ass(@"\k100")[0].IntrinsicAdvanced,"karaoke color progression is advanced");
        check(Ass(@"\p1")[0].IntrinsicAdvanced,"static vector drawings are advanced rather than ordinary text");
        check(Ass(@"\t(bad,\frz360)")[0].Category == TextCategory.Uncertain,"malformed critical transform remains uncertain");
        check(Ass(@"\move(1,2,no,4)")[0].Category == TextCategory.Uncertain,"malformed motion never becomes ordinary scrolling");
        check(Ass(@"\t(\zzz100)")[0].Category == TextCategory.Uncertain,"unknown transform targets cannot be treated as an ineffective animation");
        check(Ass(@"\clip(bad)")[0].Category == TextCategory.Uncertain,"invalid clipping is retained as uncertain structure");
        check(Ass(@"\pos(1,2)\move(1,2,4,2)")[0].Category == TextCategory.Uncertain,"conflicting positioning tags remain uncertain");
        foreach (string tag in new[] { @"\fad(0,0)",@"\t(\frz0)",@"\fscx120\t(\fscx120)",@"\1c&HFFFFFF&\t(\1c&HFFFFFF&)",@"\k0",@"\bord3\1c&H0000FF&\pos(960,1000)" })
            check(Ass(tag)[0].Category == TextCategory.Subtitle,"static or ineffective ASS tag does not trigger advanced: "+tag);
        check(Ass(@"\pos(960,1000)")[0].Analysis.Bottom > .99,"ASS anchor and canvas normalize bottom-center position");
        check(Ass(@"\an8\pos(960,15)")[0].Category == TextCategory.Subtitle,"sequential top subtitles are not mistaken for fixed danmaku");
        check(Ass(@"\pos(960,1000)",3)[0].Category == TextCategory.Uncertain,"three unlabelled events do not establish a class");
        StringBuilder mix = new StringBuilder(Header);
        for (int i = 0; i < 20; i++) { mix.Append(Line(i,@"\pos(960,1000)",i*4,i*4+3)); mix.Append(Line(i,@"\move(1920,100,-300,100)",i*4,i*4+3)); }
        mix.Append(Line(0,@"\t(\frz360)",0,3));
        var tracks = TimedText.Parse(mix.ToString(),"mixed.ass",".ass");
        check(tracks.Count == 3 && tracks[0].Category == TextCategory.Subtitle && tracks[1].Category == TextCategory.Danmaku && tracks[2].IntrinsicAdvanced,"stable subtitle, scrolling and advanced subsets become three logical tracks");
        check(tracks.Sum(t => t.Events.Count) == 41 && tracks[2].Cues.Count == 0,"split preserves every raw event and prevents advanced playback");
        StringBuilder bilingual = new StringBuilder(Header);
        for (int i = 0; i < 30; i++) { bilingual.Append(Line(i,@"\pos(960,1000)",i*4,i*4+3)); bilingual.Append(Line(i,@"\an8\pos(960,20)",i*4,i*4+3)); }
        var bi = TimedText.Parse(bilingual.ToString(),"bilingual.ass",".ass");
        check(bi.Count == 1 && bi[0].Category == TextCategory.Subtitle && bi[0].Analysis.O2 == 1 && bi[0].Analysis.O3 == 0,"overlapping bilingual pairs remain subtitles; O3 distinguishes them from concurrency");
        var intervals = new List<TextCue> { new TextCue { Start=0,End=2 },new TextCue { Start=1,End=3 },new TextCue { Start=2,End=4 } };
        var a = CaptionClassifier.Analyze(intervals);
        check(Math.Abs(a.O2-.5)<.00001 && a.O3 == 0,"sweep treats touching intervals as half-open and integrates duration ratios");
        var inferred = new List<TextCue>(); for (int i=0;i<50;i++) inferred.Add(new TextCue { Start=i*10,End=i*10+8,DurationInferred=true });
        a = CaptionClassifier.Analyze(inferred);
        check(a.DurationReliability == 0 && a.O2 == 0 && CaptionClassifier.Decide(inferred,a) == TextCategory.Uncertain,"inferred renderer durations do not manufacture subtitle evidence");
        StringBuilder dialogue = new StringBuilder(), dense = new StringBuilder();
        for(int i=0;i<30;i++) { dialogue.Append(i+"\n"+Time(i*4).Replace('.',',')+" --> "+Time(i*4+3).Replace('.',',')+"\nDialogue\n\n"); dense.Append(i+"\n00:00:01,000 --> 00:00:06,000\nComment\n\n"); }
        check(TimedText.Parse(dialogue.ToString(),"fake.xml",".xml")[0].Category == TextCategory.Subtitle,"sequential SRT dialogue uses temporal evidence independently of suffix");
        check(TimedText.Parse(dense.ToString(),"dense.srt",".srt")[0].Category == TextCategory.Danmaku,"overlapping clustered SRT comments are not automatically subtitles");
        check(TimedText.Parse("<i><d p='1,6,25,1'>reverse</d></i>","x.xml",".xml")[0].Events[0].DurationInferred,"Bilibili renderer duration is marked inferred despite reliable protocol class");
        check(TimedText.Parse("[{\"time\":1,\"type\":\"animation\",\"text\":\"payload\"}]","x.json",".json")[0].IntrinsicAdvanced,"explicit JSON animation type enters advanced stage");
        check(TimedText.Parse("[{\"time\":1,\"motion\":{},\"text\":\"unknown\"}]","x.json",".json")[0].Category == TextCategory.Uncertain,"unparsed JSON motion is not guessed as ordinary");
        using (var catalog = new CaptionCatalog(IntPtr.Zero,null,new CaptionOptions(),null,delegate {})) {
            var source = new TextSource { Id="stream:0",Stream=-1,Title="Mixed",Category=TextCategory.Uncertain };
            catalog.Sources.Add(source); catalog.Classify(source,TextCategory.Danmaku); catalog.Toggle(source);
            catalog.ApplyContainer(source,tracks);
            check(source.Category == TextCategory.Danmaku && !source.Enabled && source.AutoCategory == TextCategory.Subtitle,"container completion retains manual category/off state and separate automatic diagnosis");
            check(catalog.Sources.Count == 3 && catalog.Sources[2].IntrinsicAdvanced,"container mixed tracks keep separate immutable advanced group");
        }
        var large = new List<TextCue>(); for(int i=0;i<100000;i++) large.Add(new TextCue { Start=i*.2,End=i*.2+4 });
        a = CaptionClassifier.Analyze(large);
        check(a.Samples == 100000 && a.O3 > .99 && a.B3 > .99,"full-size 100000-event scan handles concurrency and clustering");
    }
}
