using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;


namespace BilibiliPotPlayer
{
    internal sealed class UiLanguage
    {
        readonly string file;
        internal UiLanguage(string file) { this.file = file ?? ""; }
        bool Chinese { get { return file.StartsWith("Chinese", StringComparison.OrdinalIgnoreCase); } }
        bool Traditional { get { return file.IndexOf("Traditional", StringComparison.OrdinalIgnoreCase) >= 0; } }
        internal string Audio { get { return Chinese ? Traditional ? "音訊" : "音频" : "Audio"; } }
        internal string Video { get { return Chinese ? "视频".Replace("频", Traditional ? "頻" : "频") : "Video"; } }
        internal string Subtitles { get { return Chinese ? Traditional ? "字幕和彈幕" : "字幕和弹幕" : "Subtitles & danmaku"; } }
        internal string SubtitleTitle { get { return Chinese ? "字幕" : "Subtitles"; } }
        internal string Translation { get { return Chinese ? Traditional ? "翻譯" : "翻译" : "Translation"; } }
        internal string TranslationPending { get { return Chinese ? Traditional ? "暫不可用" : "暂不可用" : "Unavailable"; } }
        internal string Off { get { return Chinese ? Traditional ? "關閉" : "关闭" : "Off"; } }
        internal string NoSubtitles { get { return Chinese ? Traditional ? "沒有可用字幕" : "没有可用字幕" : "No subtitles"; } }
        internal string OpenSubtitles { get { return Chinese ? Traditional ? "載入字幕…" : "加载字幕…" : "Open subtitles…"; } }
        internal string SubtitleSettings { get { return Chinese ? "字幕设置…".Replace("设置", Traditional ? "設定" : "设置") : "Subtitle settings…"; } }
        internal string Spatial(string value)
        { return value == "Atmos" ? Chinese ? Traditional ? "杜比全景聲" : "杜比全景声" : "Dolby Atmos" : value; }
        internal string Hdr(string value)
        { return value == "Dolby Vision" && Chinese ? Traditional ? "杜比視界" : "杜比视界" : value; }
        internal string ChannelLabel(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            string[] parts = value.Split('.'); int bed, height = 0;
            if (parts.Length < 2 || !Int32.TryParse(parts[0], out bed)) return value;
            if (parts.Length >= 3) Int32.TryParse(parts[2], out height);
            string label;
            if (height > 0) label = Chinese ? "沉浸式" : "Immersive ";
            else if (bed == 1) label = Chinese ? Traditional ? "單聲道" : "单声道" : "Mono ";
            else if (bed == 2) label = Chinese ? Traditional ? "立體聲" : "立体声" : "Stereo ";
            else label = Chinese ? Traditional ? "環繞聲" : "环绕声" : "Surround ";
            return label + value;
        }
        internal static UiLanguage Read()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\DAUM\\PotPlayerMini64\\Settings"))
                return new UiLanguage(key == null ? "English.ini" : Convert.ToString(key.GetValue("LanguageIni", "English.ini")));
        }
    }
}
