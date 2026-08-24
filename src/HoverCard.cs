// The hover read-out, shared by the widget and the tray icon.
//
// One visual, two hosts. The widget puts the card inside a WPF ToolTip; the tray shows it in
// this window. They must not drift apart - the whole point is that hovering either surface
// tells you the same thing.
//
// Why the tray needs a window of its own rather than the shell's tooltip: NotifyIcon.Text
// THROWS above 63 characters, which is why the tray used to read "Vibespan / 5h 17%" while the
// widget showed three full lines. 63 characters cannot hold a metric line, a reset countdown
// and a provenance line, so the shell tooltip is switched off (Text left empty) and this is
// drawn instead.
//
// The window never takes focus and never takes a click - WS_EX_NOACTIVATE keeps the foreground
// window where it was, WS_EX_TRANSPARENT lets clicks fall through to the tray icon underneath,
// and WS_EX_TOOLWINDOW keeps it out of Alt-Tab.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Vibespan
{
    public static class HoverCard
    {
        // Same palette as the context menu, so every surface the widget owns reads as one thing.
        static readonly Brush Bg = Frozen(0x2C, 0x2C, 0x2C);
        static readonly Brush Bd = Frozen(0x48, 0x48, 0x48);
        static readonly Brush Fg = Frozen(0xF2, 0xF2, 0xF2);
        static readonly Brush Dim = Frozen(0x9A, 0x9A, 0x9A);

        public const double FontPx = 13.5;

        static Brush Frozen(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        static FontFamily Face()
        {
            // Falls back on its own if the Variable face is missing (pre-Windows 11).
            return new FontFamily("Segoe UI Variable Text, Segoe UI");
        }

        /// <summary>
        /// The card itself. <paramref name="metaFrom"/> is the index of the first metadata line -
        /// the "updated 15:43 - polled" tail - which is rendered dimmer and a shade smaller so the
        /// numbers stay the thing your eye lands on.
        /// </summary>
        public static Border Build(string text, int metaFrom)
        {
            var stack = new StackPanel();
            string[] lines = (text ?? "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                bool meta = metaFrom >= 0 && i >= metaFrom;
                stack.Children.Add(new TextBlock
                {
                    Text = line,
                    Foreground = meta ? Dim : Fg,
                    FontFamily = Face(),
                    FontSize = meta ? FontPx - 1.0 : FontPx,
                    Margin = new Thickness(0, i == 0 ? 0 : 3, 0, 0)
                });
            }

            var border = new Border
            {
                Background = Bg,
                BorderBrush = Bd,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(13, 10, 15, 10),
                Child = stack,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 16,
                    ShadowDepth = 3,
                    Direction = 270,
                    Opacity = 0.55,
                    Color = Colors.Black
                }
            };
            TextOptions.SetTextFormattingMode(border, TextFormattingMode.Ideal);
            return border;
        }

        /// <summary>Wrap the card in a ToolTip with the default chrome stripped off.</summary>
        public static ToolTip Tip(string text, int metaFrom)
        {
            return new ToolTip
            {
                Content = Build(text, metaFrom),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HasDropShadow = false,          // the Border draws its own
                Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
                HorizontalOffset = 2,
                VerticalOffset = 18
            };
        }

        // ---------- the tray's stand-alone card ----------

        sealed class CardWindow : Window
        {
            protected override void OnSourceInitialized(EventArgs e)
            {
                base.OnSourceInitialized(e);
                IntPtr h = new WindowInteropHelper(this).Handle;
                Native.AddExStyle(h, Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW |
                                     Native.WS_EX_TRANSPARENT);
                Native.RemoveExStyle(h, Native.WS_EX_APPWINDOW);
            }
        }

        static Window _tray;

        /// <summary>
        /// Show the tray card near a point in physical screen pixels, nudged so it never hangs off
        /// the monitor it landed on.
        /// </summary>
        public static void ShowAt(string text, int deviceX, int deviceY, int metaFrom)
        {
            try
            {
                if (_tray == null)
                {
                    _tray = new CardWindow
                    {
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = Brushes.Transparent,
                        ShowActivated = false,
                        Focusable = false,
                        ShowInTaskbar = false,
                        Topmost = true,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        ResizeMode = ResizeMode.NoResize,
                        // Room for the drop shadow, which is drawn outside the border.
                        Margin = new Thickness(0)
                    };
                }

                _tray.Content = Build(text, metaFrom);
                if (!_tray.IsVisible) _tray.Show();
                _tray.UpdateLayout();

                Native.RECT r;
                int w = 240, h = 80;
                if (Native.GetWindowRect(new WindowInteropHelper(_tray).Handle, out r) && r.Width > 0)
                {
                    w = r.Width;
                    h = r.Height;
                }

                // The tray lives at a screen corner, so sit the card clear of the cursor and then
                // clamp: guessing a side would put it off-screen on three of the four taskbar
                // positions.
                System.Drawing.Rectangle wa =
                    System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(deviceX, deviceY)).WorkingArea;

                int x = deviceX - w / 2;
                int y = deviceY - h - 14;
                if (x + w > wa.Right) x = wa.Right - w;
                if (x < wa.Left) x = wa.Left;
                if (y < wa.Top) y = deviceY + 22;           // no room above: drop below the cursor
                if (y + h > wa.Bottom) y = wa.Bottom - h;

                Native.SetWindowPos(new WindowInteropHelper(_tray).Handle, IntPtr.Zero,
                                    x, y, 0, 0, Native.SWP_MOVE_ONLY);
            }
            catch (Exception e) { Log.Write("hover card failed: " + e.Message); }
        }

        public static void HideTray()
        {
            try { if (_tray != null && _tray.IsVisible) _tray.Hide(); }
            catch { }
        }

        public static void Close()
        {
            try { if (_tray != null) { _tray.Close(); _tray = null; } }
            catch { }
        }
    }
}
