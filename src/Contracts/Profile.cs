using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;


namespace BilibiliPotPlayer
{
    public sealed class Profile
    {
        public int HoldMilliseconds = 300, RewindIntervalMilliseconds = 100;
        public int SeekSeconds = 5, VolumeStepPercent = 10;
        public static Profile Load()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "input-profile.json");
            if (!File.Exists(path)) throw new FileNotFoundException("input-profile.json is missing", path);
            Profile p = new JavaScriptSerializer().Deserialize<Profile>(File.ReadAllText(path));
            if (p.HoldMilliseconds < 100 || p.HoldMilliseconds > 2000 ||
                p.RewindIntervalMilliseconds < 50 || p.RewindIntervalMilliseconds > 1000 ||
                p.SeekSeconds < 1 || p.SeekSeconds > 60 || p.VolumeStepPercent < 1 || p.VolumeStepPercent > 100)
                throw new InvalidDataException("Invalid input profile");
            return p;
        }
    }
}
