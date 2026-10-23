using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace BilibiliPotPlayer
{
    internal sealed class AssFont
    {
        internal readonly string Name,Key;
        internal readonly byte[] Data;
        internal AssFont(string name,byte[] data) { Name=name; Data=data; using(var hash=System.Security.Cryptography.SHA256.Create()) Key=name+":"+BitConverter.ToString(hash.ComputeHash(data)); }
    }
    internal sealed class LibassRenderer : IDisposable
    {
        [DllImport("BilibiliAss.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr bili_ass_create();
        [DllImport("BilibiliAss.dll",CallingConvention=CallingConvention.Cdecl)] static extern void bili_ass_destroy(IntPtr context);
        [DllImport("BilibiliAss.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr bili_ass_read(IntPtr context,byte[] data,int bytes);
        [DllImport("BilibiliAss.dll",CallingConvention=CallingConvention.Cdecl)] static extern void bili_ass_free_track(IntPtr track);
        [DllImport("BilibiliAss.dll",CallingConvention=CallingConvention.Cdecl)] static extern int bili_ass_render(IntPtr context,IntPtr track,long time,int width,int height,int stride,IntPtr pixels,int opacity);
        [DllImport("BilibiliAss.dll",CallingConvention=CallingConvention.Cdecl)] static extern int bili_ass_version();
        [DllImport("BilibiliAss.dll",CallingConvention=CallingConvention.Cdecl)] static extern void bili_ass_add_font(IntPtr context,byte[] name,byte[] data,int length);
        internal static int Version { get { return bili_ass_version(); } }
        IntPtr context;
        readonly Dictionary<string,IntPtr> tracks = new Dictionary<string,IntPtr>();
        readonly HashSet<string> fonts = new HashSet<string>(), fontSections = new HashSet<string>();
        internal LibassRenderer() { context=bili_ass_create(); if(context==IntPtr.Zero) throw new InvalidOperationException("libass 初始化失败"); }
        internal void Clear()
        { foreach(IntPtr track in tracks.Values) bili_ass_free_track(track); tracks.Clear(); }
        internal void SetTrack(string key,string script)
        {
            IntPtr old; if(tracks.TryGetValue(key,out old)) { bili_ass_free_track(old); tracks.Remove(key); }
            int start=-1,end=script.Length;
            for(int row=0;row<script.Length;) {
                int next=script.IndexOf('\n',row), length=(next<0?script.Length:next)-row;
                string line=script.Substring(row,length).Trim();
                if(start<0 && line.Equals("[Fonts]",StringComparison.OrdinalIgnoreCase)) start=row;
                else if(start>=0 && AssEvents.IsSectionHeader(line)) { end=row; break; }
                if(next<0) break; row=next+1;
            }
            if(start>=0) {
                string section=script.Substring(start,end-start);
                if(!fontSections.Add(section)) script=script.Remove(start,end-start);
            }
            byte[] data=Encoding.UTF8.GetBytes(script);
            if(data.Length > TimedText.MaxBytes) throw new InvalidOperationException("ASS 内容超过大小限制");
            IntPtr track=bili_ass_read(context,data,data.Length);
            if(track==IntPtr.Zero) throw new InvalidOperationException("libass 无法读取字幕");
            tracks.Add(key,track);
        }
        internal void AddFonts(IEnumerable<AssFont> entries)
        { foreach(AssFont font in entries) if(fonts.Add(font.Key)) bili_ass_add_font(context,Encoding.UTF8.GetBytes(font.Name+"\0"),font.Data,font.Data.Length); }
        internal int Render(Bitmap target,string key,long time,int opacity=100)
        {
            IntPtr track; if(!tracks.TryGetValue(key,out track)) return 0;
            BitmapData data=target.LockBits(new Rectangle(0,0,target.Width,target.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppPArgb);
            try { return bili_ass_render(context,track,time,target.Width,target.Height,data.Stride,data.Scan0,opacity); }
            finally { target.UnlockBits(data); }
        }
        public void Dispose() { Clear(); if(context!=IntPtr.Zero) { bili_ass_destroy(context); context=IntPtr.Zero; } }
    }
}
