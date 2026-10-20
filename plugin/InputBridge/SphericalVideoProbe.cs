using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Box = BilibiliPotPlayer.DolbyVisionProbe.Box;
using Reader = BilibiliPotPlayer.DolbyVisionProbe.Reader;

namespace BilibiliPotPlayer
{
    internal static class SphericalVideoProbe
    {
        static readonly byte[] GoogleId = { 0xFF, 0xCC, 0x82, 0x63, 0xF8, 0x55, 0x4A, 0x93, 0x88, 0x14, 0x58, 0x7A, 0x02, 0x52, 0x1F, 0xDD };
        internal static bool Candidate(string path, int width, int height, bool metadata)
        {
            if (metadata) return true;
            // Repacked files can lose their spherical metadata. Require an explicit marker
            // as well as a 2:1 canvas; an ordinary 2:1 film is not a camera input target.
            return width > 0 && height > 0 && width == height * 2 && Regex.IsMatch(Path.GetFileNameWithoutExtension(path ?? ""),
                @"360\s*[°度]|\b360\s*(?:VR|video)\b|\bVR\s*360\b|全景|spherical", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        internal static bool Read(string path)
        {
            try { using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) return Read(file); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        internal static bool Read(Stream file)
        {
            try { return Inspect(new Reader(file), new Box { Start = 0, End = file.Length }, 0); }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (XmlException) { return false; }
            catch (DecoderFallbackException) { return false; }
        }
        static bool Inspect(Reader reader, Box parent, int depth)
        {
            if (depth > 8) return false;
            foreach (Box box in reader.Boxes(parent.Start, parent.End))
            {
                if (box.Type == "uuid" && Legacy(reader, box)) return true;
                if (box.Type == "moov" || box.Type == "trak" || box.Type == "mdia" || box.Type == "minf" || box.Type == "stbl")
                { if (Inspect(reader, box, depth + 1)) return true; }
                else if (box.Type == "stsd")
                {
                    uint count = reader.U32(box.Start + 4, box.End);
                    var entries = reader.Boxes(box.Start + 8, box.End);
                    if (count != entries.Count || count > 256) continue;
                    foreach (Box entry in entries)
                    {
                        if (entry.Type != "avc1" && entry.Type != "avc3" && entry.Type != "hvc1" && entry.Type != "hev1" &&
                            entry.Type != "av01" && entry.Type != "vp09" && entry.Type != "mp4v" && entry.Type != "encv" && entry.Type != "dvhe" && entry.Type != "dvh1") continue;
                        if (entry.End - entry.Start < 78) continue;
                        foreach (Box child in reader.Boxes(entry.Start + 78, entry.End))
                            if (child.Type == "sv3d" && Projection(reader, child)) return true;
                    }
                }
            }
            return false;
        }
        static bool Projection(Reader reader, Box spherical)
        {
            Box header = reader.Find(spherical, "svhd"), projection = reader.Find(spherical, "proj");
            if (header.End - header.Start < 5) return false;
            Box pose = reader.Find(projection, "prhd");
            if (pose.End - pose.Start < 16) return false;
            foreach (Box box in reader.Boxes(projection.Start, projection.End))
                if (box.Type == "equi" && box.End - box.Start >= 20 ||
                    (box.Type == "cbmp" || box.Type == "mshp") && box.End - box.Start >= 12) return true;
            return false;
        }
        static bool Legacy(Reader reader, Box box)
        {
            if (box.End - box.Start < 16 || box.End - box.Start > 262160) return false;
            byte[] id = reader.Bytes(box.Start, 16, box.End);
            for (int i = 0; i < id.Length; i++) if (id[i] != GoogleId[i]) return false;
            string xml = new UTF8Encoding(false, true).GetString(reader.Bytes(box.Start + 16, (int)(box.End - box.Start - 16), box.End)).TrimEnd('\0');
            XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144 };
            using (XmlReader document = XmlReader.Create(new StringReader(xml), settings))
                while (document.Read())
                    if (document.NodeType == XmlNodeType.Element && document.LocalName == "Spherical" && document.NamespaceURI == "http://ns.google.com/videos/1.0/spherical/")
                        return String.Equals(document.ReadElementContentAsString().Trim(), "true", StringComparison.OrdinalIgnoreCase);
            return false;
        }
    }
}
