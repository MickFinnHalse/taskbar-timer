using System;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace TaskbarTimer
{
    /// <summary>
    /// Fires when the countdown reaches zero.
    ///
    /// Deliberately does NOT use toast notifications: those go through the Windows
    /// notification pipeline, which is switched off on this machine, so an alert that
    /// relied on them would silently do nothing.
    /// </summary>
    public sealed class AlertController
    {
        private readonly Form  _host;
        private readonly Label _label;
        private readonly Timer _flashTimer;
        private Timer          _timeout;
        private SoundPlayer    _player;
        private int            _frame;

        public bool IsAlerting { get; private set; }

        /// <summary>Raised after the alert stops, so the window can repaint itself.</summary>
        public Action Stopped;

        public AlertController(Form host, Label label, int flashTickMs)
        {
            _host  = host;
            _label = label;
            _flashTimer = new Timer { Interval = flashTickMs };
            _flashTimer.Tick += delegate { _frame++; ApplyFlashFrame(_frame); };
        }

        public int FlashTickMs
        {
            get { return _flashTimer.Interval; }
            set { _flashTimer.Interval = Math.Max(40, value); }
        }

        /// <param name="timeoutSeconds">0 keeps alerting until it is dismissed.</param>
        public void Start(AlertMode mode, string soundFile, int volume, int timeoutSeconds)
        {
            if (IsAlerting) return;
            IsAlerting = true;
            _frame = 0;

            if (mode == AlertMode.Flash || mode == AlertMode.Both)
                _flashTimer.Start();

            if (mode == AlertMode.Sound || mode == AlertMode.Both)
                StartSound(soundFile, volume);

            if (timeoutSeconds > 0)
            {
                _timeout = new Timer { Interval = timeoutSeconds * 1000 };
                _timeout.Tick += delegate { Stop(); };
                _timeout.Start();
            }
        }

        /// <summary>Silences the alert. The window repaints itself via Stopped.</summary>
        public void Stop()
        {
            if (!IsAlerting) return;
            IsAlerting = false;
            _flashTimer.Stop();
            if (_timeout != null) { _timeout.Stop(); _timeout.Dispose(); _timeout = null; }
            StopSound();
            if (Stopped != null) Stopped();
        }

        /// <summary>Plays one cycle of the alarm at the given volume, for previewing.</summary>
        public void Preview(string soundFile, int volume)
        {
            if (IsAlerting) return;
            StopSound();
            try
            {
                using (var ms = new MemoryStream(BuildClip(soundFile, volume)))
                {
                    var p = new SoundPlayer(ms);
                    p.Load();
                    p.Play();      // async; the clip is about 1.2s
                }
            }
            catch { }
        }

        private void StartSound(string soundFile, int volume)
        {
            try
            {
                byte[] clip = BuildClip(soundFile, volume);
                _player = new SoundPlayer(new MemoryStream(clip));
                _player.Load();
                _player.PlayLooping();
            }
            catch
            {
                // Last resort so an unreadable custom wav still makes some noise.
                _player = null;
                try { SystemSounds.Exclamation.Play(); } catch { }
            }
        }

        /// <summary>
        /// The custom wav if one is configured and readable, otherwise the generated
        /// tone. Volume is baked into the samples either way - see AlertSound.
        /// </summary>
        private static byte[] BuildClip(string soundFile, int volume)
        {
            if (!string.IsNullOrEmpty(soundFile) && File.Exists(soundFile))
            {
                try { return AlertSound.ApplyVolume(File.ReadAllBytes(soundFile), volume); }
                catch { }
            }
            return AlertSound.BuildTone(volume);
        }

        private void StopSound()
        {
            if (_player == null) return;
            try { _player.Stop(); _player.Dispose(); } catch { }
            _player = null;
        }

        /// <summary>
        /// Called every FlashTickMs for as long as the alert is running. Frame 1 is the
        /// first tick, and it counts up without limit unless a timeout stops it.
        ///
        /// Two phases, on the theory that an alarm should shout and then nag rather than
        /// strobe forever: a hard 2 Hz flash for the first six seconds to catch the eye,
        /// then one brief blink per second, so an alert you walked away from stays
        /// visible without becoming unbearable.
        ///
        /// The digits stay legible throughout - the background carries the flash and the
        /// text colour follows it, rather than the text blinking out.
        /// </summary>
        private void ApplyFlashFrame(int frame)
        {
            const int HardStrobeFrames = 24;   // 24 x 250ms = 6 seconds

            bool lit = frame <= HardStrobeFrames
                ? frame % 2 == 0                // every other frame: 2 Hz
                : frame % 4 == 0;               // one frame in four: a blink per second

            _host.BackColor  = lit ? Theme.AlertBack : Theme.RestBack;
            _label.ForeColor = lit ? Theme.AlertFore : Theme.RestFore;
        }
    }
}
