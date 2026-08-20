// Threshold alerts.
//
// In-widget is the PRIMARY channel and the balloon is best-effort. That is not a stylistic
// choice: a uiAccess process launched by a member of Administrators runs at High integrity,
// and notifications from elevated processes are silently suppressed - no exception, no
// error, nothing on screen. A pulse drawn by the widget itself is in-process, immune to UIPI
// and to Focus Assist, and it is already where the user is looking.
//
// Alerting discipline, which is what keeps this from becoming noise:
//   * rising edge only, once per window - polling must not produce one alert per poll
//   * hysteresis: an alert only re-arms after the value drops well back below the level
//   * a new window (a changed reset time) re-arms everything
//   * silent by default; a single short pulse, never a continuous flash (WCAG 2.3.1 caps
//     flashing at 3 Hz and a permanently blinking widget is the classic annoyance)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Vibespan
{
    public class Alerts
    {
        const double Hysteresis = 5.0;      // must fall this far below a level to re-arm

        class State
        {
            public Dictionary<int, bool> Armed = new Dictionary<int, bool>();
            public string WindowStamp;      // reset time; a change means a fresh window
        }

        readonly Dictionary<string, State> _states = new Dictionary<string, State>(StringComparer.Ordinal);
        readonly WidgetWindow _win;
        DispatcherTimer _pulse;
        int _pulsePhase;
        Brush _restoreBorder;

        public Alerts(WidgetWindow win) { _win = win; }

        /// <summary>Set by Tray so an alert can also try a balloon. Null if there is no tray.</summary>
        public Action<string, string> BalloonSink;

        public void Evaluate(Snapshot snap, Cfg cfg)
        {
            if (snap == null || cfg == null) return;

            foreach (Bucket b in snap.Buckets)
            {
                if (!b.HasPercent) continue;
                RowCfg row = cfg.Row(b.Key);
                if (row == null || !row.Visible) continue;   // don't alert on a hidden metric

                State st;
                if (!_states.TryGetValue(b.Key, out st)) { st = new State(); _states[b.Key] = st; }

                string stamp = b.ResetIso ?? "";
                if (st.WindowStamp != stamp)
                {
                    st.WindowStamp = stamp;
                    st.Armed.Clear();                        // new window: everything re-arms
                }

                foreach (int level in cfg.AlertLevels)
                {
                    bool armed;
                    if (!st.Armed.TryGetValue(level, out armed)) { armed = true; st.Armed[level] = true; }

                    if (armed && b.Percent >= level)
                    {
                        st.Armed[level] = false;
                        Fire(b, level, cfg);
                    }
                    else if (!armed && b.Percent < level - Hysteresis)
                    {
                        st.Armed[level] = true;              // dropped clear; allow it again
                    }
                }
            }
        }

        void Fire(Bucket b, int level, Cfg cfg)
        {
            string reset = Fmt.Countdown(b.ResetsAt);
            string body = string.Format(I18n.T.AlertBody,
                                        b.LongLabel,
                                        ((int)Math.Round(b.Percent)).ToString(CultureInfo.InvariantCulture),
                                        reset.Length > 0 ? reset : "-");
            Log.Write("alert: " + b.Key + " crossed " + level + "% (" + b.Percent.ToString("0.#", CultureInfo.InvariantCulture) + "%)");

            if (cfg.IsMuted) return;

            Pulse(cfg, b.Percent);

            if (cfg.AlertSound)
            {
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
            }
            if (BalloonSink != null)
            {
                try { BalloonSink(I18n.T.AlertTitle, body); } catch { }
            }
        }

        /// <summary>Two short border flashes, then restore. Deliberately finite.</summary>
        void Pulse(Cfg cfg, double pct)
        {
            if (_win == null || _win.RootBorder == null) return;
            if (_pulse != null) { _pulse.Stop(); _pulse = null; }

            _restoreBorder = _win.RootBorder.BorderBrush;
            Brush hot = Theme.PercentBrush(cfg, Math.Max(pct, Theme.CriticalAt(cfg)), null);

            _pulsePhase = 0;
            _pulse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _pulse.Tick += delegate
            {
                _pulsePhase++;
                bool on = (_pulsePhase % 2) == 1;
                _win.RootBorder.BorderBrush = on ? hot : _restoreBorder;
                if (_pulsePhase >= 4)
                {
                    _pulse.Stop();
                    _pulse = null;
                    _win.RootBorder.BorderBrush = _restoreBorder;
                }
            };
            _win.RootBorder.BorderBrush = hot;
            _pulse.Start();
        }

        /// <summary>Called when the user edits the levels, so a new level does not fire retroactively.</summary>
        public void Rearm()
        {
            foreach (KeyValuePair<string, State> kv in _states) kv.Value.Armed.Clear();
        }
    }
}
