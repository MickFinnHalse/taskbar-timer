using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace TaskbarTimer
{
    public enum AlertMode { Flash, Sound, Both }

    /// <summary>
    /// Plain key=value file kept next to the exe, so it stays portable and hand-editable.
    /// </summary>
    public sealed class Settings
    {
        public int       DurationSeconds     { get; set; }
        public AlertMode Alert               { get; set; }
        public bool      AutoPosition        { get; set; }
        public int       ManualX             { get; set; }
        public int       ManualY             { get; set; }
        public float     FontSize            { get; set; }
        public bool      Bold                { get; set; }
        public int       FlashTickMs         { get; set; }
        public string    SoundFile           { get; set; }
        public int       Volume              { get; set; }   // 0-100
        public int       AlertTimeoutSeconds { get; set; }   // 0 = until dismissed

        private static string Path
        {
            get
            {
                string dir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                return System.IO.Path.Combine(dir, "settings.ini");
            }
        }

        public static Settings Defaults()
        {
            return new Settings
            {
                DurationSeconds     = 30 * 60,
                Alert               = AlertMode.Both,
                AutoPosition        = true,
                ManualX             = -1,
                ManualY             = -1,
                FontSize            = 11f,
                Bold                = true,
                FlashTickMs         = 250,
                SoundFile           = "",
                Volume              = 70,
                AlertTimeoutSeconds = 0
            };
        }

        public static Settings Load()
        {
            Settings s = Defaults();
            try
            {
                if (!File.Exists(Path)) return s;
                foreach (string raw in File.ReadAllLines(Path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    switch (k.ToLowerInvariant())
                    {
                        case "durationseconds":     s.DurationSeconds     = ParseInt(v, s.DurationSeconds);     break;
                        case "alert":               s.Alert               = ParseAlert(v, s.Alert);             break;
                        case "autoposition":        s.AutoPosition        = ParseBool(v, s.AutoPosition);       break;
                        case "manualx":             s.ManualX             = ParseInt(v, s.ManualX);             break;
                        case "manualy":             s.ManualY             = ParseInt(v, s.ManualY);             break;
                        case "fontsize":            s.FontSize            = ParseFloat(v, s.FontSize);          break;
                        case "bold":                s.Bold                = ParseBool(v, s.Bold);               break;
                        case "flashtickms":         s.FlashTickMs         = ParseInt(v, s.FlashTickMs);         break;
                        case "soundfile":           s.SoundFile           = v;                                  break;
                        case "volume":              s.Volume              = ParseInt(v, s.Volume);              break;
                        case "alerttimeoutseconds": s.AlertTimeoutSeconds = ParseInt(v, s.AlertTimeoutSeconds); break;
                    }
                }
            }
            catch { /* a corrupt settings file must never stop the timer starting */ }

            if (s.FlashTickMs < 40)  s.FlashTickMs = 40;
            if (s.FontSize    < 6f)  s.FontSize    = 6f;
            if (s.Volume      < 0)   s.Volume      = 0;
            if (s.Volume      > 100) s.Volume      = 100;
            if (s.AlertTimeoutSeconds < 0) s.AlertTimeoutSeconds = 0;
            return s;
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Taskbar Timer settings. Close the timer before editing by hand.");
                sb.AppendLine("# Alert               = Flash | Sound | Both");
                sb.AppendLine("# Volume              = 0-100 (applies to the alarm only)");
                sb.AppendLine("# AlertTimeoutSeconds = 0 means keep alerting until dismissed");
                sb.AppendLine("DurationSeconds     = " + DurationSeconds);
                sb.AppendLine("Alert               = " + Alert);
                sb.AppendLine("AutoPosition        = " + (AutoPosition ? "true" : "false"));
                sb.AppendLine("ManualX             = " + ManualX);
                sb.AppendLine("ManualY             = " + ManualY);
                sb.AppendLine("FontSize            = " + FontSize.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Bold                = " + (Bold ? "true" : "false"));
                sb.AppendLine("FlashTickMs         = " + FlashTickMs);
                sb.AppendLine("SoundFile           = " + (SoundFile ?? ""));
                sb.AppendLine("Volume              = " + Volume);
                sb.AppendLine("AlertTimeoutSeconds = " + AlertTimeoutSeconds);
                File.WriteAllText(Path, sb.ToString());
            }
            catch { }
        }

        private static int   ParseInt(string v, int d)    { int r;   return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : d; }
        private static float ParseFloat(string v, float d){ float r; return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : d; }
        private static bool  ParseBool(string v, bool d)  { return v.Equals("true", StringComparison.OrdinalIgnoreCase) ? true
                                                                 : v.Equals("false", StringComparison.OrdinalIgnoreCase) ? false : d; }
        private static AlertMode ParseAlert(string v, AlertMode d)
        {
            try { return (AlertMode)Enum.Parse(typeof(AlertMode), v, true); } catch { return d; }
        }
    }

    /// <summary>
    /// Colours. The running, warning and paused colours are fixed brand values rather
    /// than theme-derived, because they carry meaning - only the resting background
    /// follows the Windows light/dark setting so the tile still sits in the taskbar.
    /// </summary>
    public static class Theme
    {
        public static Color RestBack   { get; private set; }  // follows Windows theme
        public static Color RestFore   { get; private set; }  // #1E9E44 - running
        public static Color WarnFore   { get; private set; }  // #C22A1E - final tenth
        public static Color PausedFore { get; private set; }  // #FFAB35 - paused
        public static Color AlertBack  { get; private set; }
        public static Color AlertFore  { get; private set; }

        static Theme() { Refresh(); }

        public static void Refresh()
        {
            bool light = SystemUsesLightTheme();
            RestBack   = light ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);
            RestFore   = Color.FromArgb(0x1E, 0x9E, 0x44);
            WarnFore   = Color.FromArgb(0xC2, 0x2A, 0x1E);
            PausedFore = Color.FromArgb(0xFF, 0xAB, 0x35);
            AlertBack  = Color.FromArgb(200, 32, 32);
            AlertFore  = Color.White;
        }

        private static bool SystemUsesLightTheme()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k == null) return false;
                    object v = k.GetValue("SystemUsesLightTheme");
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }
    }
}
