using System;
using System.IO;
using System.Text;

namespace TaskbarTimer
{
    /// <summary>
    /// Builds the alarm audio as raw PCM.
    ///
    /// SoundPlayer has no volume property, and the alternatives are all worse:
    /// waveOutSetVolume moves the volume of the whole output device (so it would turn
    /// down everything else you are listening to), and WPF's MediaPlayer needs a
    /// Dispatcher that a WinForms message loop does not provide. So volume is applied
    /// to the sample amplitudes when the clip is generated. Changing the volume
    /// rebuilds the clip, which costs about a millisecond.
    /// </summary>
    internal static class AlertSound
    {
        private const int SampleRate = 44100;
        private const int ToneHz     = 880;    // A5 - cuts through speech and fan noise

        private struct Segment
        {
            public readonly double Hz;      // 0 = silence
            public readonly double Seconds;
            public Segment(double hz, double seconds) { Hz = hz; Seconds = seconds; }
        }

        /// <summary>
        /// Two short beeps then a rest, looping to roughly 1.2s. Deliberately not a
        /// continuous tone: an intermittent pattern reads as an alarm rather than as
        /// something broken, and it is far easier to sit next to for two minutes.
        /// </summary>
        public static byte[] BuildTone(int volumePercent)
        {
            double gain = Clamp(volumePercent, 0, 100) / 100.0;

            Segment[] pattern =
            {
                new Segment(ToneHz, 0.15),
                new Segment(0,      0.09),
                new Segment(ToneHz, 0.15),
                new Segment(0,      0.81)
            };

            int total = 0;
            foreach (Segment seg in pattern) total += (int)(seg.Seconds * SampleRate);

            short[] samples = new short[total];
            int at = 0;
            foreach (Segment seg in pattern)
            {
                int n = (int)(seg.Seconds * SampleRate);
                if (seg.Hz > 0)
                {
                    // 6ms fade in and out, or each beep starts and ends with a click.
                    int fade = (int)(0.006 * SampleRate);
                    for (int i = 0; i < n; i++)
                    {
                        double env = 1.0;
                        if (i < fade)          env = (double)i / fade;
                        else if (i > n - fade) env = (double)(n - i) / fade;

                        double v = Math.Sin(2.0 * Math.PI * seg.Hz * i / SampleRate);
                        samples[at + i] = (short)(v * env * gain * short.MaxValue * 0.85);
                    }
                }
                at += n;
            }

            return WrapPcm16Mono(samples);
        }

        /// <summary>
        /// Applies volume to a user-supplied wav by scaling its samples. Only 16-bit PCM
        /// is handled, which covers almost every wav in the wild; anything else is
        /// returned untouched and simply plays at its own level.
        /// </summary>
        public static byte[] ApplyVolume(byte[] wav, int volumePercent)
        {
            double gain = Clamp(volumePercent, 0, 100) / 100.0;
            if (Math.Abs(gain - 1.0) < 0.001) return wav;

            try
            {
                if (wav.Length < 44) return wav;
                if (Encoding.ASCII.GetString(wav, 0, 4) != "RIFF") return wav;
                if (Encoding.ASCII.GetString(wav, 8, 4) != "WAVE") return wav;

                int bitsPerSample = 0;
                int dataStart = -1, dataLen = 0;
                int pos = 12;

                while (pos + 8 <= wav.Length)
                {
                    string id  = Encoding.ASCII.GetString(wav, pos, 4);
                    int    len = BitConverter.ToInt32(wav, pos + 4);
                    int    body = pos + 8;

                    if (id == "fmt " && len >= 16)
                    {
                        short format = BitConverter.ToInt16(wav, body);
                        bitsPerSample = BitConverter.ToInt16(wav, body + 14);
                        if (format != 1) return wav;          // not plain PCM
                    }
                    else if (id == "data")
                    {
                        dataStart = body;
                        dataLen   = Math.Min(len, wav.Length - body);
                    }
                    pos = body + len + (len % 2);             // chunks are word aligned
                }

                if (bitsPerSample != 16 || dataStart < 0) return wav;

                byte[] copy = (byte[])wav.Clone();
                for (int i = dataStart; i + 1 < dataStart + dataLen; i += 2)
                {
                    short s = BitConverter.ToInt16(copy, i);
                    int scaled = (int)(s * gain);
                    if (scaled >  short.MaxValue) scaled = short.MaxValue;
                    if (scaled <  short.MinValue) scaled = short.MinValue;
                    byte[] b = BitConverter.GetBytes((short)scaled);
                    copy[i]     = b[0];
                    copy[i + 1] = b[1];
                }
                return copy;
            }
            catch { return wav; }
        }

        private static byte[] WrapPcm16Mono(short[] samples)
        {
            int dataLen = samples.Length * 2;
            using (var ms = new MemoryStream(44 + dataLen))
            using (var w  = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataLen);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);                          // fmt chunk size
                w.Write((short)1);                    // PCM
                w.Write((short)1);                    // mono
                w.Write(SampleRate);
                w.Write(SampleRate * 2);              // byte rate
                w.Write((short)2);                    // block align
                w.Write((short)16);                   // bits per sample
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataLen);
                foreach (short s in samples) w.Write(s);
                w.Flush();
                return ms.ToArray();
            }
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }
}
