using System;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TaskbarTimer
{
    /// <summary>
    /// Turns loose human input into seconds: "30", "30m", "1h30m", "90s", "2h", "45 min".
    /// A bare number is read as minutes, which is what people mean nine times out of ten.
    /// </summary>
    public static class DurationParser
    {
        public static int Parse(string input)
        {
            if (string.IsNullOrEmpty(input)) return 0;
            string s = input.Trim().ToLowerInvariant();

            int bare;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out bare))
                return bare > 0 ? bare * 60 : 0;

            // mm:ss or h:mm:ss
            Match clock = Regex.Match(s, @"^(\d+):([0-5]?\d)(?::([0-5]?\d))?$");
            if (clock.Success)
            {
                int a = int.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture);
                int b = int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture);
                if (clock.Groups[3].Success)
                    return a * 3600 + b * 60 + int.Parse(clock.Groups[3].Value, CultureInfo.InvariantCulture);
                return a * 60 + b;
            }

            int total = 0;
            bool any = false;
            foreach (Match m in Regex.Matches(s, @"(\d+)\s*(h|hr|hrs|hour|hours|m|min|mins|minute|minutes|s|sec|secs|second|seconds)"))
            {
                int n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                string unit = m.Groups[2].Value;
                if (unit[0] == 'h') total += n * 3600;
                else if (unit[0] == 's') total += n;
                else total += n * 60;
                any = true;
            }
            return any ? total : 0;
        }
    }

    /// <summary>Run-key entry, so the timer comes back after a reboot.</summary>
    public static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name   = "IllumEdTaskbarTimer";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                        return k != null && k.GetValue(Name) != null;
                }
                catch { return false; }
            }
        }

        public static void Set(bool enabled)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (enabled)
                        k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
                    else if (k.GetValue(Name) != null)
                        k.DeleteValue(Name, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not change the startup setting.\r\n\r\n" + ex.Message,
                                "Taskbar Timer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    /// <summary>Small prompt for a duration the presets do not cover.</summary>
    public sealed class CustomDurationDialog : Form
    {
        private readonly TextBox _box;
        public int Seconds { get; private set; }

        public CustomDurationDialog(int currentSeconds)
        {
            Text            = "Countdown for...";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterScreen;
            MinimizeBox     = false;
            MaximizeBox     = false;
            ShowInTaskbar   = false;
            ClientSize      = new Size(300, 116);
            TopMost         = true;

            var hint = new Label
            {
                Text     = "e.g.  25    45m    1h30m    90s    2:30",
                AutoSize = false,
                Bounds   = new Rectangle(12, 10, 276, 20)
            };

            _box = new TextBox
            {
                Bounds = new Rectangle(12, 34, 276, 24),
                Text   = (currentSeconds / 60).ToString(CultureInfo.InvariantCulture)
            };
            _box.SelectAll();

            var ok = new Button
            {
                Text         = "Start",
                DialogResult = DialogResult.OK,
                Bounds       = new Rectangle(132, 70, 74, 28)
            };
            var cancel = new Button
            {
                Text         = "Cancel",
                DialogResult = DialogResult.Cancel,
                Bounds       = new Rectangle(214, 70, 74, 28)
            };

            ok.Click += delegate
            {
                Seconds = DurationParser.Parse(_box.Text);
                if (Seconds <= 0)
                {
                    MessageBox.Show(this, "Could not read that as a length of time.",
                                    "Taskbar Timer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                }
            };

            Controls.Add(hint);
            Controls.Add(_box);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
