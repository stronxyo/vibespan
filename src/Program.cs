using System;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Vibespan
{
    public static class Program
    {
        static Mutex _single;
        static Tray _tray;
        static TopmostWatcher _watcher;

        /// <summary>Identity of the row set, so the menu is rebuilt on change and not on every poll.</summary>
        static string BucketKeys(Snapshot s)
        {
            if (s == null || s.Buckets == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (Bucket b in s.Buckets)
            {
                if (!b.HasPercent) continue;
                sb.Append(b.Key).Append(b.IsActive ? "*" : "").Append('|');
            }
            return sb.ToString();
        }

        [STAThread]
        public static int Main(string[] argv)
        {
            // Statusline mode: Claude Code runs the same executable per turn, pipes it JSON and
            // renders whatever it prints. Must not touch the UI or the single-instance mutex.
            if (argv.Length > 0 && argv[0] == "--feed")
                return Feed.RunAsStatusLine();

            // --demo <file>: render a fixture instead of calling the endpoint.
            // --config <dir>: keep settings somewhere other than %LOCALAPPDATA%\Vibespan.
            for (int i = 0; i < argv.Length - 1; i++)
            {
                if (argv[i] == "--demo") Api.DemoFile = argv[i + 1];
                else if (argv[i] == "--config") Cfg.UseDir(argv[i + 1]);
            }
            bool isDemo = !string.IsNullOrEmpty(Api.DemoFile);

            // A named mutex, not "kill anything with my process name". Killing the previous
            // instance leaves its tray icon behind as a ghost until someone hovers over it.
            //
            // Demo instances opt out. An installed widget runs at High integrity and cannot
            // be stopped from a normal shell, so a shared mutex meant every screenshot run
            // silently exited and photographed the installed window instead - twenty
            // "different" variants that were all the same live window.
            if (!isDemo)
            {
                bool created;
                _single = new Mutex(true, "Local\\VibespanSingleInstance", out created);
                if (!created)
                {
                    Log.Write("another instance is already running; exiting");
                    return 0;
                }
            }

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                Log.Write("FATAL: " + (ex != null ? ex.ToString() : "unknown"));
            };

            Cfg cfg = Cfg.Load();
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var win = new WidgetWindow(cfg);

            MenuBuilder builder = new MenuBuilder(win);
            if (!isDemo) _tray = new Tray(win, delegate { return builder.Build(); });

            Action rebuild = delegate
            {
                try { win.RootBorder.ContextMenu = builder.Build(); }
                catch (Exception e) { Log.Write("menu rebuild failed: " + e.Message); }
            };
            win.MenuNeedsRebuild += rebuild;
            // The menu lists one entry per discovered bucket, so it cannot be built correctly
            // until data arrives - and it was only ever built at Loaded, when the snapshot is
            // still null. That is why Metrics sat on "loading..." forever: the placeholder was
            // cached and nothing asked for it again. Rebuild when the bucket set actually
            // changes, which is once per session in practice, not on every poll.
            string lastKeys = null;
            win.SnapshotUpdated += delegate (Snapshot s)
            {
                if (_tray != null) _tray.Update(s, win.Config);

                string keys = BucketKeys(s);
                if (keys == lastKeys) return;

                // Never swap the menu out from under an open one. lastKeys stays unchanged so
                // the next snapshot retries.
                ContextMenu open = win.RootBorder != null ? win.RootBorder.ContextMenu : null;
                if (open != null && open.IsOpen) return;

                lastKeys = keys;
                rebuild();
            };

            win.Loaded += delegate
            {
                rebuild();
                if (_tray != null) _tray.Start();
                _watcher = new TopmostWatcher(win);
                _watcher.Start();
            };

            try
            {
                app.MainWindow = win;
                win.Show();
                return app.Run();
            }
            finally
            {
                // A tray icon outlives a crashed process, so tear it down on every exit path.
                if (_watcher != null) _watcher.Dispose();
                if (_tray != null) _tray.Dispose();
                try { if (_single != null) _single.ReleaseMutex(); } catch { }
            }
        }
    }
}
