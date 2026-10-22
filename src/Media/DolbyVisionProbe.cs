using System;
using System.Collections.Generic;
using System.IO;

namespace BilibiliPotPlayer
{
    // Fallback for non-fragmented HEVC MP4s whose dvcC/dvvC was lost during remuxing.
    // Read sample tables and a bounded number of NAL headers, without decoding or changing media.
    internal static class DolbyVisionProbe
    {
        const int MaxSamples = 32, MaxBoxes = 4096, MaxRpuBytes = 65536;
        internal struct Box { internal long Start, End; internal string Type; }
        internal sealed class Reader
        {
            internal readonly Stream File;
            int boxes;
            internal Reader(Stream file) { File = file; }
            internal byte[] Bytes(long position, int count, long end)
            {
                if (position < 0 || count < 0 || position > end - count || end > File.Length) throw new InvalidDataException();
                byte[] data = new byte[count]; File.Position = position;
                int got = 0;
                while (got < count) { int n = File.Read(data, got, count - got); if (n == 0) throw new EndOfStreamException(); got += n; }
                return data;
            }
            internal uint U32(long position, long end) { return Big32(Bytes(position, 4, end), 0); }
            internal ulong U64(long position, long end)
            { return ((ulong)U32(position, end) << 32) | U32(position + 4, end); }
            internal List<Box> Boxes(long start, long end)
            {
                List<Box> result = new List<Box>();
                for (long pos = start; pos < end; )
                {
                    if (++boxes > MaxBoxes) throw new InvalidDataException();
                    byte[] header = Bytes(pos, 8, end); ulong size = Big32(header, 0); int length = 8;
                    if (size == 1) { size = U64(pos + 8, end); length = 16; }
                    else if (size == 0) size = (ulong)(end - pos);
                    if (size < (ulong)length || size > (ulong)(end - pos)) throw new InvalidDataException();
                    long next = pos + (long)size;
                    result.Add(new Box { Start = pos + length, End = next, Type = System.Text.Encoding.ASCII.GetString(header, 4, 4) });
                    pos = next;
                }
                return result;
            }
            internal Box Find(Box parent, string type)
            {
                foreach (Box box in Boxes(parent.Start, parent.End)) if (box.Type == type) return box;
                throw new InvalidDataException();
            }
            internal void Table(Box box, uint count, int width, int header)
            { if (box.End - box.Start < header || count > (ulong)(box.End - box.Start - header) / (uint)width) throw new InvalidDataException(); }
        }
        internal static HashSet<long> Read(string path)
        {
            try
            {
                using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    return Read(file);
            }
            catch (IOException) { return new HashSet<long>(); }
            catch (UnauthorizedAccessException) { return new HashSet<long>(); }
        }
        internal static HashSet<long> Read(Stream file)
        {
            HashSet<long> tracks = new HashSet<long>();
            try
            {
                Reader reader = new Reader(file);
                Box moov = reader.Find(new Box { Start = 0, End = file.Length }, "moov");
                foreach (Box track in reader.Boxes(moov.Start, moov.End))
                {
                    if (track.Type != "trak") continue;
                    try { long id = InspectTrack(reader, track); if (id > 0) tracks.Add(id); }
                    catch (InvalidDataException) { } // An unsupported/malformed track does not affect other tracks.
                    catch (IOException) { }
                }
            }
            catch (InvalidDataException) { }
            catch (IOException) { }
            return tracks;
        }
        static long InspectTrack(Reader reader, Box track)
        {
            Box tkhd = reader.Find(track, "tkhd"), mdia = reader.Find(track, "mdia"), handler = reader.Find(mdia, "hdlr");
            if (System.Text.Encoding.ASCII.GetString(reader.Bytes(handler.Start + 8, 4, handler.End)) != "vide") return 0;
            int version = reader.Bytes(tkhd.Start, 1, tkhd.End)[0];
            if (version > 1) return 0;
            uint id = reader.U32(tkhd.Start + (version == 1 ? 20 : 12), tkhd.End);
            Box table = reader.Find(reader.Find(mdia, "minf"), "stbl"), stsd = reader.Find(table, "stsd");
            uint descriptions = reader.U32(stsd.Start + 4, stsd.End);
            List<Box> entries = reader.Boxes(stsd.Start + 8, stsd.End);
            if (descriptions != entries.Count) return 0;
            List<int> lengthSizes = new List<int>();
            foreach (Box entry in entries)
            {
                int lengthSize = 0;
                if (entry.Type == "hvc1" || entry.Type == "hev1" || entry.Type == "dvhe" || entry.Type == "dvh1")
                {
                    Box hvcc = reader.Find(new Box { Start = entry.Start + 78, End = entry.End }, "hvcC");
                    if (reader.Bytes(hvcc.Start, 1, hvcc.End)[0] == 1)
                        lengthSize = (reader.Bytes(hvcc.Start + 21, 1, hvcc.End)[0] & 3) + 1;
                }
                lengthSizes.Add(lengthSize);
            }
            if (!lengthSizes.Exists(delegate(int n) { return n > 0; })) return 0;
            Box sizes = reader.Find(table, "stsz"), mapping = reader.Find(table, "stsc"), offsets = new Box();
            foreach (Box box in reader.Boxes(table.Start, table.End)) if (box.Type == "stco" || box.Type == "co64") offsets = box;
            if (offsets.Type == null) return 0; // Fragmented MP4 and compact tables use the ordinary metadata path.
            uint fixedSize = reader.U32(sizes.Start + 4, sizes.End), sampleCount = reader.U32(sizes.Start + 8, sizes.End);
            if (fixedSize == 0) reader.Table(sizes, sampleCount, 4, 12);
            uint chunkCount = reader.U32(offsets.Start + 4, offsets.End), runCount = reader.U32(mapping.Start + 4, mapping.End);
            reader.Table(offsets, chunkCount, offsets.Type == "co64" ? 8 : 4, 8); reader.Table(mapping, runCount, 12, 8);
            if (sampleCount == 0 || chunkCount == 0 || runCount == 0 || reader.U32(mapping.Start + 8, mapping.End) != 1) return 0;
            int sample = 0; uint run = 0;
            for (uint chunk = 1; chunk <= chunkCount && sample < sampleCount && sample < MaxSamples; chunk++)
            {
                if (run + 1 < runCount)
                {
                    uint next = reader.U32(mapping.Start + 8 + (run + 1) * 12L, mapping.End);
                    if (next <= chunk) { if (next != chunk) return 0; run++; }
                }
                long runStart = mapping.Start + 8 + run * 12L;
                uint count = reader.U32(runStart + 4, mapping.End), description = reader.U32(runStart + 8, mapping.End);
                if (count == 0 || description == 0 || description > lengthSizes.Count) return 0;
                long offsetPosition = offsets.Start + 8 + (chunk - 1) * (offsets.Type == "co64" ? 8L : 4L);
                ulong offset = offsets.Type == "co64" ? reader.U64(offsetPosition, offsets.End) : reader.U32(offsetPosition, offsets.End);
                if (offset > (ulong)reader.File.Length) return 0;
                long position = (long)offset;
                for (uint n = 0; n < count && sample < sampleCount && sample < MaxSamples; n++, sample++)
                {
                    uint size = fixedSize != 0 ? fixedSize : reader.U32(sizes.Start + 12 + sample * 4L, sizes.End);
                    if (size == 0 || size > reader.File.Length - position) return 0;
                    if (lengthSizes[(int)description - 1] > 0 && HasRpu(reader, position, position + size, lengthSizes[(int)description - 1])) return id;
                    position += size;
                }
            }
            return 0;
        }
        static bool HasRpu(Reader reader, long position, long end, int lengthSize)
        {
            for (int n = 0; position < end && n < 4096; n++)
            {
                byte[] length = reader.Bytes(position, lengthSize, end); uint size = 0;
                foreach (byte b in length) size = (size << 8) | b;
                position += lengthSize;
                if (size < 2 || size > end - position) return false;
                byte[] header = reader.Bytes(position, 2, end);
                if (header[0] == 0x7C && header[1] == 1 && size <= MaxRpuBytes &&
                    IsRpu(reader.Bytes(position, (int)size, end))) return true;
                position += size;
            }
            return false;
        }
        internal static bool IsRpu(byte[] nal)
        {
            if (nal.Length < 12 || nal.Length > MaxRpuBytes || nal[0] != 0x7C || nal[1] != 1) return false;
            List<byte> rbsp = new List<byte>(); int zeros = 0;
            for (int i = 2; i < nal.Length; i++)
            {
                byte b = nal[i];
                if (zeros >= 2 && b == 3)
                { if (i + 1 >= nal.Length || nal[i + 1] > 3) return false; zeros = 0; continue; }
                rbsp.Add(b); zeros = b == 0 ? zeros + 1 : 0;
            }
            while (rbsp.Count > 0 && rbsp[rbsp.Count - 1] == 0) rbsp.RemoveAt(rbsp.Count - 1);
            if (rbsp.Count < 10 || rbsp[0] != 25 || rbsp[rbsp.Count - 1] != 0x80) return false;
            // RPU type 2, format 18. CRC-32/MPEG-2 covers the RBSP after prefix 25,
            // including the transmitted checksum, excluding the final 0x80 terminator.
            int type = rbsp[1] >> 2;
            int format = ((rbsp[1] & 3) << 9) | (rbsp[2] << 1) | (rbsp[3] >> 7);
            if (type != 2 || format != 18) return false;
            uint crc = 0xFFFFFFFF;
            for (int i = 1; i < rbsp.Count - 1; i++)
            {
                crc ^= (uint)rbsp[i] << 24;
                for (int bit = 0; bit < 8; bit++) crc = (crc << 1) ^ ((crc & 0x80000000) != 0 ? 0x04C11DB7u : 0);
            }
            return crc == 0;
        }
        static uint Big32(byte[] bytes, int at)
        { return ((uint)bytes[at] << 24) | ((uint)bytes[at + 1] << 16) | ((uint)bytes[at + 2] << 8) | bytes[at + 3]; }
    }
}
