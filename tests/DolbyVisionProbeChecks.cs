using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using BilibiliPotPlayer;

internal static class DolbyVisionProbeChecks
{
    sealed class Track
    {
        internal uint Id;
        internal string Codec = "hvc1";
        internal byte[][] Samples;
        internal int LengthSize = 4;
        internal bool WideOffsets, FixedSize;
    }
    internal static void Run(Action<bool, bool, string> equal)
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
        byte[] rpu = File.ReadAllBytes(Path.Combine(root, "tests", "fixtures", "dolby-vision-rpu.bin"));
        equal(true, DolbyVisionProbe.IsRpu(rpu), "real frame RPU validates with its checksum");
        byte[] corrupt = (byte[])rpu.Clone(); corrupt[30] ^= 1;
        equal(false, DolbyVisionProbe.IsRpu(corrupt), "corrupt RPU is not Dolby Vision");
        byte[] otherNal = (byte[])rpu.Clone(); otherNal[0] = 0x50;
        equal(false, DolbyVisionProbe.IsRpu(otherNal), "valid payload in another NAL type is ignored");
        equal(false, DolbyVisionProbe.IsRpu(new byte[] { 0x7C, 1, 25, 8, 9 }), "truncated RPU cannot relabel HLG");
        byte[] plain = new byte[rpu.Length]; plain[0] = 0x28; plain[1] = 1;
        equal(true, Probe(new Track { Id = 7, Samples = new[] { rpu } }).Contains(7), "missing container DV config falls back to frame RPU");
        equal(false, Probe(new Track { Id = 7, Samples = new[] { plain } }).Contains(7), "ordinary HEVC is not inferred as Dolby Vision");
        equal(false, Probe(new Track { Id = 7, Samples = new[] { corrupt } }).Contains(7), "MP4 fallback rejects corrupt metadata");
        HashSet<long> tracks = Probe(new Track { Id = 1, Samples = new[] { plain } }, new Track { Id = 2, Samples = new[] { rpu } });
        equal(false, tracks.Contains(1), "second track RPU does not relabel first video track");
        equal(true, tracks.Contains(2), "RPU belongs to exact MP4 track ID");
        equal(true, Probe(new Track { Id = 3, Samples = new[] { plain, rpu }, LengthSize = 2, WideOffsets = true }).Contains(3), "later chunk, two-byte NAL lengths and 64-bit offsets");
        equal(true, Probe(new Track { Id = 4, Samples = new[] { plain, rpu }, FixedSize = true }).Contains(4), "fixed-size sample table with RPU in later sample");
        equal(false, Probe(new Track { Id = 5, Samples = new[] { rpu }, Codec = "avc1" }).Contains(5), "AVC does not use HEVC RPU detection");
        using (MemoryStream malformed = new MemoryStream(Join(U32(UInt32.MaxValue), Encoding.ASCII.GetBytes("moov"))))
            equal(false, DolbyVisionProbe.Read(malformed).Count > 0, "malformed box lengths return no HDR override");
        using (MemoryStream unframed = new MemoryStream(Box("mdat", rpu)))
            equal(false, DolbyVisionProbe.Read(unframed).Count > 0, "byte patterns outside a HEVC sample are ignored");
        equal(true, "Dolby Vision" == MediaTrack.HdrName("Dolby Vision", "HLG", "HLG"), "explicit Dolby Vision takes precedence over HLG");
        equal(true, "HLG" == MediaTrack.HdrName("", "", "HLG"), "HLG without Dolby metadata retains HLG label");
    }
    static HashSet<long> Probe(params Track[] tracks)
    {
        byte[] ftyp = Box("ftyp", Encoding.ASCII.GetBytes("isom"), U32(0), Encoding.ASCII.GetBytes("isom"));
        byte[] moov = Movie(tracks, 0), media = new byte[0];
        long firstSample = ftyp.Length + moov.Length + 8;
        moov = Movie(tracks, firstSample);
        foreach (Track track in tracks) foreach (byte[] nal in track.Samples) media = Join(media, Sample(nal, track.LengthSize));
        using (MemoryStream file = new MemoryStream(Join(ftyp, moov, Box("mdat", media)))) return DolbyVisionProbe.Read(file);
    }
    static byte[] Movie(Track[] tracks, long offset)
    {
        byte[] result = new byte[0];
        foreach (Track track in tracks)
        {
            byte[] sizes = new byte[0], positions = new byte[0];
            int sampleSize = track.Samples[0].Length + track.LengthSize;
            foreach (byte[] nal in track.Samples)
            {
                sizes = Join(sizes, U32((uint)(nal.Length + track.LengthSize)));
                positions = Join(positions, track.WideOffsets ? Join(U32((uint)(offset >> 32)), U32((uint)offset)) : U32((uint)offset));
                offset += nal.Length + track.LengthSize;
            }
            byte[] config = new byte[22]; config[0] = 1; config[21] = (byte)(track.LengthSize - 1);
            byte[] description = Box(track.Codec, new byte[78], Box("hvcC", config));
            byte[] stbl = Box("stbl", Box("stsd", U32(0), U32(1), description),
                Box("stsz", U32(0), U32(track.FixedSize ? (uint)sampleSize : 0), U32((uint)track.Samples.Length), track.FixedSize ? new byte[0] : sizes),
                Box("stsc", U32(0), U32(1), U32(1), U32(1), U32(1)),
                Box(track.WideOffsets ? "co64" : "stco", U32(0), U32((uint)track.Samples.Length), positions));
            result = Join(result, Box("trak", Box("tkhd", new byte[12], U32(track.Id)),
                Box("mdia", Box("hdlr", new byte[8], Encoding.ASCII.GetBytes("vide")), Box("minf", stbl))));
        }
        return Box("moov", result);
    }
    static byte[] Sample(byte[] nal, int lengthSize)
    {
        byte[] length = U32((uint)nal.Length), prefix = new byte[lengthSize];
        Array.Copy(length, 4 - lengthSize, prefix, 0, lengthSize); return Join(prefix, nal);
    }
    static byte[] Box(string type, params byte[][] data)
    { byte[] body = Join(data); return Join(U32((uint)(body.Length + 8)), Encoding.ASCII.GetBytes(type), body); }
    static byte[] U32(uint n) { return new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n }; }
    static byte[] Join(params byte[][] parts)
    {
        using (MemoryStream data = new MemoryStream())
        { foreach (byte[] part in parts) data.Write(part, 0, part.Length); return data.ToArray(); }
    }
}
