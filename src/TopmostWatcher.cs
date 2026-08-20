// Keeps the widget on top and decides when to hide for a full-screen app.
//
// Event-driven rather than polled: a 500 ms timer that re-asserts Z-order forever is what the
// predecessor did, and it burns CPU while idle for no benefit. EVENT_SYSTEM_FOREGROUND fires
// only when something actually changes.
//
// The slow timer is still here on purpose, demoted to a backstop. The hook does NOT fire when
// a full-screen app changes its own rect (borderless -> exclusive, a resolution change), nor
// when another topmost window re-asserts itself without a foreground change. Windows 11 24H2
// also has an unfixed regression where opening a WinUI 3 app knocks other processes' topmost
// windows out of Z-order, which only a re-assert recovers from.
using System;
using System.Windows.Threading;

namespace Vibespan
{
    public class TopmostWatcher : IDisposable
    {
        readonly WidgetWindow _win;
        readonly Dispatcher _dispatcher;

        // MUST be a field. A delegate passed inline to SetWinEventHook is collected by the GC
        // and the process then dies with CallbackOnCollectedDelegate - minutes or hours later,
        // which makes it a miserable bug to trace back.
        readonly Native.WinEventProc _proc;
        IntPtr _hook = IntPtr.Zero;

        DispatcherTimer _coalesce;   // events arrive in bursts; do the work once
        DispatcherTimer _fallback;

        public TopmostWatcher(WidgetWindow win)
        {
            _win = win;
            _dispatcher = win.Dispatcher;
            _proc = new Native.WinEventProc(OnWinEvent);
        }

        public void Start()
        {
            _hook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _proc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);

            if (_hook == IntPtr.Zero) Log.Write("SetWinEventHook failed; relying on the fallback timer");

            _coalesce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _coalesce.Tick += delegate
            {
                _coalesce.Stop();
                Evaluate();
            };

            _fallback = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _fallback.Tick += delegate { Evaluate(); };
            _fallback.Start();

            Evaluate();
        }

        // Out-of-context events are delivered on the thread that installed the hook, so this is
        // already the UI thread. Do no work here: events re-enter, so just restart the coalescer.
        void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (_coalesce == null) return;
                _coalesce.Stop();
                _coalesce.Start();
            }
            catch { }
        }

        void Evaluate()
        {
            try { _win.EvaluateTopmost(); }
            catch (Exception e) { Log.Write("topmost evaluate failed: " + e.Message); }
        }

        public void Dispose()
        {
            try { if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook); } catch { }
            _hook = IntPtr.Zero;
            if (_coalesce != null) _coalesce.Stop();
            if (_fallback != null) _fallback.Stop();
        }
    }
}
