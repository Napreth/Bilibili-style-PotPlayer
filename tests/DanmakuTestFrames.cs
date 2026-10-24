using System;
using System.Collections.Generic;
using BilibiliPotPlayer;

internal static class DanmakuTestFrames
{
    // Play a fresh session up to a sample time using the actual live admission path.
    internal static List<DanmakuPlacement> At(List<TextCue> cues,double time,int width,int height,CaptionOptions options,
        Func<TextCue,float,float> measure,double rate=1,PlaybackClock clock=null)
    {
        var playback=new DanmakuPlayback();
        Func<double,double> animation=t=>clock!=null?clock.AnimationTime(t):t/Math.Max(.1,rate);
        foreach(TextCue cue in cues) if(cue.Start<=time)
            playback.Frame(cues,cue.Start,width,height,options,(eventCue,size,appearance)=>measure(eventCue,size),animation(cue.Start),0,rate);
        return playback.Frame(cues,time,width,height,options,(cue,size,appearance)=>measure(cue,size),animation(time),0,rate);
    }
}
