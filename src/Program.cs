using System;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarTimer
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Only ever one timer on the taskbar; a second launch just exits.
            bool isFirst;
            using (var single = new Mutex(true, "Local\\IllumEdTaskbarTimer", out isFirst))
            {
                if (!isFirst) return;

                // Before any window exists, or tray coordinates come back virtualised
                // on a scaled display and the timer lands in the wrong place.
                Native.EnableDpiAwareness();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Settings cfg = Settings.Load();
                ApplyCommandLine(cfg, args);

                Application.Run(new TimerWindow(cfg));
                GC.KeepAlive(single);
            }
        }

        /// <summary>
        /// TaskbarTimer.exe 25         -> 25 minute default
        /// TaskbarTimer.exe 90s        -> 90 seconds
        /// TaskbarTimer.exe 1h30m      -> an hour and a half
        /// </summary>
        private static void ApplyCommandLine(Settings cfg, string[] args)
        {
            if (args == null || args.Length == 0) return;
            int seconds = DurationParser.Parse(string.Join(" ", args));
            if (seconds > 0) cfg.DurationSeconds = seconds;
        }
    }
}
