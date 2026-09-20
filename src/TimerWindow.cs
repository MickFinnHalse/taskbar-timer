using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarTimer
{
    public sealed class TimerWindow : Form
    {
        private readonly Settings        _cfg;
        private readonly Label           _label;
        private readonly Timer           _tick;
        private readonly AlertController _alert;
        private readonly Stopwatch       _clock = new Stopwatch();

        private int  _remainingAtPause;   // seconds left when paused, or before starting
        private bool _expired;
        private int  _housekeeping;

        private Point _dragOrigin;
        private bool  _dragging;
        private bool  _swallowNextClick;

        private const int Gap  = 8;    // px between our right edge and the tray
        private const int PadX = 10;
        private const int PadY = 2;

        public TimerWindow(Settings cfg)
        {
            _cfg = cfg;
            _remainingAtPause = cfg.DurationSeconds;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;     // no taskbar button of its own
            TopMost         = true;
            StartPosition   = FormStartPosition.Manual;
            BackColor       = Theme.RestBack;
            DoubleBuffered  = true;

            _label = new Label
            {
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.RestFore,
                Font      = new Font("Segoe UI", _cfg.FontSize,
                                     _cfg.Bold ? FontStyle.Bold : FontStyle.Regular,
                                     GraphicsUnit.Point),
                Text      = Format(_remainingAtPause)
            };
            Controls.Add(_label);

            _alert = new AlertController(this, _label, _cfg.FlashTickMs);
            _alert.Stopped = ApplyRestingColours;   // repaint the instant the alarm ends

            BuildMenu();
            SizeToContent();
            Reposition();
            ApplyRestingColours();

            _tick = new Timer { Interval = 200 };
            _tick.Tick += OnTick;
            _tick.Start();

            _label.MouseDown += OnMouseDown;
            _label.MouseMove += OnMouseMove;
            _label.MouseUp   += OnMouseUp;
            MouseDown        += OnMouseDown;
            MouseMove        += OnMouseMove;
            MouseUp          += OnMouseUp;

            HookSystemEvents();
        }

        // Keep the window out of Alt-Tab. Deliberately NOT WS_EX_NOACTIVATE: a window
        // that never activates cannot host a context menu that dismisses properly, and
        // the right-click menu matters more here than avoiding a moment of focus.
        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        // ---------- countdown state ----------

        private bool Running { get { return _clock.IsRunning; } }

        private int RemainingSeconds
        {
            get
            {
                if (!Running) return _remainingAtPause;
                int left = _remainingAtPause - (int)Math.Floor(_clock.Elapsed.TotalSeconds);
                return left < 0 ? 0 : left;
            }
        }

        private void StartOrPause()
        {
            if (_alert.IsAlerting) { Dismiss(); return; }
            if (Running)
            {
                _remainingAtPause = RemainingSeconds;
                _clock.Reset();
            }
            else
            {
                if (_remainingAtPause <= 0) _remainingAtPause = _cfg.DurationSeconds;
                _expired = false;
                _clock.Restart();
            }
            Render();
        }

        private void ResetToDuration()
        {
            Dismiss();
            _clock.Reset();
            _remainingAtPause = _cfg.DurationSeconds;
            _expired = false;
            Render();
        }

        private void SetDuration(int seconds, bool startNow)
        {
            Dismiss();
            _cfg.DurationSeconds = seconds;
            _cfg.Save();
            _clock.Reset();
            _remainingAtPause = seconds;
            _expired = false;
            if (startNow) _clock.Restart();
            Render();
        }

        private void Dismiss()
        {
            if (_alert.IsAlerting) _alert.Stop();
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (Running && RemainingSeconds <= 0 && !_expired)
            {
                _expired = true;
                _clock.Reset();
                _remainingAtPause = 0;
                _alert.Start(_cfg.Alert, _cfg.SoundFile, _cfg.Volume, _cfg.AlertTimeoutSeconds);
            }

            Render();

            // Cheap housekeeping roughly every 2s: the taskbar may have moved, the
            // display may have changed, and z-order among topmost windows drifts.
            if (++_housekeeping >= 10)
            {
                _housekeeping = 0;
                Native.BringToTop(Handle);
                if (_cfg.AutoPosition && !_dragging) Reposition();
            }
        }

        private void Render()
        {
            string text = Format(RemainingSeconds);
            if (_label.Text != text)
            {
                _label.Text = text;
                SizeToContent();
                if (_cfg.AutoPosition && !_dragging) Reposition();
            }
            if (!_alert.IsAlerting) ApplyRestingColours();
        }

        /// <summary>
        /// Running: green, turning red once inside the final tenth of whatever the timer
        /// was set to - so the warning arrives proportionally, 3 minutes into a 30 minute
        /// countdown and 30 seconds into a 5 minute one.
        /// Paused: amber digits. The background stays theme-coloured throughout - a
        /// filled tile stood out too much against the taskbar to be worth the extra
        /// signal, and the digit colour alone reads the state perfectly well.
        /// </summary>
        private void ApplyRestingColours()
        {
            BackColor = Theme.RestBack;
            _label.ForeColor = !Running          ? Theme.PausedFore
                             : InFinalTenth      ? Theme.WarnFore
                                                 : Theme.RestFore;
        }

        private bool InFinalTenth
        {
            get
            {
                if (_cfg.DurationSeconds <= 0) return false;
                int threshold = (int)Math.Ceiling(_cfg.DurationSeconds * 0.10);
                if (threshold < 1) threshold = 1;
                return RemainingSeconds <= threshold;
            }
        }

        private static string Format(int seconds)
        {
            if (seconds < 0) seconds = 0;
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1
                ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                : string.Format("{0:00}:{1:00}", t.Minutes, t.Seconds);
        }

        // ---------- geometry ----------

        private void SizeToContent()
        {
            // Measure the widest string this format can reach, so the window does not
            // jitter sideways as the digits tick over.
            string widest = _label.Text.Length > 5 ? "0:00:00" : "00:00";
            Size m = TextRenderer.MeasureText(widest, _label.Font);
            Size target = new Size(m.Width + PadX * 2, m.Height + PadY * 2);
            if (Size != target) Size = target;
        }

        private void Reposition()
        {
            Rectangle tray = Native.GetTrayRect();
            Rectangle bar  = Native.GetTaskbarRect();
            if (tray.IsEmpty || bar.IsEmpty)
            {
                // Shell layout not recognised; fall back to the saved manual spot.
                if (_cfg.ManualX >= 0 && _cfg.ManualY >= 0)
                    Location = new Point(_cfg.ManualX, _cfg.ManualY);
                return;
            }
            int x = tray.Left - Width - Gap;
            int y = bar.Top + (bar.Height - Height) / 2;
            Location = new Point(x, y);
        }

        private void HookSystemEvents()
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) =>
            {
                RefreshTheme();
                if (_cfg.AutoPosition) Reposition();
            };
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) => RefreshTheme();
        }

        private void RefreshTheme()
        {
            Theme.Refresh();
            if (!_alert.IsAlerting) ApplyRestingColours();
        }

        // ---------- input ----------

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_alert.IsAlerting)
            {
                // Silence it, and remember to swallow the matching MouseUp. Without this
                // the click that dismisses the alarm falls through to StartOrPause and
                // silently kicks off a whole new countdown.
                Dismiss();
                _swallowNextClick = true;
                return;
            }
            _dragOrigin = e.Location;
            _dragging   = false;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (!_dragging &&
                (Math.Abs(e.X - _dragOrigin.X) > 4 || Math.Abs(e.Y - _dragOrigin.Y) > 4))
            {
                _dragging = true;
                _cfg.AutoPosition = false;   // dragging opts out of following the tray
            }
            if (_dragging)
                Location = new Point(Location.X + e.X - _dragOrigin.X,
                                     Location.Y + e.Y - _dragOrigin.Y);
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_swallowNextClick) { _swallowNextClick = false; return; }
            if (_dragging)
            {
                _dragging    = false;
                _cfg.ManualX = Location.X;
                _cfg.ManualY = Location.Y;
                _cfg.Save();
            }
            else
            {
                StartOrPause();
            }
        }

        // ---------- menu ----------

        private void BuildMenu()
        {
            // ShowCheckMargin matters: with only ShowImageMargin off, a checked item has
            // nowhere to draw its tick, so "Start with Windows" silently toggled with no
            // visible state and looked like a one-way switch.
            var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };

            foreach (int mins in new[] { 5, 10, 15, 20, 25, 30, 45, 60, 90 })
            {
                int m = mins;
                menu.Items.Add(new ToolStripMenuItem(m + " minutes", null,
                    delegate { SetDuration(m * 60, true); }));
            }
            menu.Items.Add(new ToolStripMenuItem("Custom...", null,
                delegate { PromptCustom(); }));
            menu.Items.Add(new ToolStripSeparator());

            var startPause = new ToolStripMenuItem("Start", null, delegate { StartOrPause(); });
            var reset      = new ToolStripMenuItem("Reset", null, delegate { ResetToDuration(); });
            menu.Items.Add(startPause);
            menu.Items.Add(reset);
            menu.Items.Add(new ToolStripSeparator());

            var alertMenu = new ToolStripMenuItem("Alert with");
            foreach (AlertMode mode in new[] { AlertMode.Flash, AlertMode.Sound, AlertMode.Both })
            {
                AlertMode captured = mode;
                var item = new ToolStripMenuItem(mode.ToString(), null, delegate
                {
                    _cfg.Alert = captured;
                    _cfg.Save();
                });
                item.Tag = captured;
                alertMenu.DropDownItems.Add(item);
            }
            alertMenu.DropDownOpening += delegate
            {
                foreach (ToolStripMenuItem i in alertMenu.DropDownItems)
                    i.Checked = (AlertMode)i.Tag == _cfg.Alert;
            };
            menu.Items.Add(alertMenu);

            // Volume. Picking a level plays one cycle of the alarm at that level, so you
            // can hear what you are choosing instead of finding out when it goes off.
            var volumeMenu = new ToolStripMenuItem("Volume");
            foreach (int step in new[] { 10, 25, 50, 70, 85, 100 })
            {
                int captured = step;
                var item = new ToolStripMenuItem(captured + "%", null, delegate
                {
                    _cfg.Volume = captured;
                    _cfg.Save();
                    _alert.Preview(_cfg.SoundFile, captured);
                });
                item.Tag = captured;
                volumeMenu.DropDownItems.Add(item);
            }
            volumeMenu.DropDownOpening += delegate
            {
                foreach (ToolStripItem it in volumeMenu.DropDownItems)
                {
                    ToolStripMenuItem mi = it as ToolStripMenuItem;
                    if (mi != null && mi.Tag is int) mi.Checked = (int)mi.Tag == _cfg.Volume;
                }
            };
            menu.Items.Add(volumeMenu);

            // How long the alarm keeps going if you are not at the desk.
            var lengthMenu = new ToolStripMenuItem("Alert for");
            string[] lengthLabels  = { "Until I stop it", "15 seconds", "30 seconds",
                                       "1 minute", "2 minutes", "5 minutes" };
            int[]    lengthSeconds = { 0, 15, 30, 60, 120, 300 };
            for (int i = 0; i < lengthLabels.Length; i++)
            {
                int captured = lengthSeconds[i];
                var item = new ToolStripMenuItem(lengthLabels[i], null, delegate
                {
                    _cfg.AlertTimeoutSeconds = captured;
                    _cfg.Save();
                });
                item.Tag = captured;
                lengthMenu.DropDownItems.Add(item);
            }
            lengthMenu.DropDownOpening += delegate
            {
                foreach (ToolStripItem it in lengthMenu.DropDownItems)
                {
                    ToolStripMenuItem mi = it as ToolStripMenuItem;
                    if (mi != null && mi.Tag is int) mi.Checked = (int)mi.Tag == _cfg.AlertTimeoutSeconds;
                }
            };
            menu.Items.Add(lengthMenu);

            menu.Items.Add(new ToolStripSeparator());

            var snapBack = new ToolStripMenuItem("Snap back to the tray", null, delegate
            {
                _cfg.AutoPosition = true;
                _cfg.Save();
                Reposition();
            });
            menu.Items.Add(snapBack);

            var autostart = new ToolStripMenuItem("Start with Windows", null,
                delegate { Autostart.Set(!Autostart.IsEnabled); });
            menu.Items.Add(autostart);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { Close(); }));

            menu.Opening += delegate
            {
                startPause.Text   = Running ? "Pause" : "Start";
                autostart.Checked = Autostart.IsEnabled;
                snapBack.Enabled  = !_cfg.AutoPosition;
            };

            ContextMenuStrip        = menu;
            _label.ContextMenuStrip = menu;
        }

        private void PromptCustom()
        {
            using (var dlg = new CustomDurationDialog(_cfg.DurationSeconds))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Seconds > 0)
                    SetDuration(dlg.Seconds, true);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cfg.Save();
            base.OnFormClosing(e);
        }
    }
}
