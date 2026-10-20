using System;
using System.IO;
using System.Text;
using BilibiliPotPlayer;

internal static class SphericalVideoProbeChecks
{
    static byte[] Box(string type, params byte[][] bodies)
    {
        using (MemoryStream file = new MemoryStream())
        {
            int size = 8; foreach (byte[] body in bodies) size += body.Length;
            file.Write(new byte[] { (byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size }, 0, 4);
            byte[] name = Encoding.ASCII.GetBytes(type); file.Write(name, 0, 4);
            foreach (byte[] body in bodies) file.Write(body, 0, body.Length);
            return file.ToArray();
        }
    }
    static byte[] Movie(byte[] child)
    {
        byte[] sample = Box("avc1", new byte[78], child);
        byte[] description = Box("stsd", new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }, sample);
        return Box("moov", Box("trak", Box("mdia", Box("minf", Box("stbl", description)))));
    }
    static bool Read(byte[] bytes) { using (MemoryStream file = new MemoryStream(bytes)) return SphericalVideoProbe.Read(file); }
    static byte[] Legacy(string xml)
    {
        byte[] id = { 0xFF, 0xCC, 0x82, 0x63, 0xF8, 0x55, 0x4A, 0x93, 0x88, 0x14, 0x58, 0x7A, 0x02, 0x52, 0x1F, 0xDD };
        return Box("moov", Box("trak", Box("uuid", id, Encoding.UTF8.GetBytes(xml))));
    }
    internal static void Run(Action<bool, bool, string> equal)
    {
        byte[] header = Box("svhd", new byte[5]), pose = Box("prhd", new byte[16]);
        equal(true, Read(Movie(Box("sv3d", header, Box("proj", pose, Box("equi", new byte[20]))))), "V2 equirectangular metadata");
        equal(true, Read(Movie(Box("sv3d", header, Box("proj", pose, Box("cbmp", new byte[12]))))), "V2 cube metadata");
        equal(false, Read(Movie(Box("st3d", new byte[5]))), "stereo alone is not spherical");
        equal(false, Read(Movie(Box("sv3d", header, Box("proj", Box("equi", new byte[20]))))), "incomplete V2 projection is rejected");
        equal(false, Read(Box("mdat", Encoding.ASCII.GetBytes("sv3d proj equi"))), "text inside media data is not a spherical box");
        string prefix = "<root xmlns:g='http://ns.google.com/videos/1.0/spherical/'>";
        equal(true, Read(Legacy(prefix + "<g:Spherical>true</g:Spherical></root>")), "V1 sphere flag uses namespace rather than a fixed prefix");
        equal(false, Read(Legacy(prefix + "<g:Spherical>false</g:Spherical></root>")), "V1 false flag");
        equal(false, Read(Legacy("<!DOCTYPE root [<!ENTITY yes 'true'>]>" + prefix + "<g:Spherical>&yes;</g:Spherical></root>")), "untrusted metadata cannot use XML entities");
    }
}
