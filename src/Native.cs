// One auditable interop surface. Declarations and thin helpers only - no policy lives here.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Vibespan
{
    public static class Native
    {
        // ---------- structs ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
            public bool Contains(int x, int y) { return x >= Left && x < Right && y >= Top && y < Bottom; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public uint dwFlags;
        }

        // ---------- window ----------
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr h, StringBuilder buf, int count);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);

        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);

        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

        // ---------- UIPI ----------
        // A uiAccess process launched by a member of Administrators runs at High IL, and UIPI
        // then drops messages sent from medium-IL Explorer. Two of those matter to the tray:
        // the WM_USER+1024 NotifyIcon callback, and the RegisterWindowMessage("TaskbarCreated")
        // broadcast that tells us to re-add the icon after an Explorer restart. Without the
        // filters the icon silently stops responding, and vanishes for good on a shell restart.
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ChangeWindowMessageFilter(uint message, uint flag);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string name);

        // ---------- accessibility event hook ----------
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd,
                                          int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmodWinEventProc,
                                                    WinEventProc proc, uint idProcess, uint idThread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);

        // ---------- shell ----------
        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);

        // ---------- token / integrity ----------
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr info, int len, out int ret);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);

        // ---------- constants ----------
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
        public const uint SWP_TOPMOST_FLAGS = SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE;

        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const uint GW_OWNER = 4;

        public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;

        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_DISPLAYCHANGE = 0x007E;
        public const int WM_WINDOWPOSCHANGED = 0x0047;
        public const int WM_DPICHANGED = 0x02E0;
        public const uint WM_NULL = 0x0000;

        public const int MA_ACTIVATE = 1, MA_NOACTIVATE = 3;

        public const uint MONITOR_DEFAULTTONEAREST = 2;

        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        public const uint MSGFLT_ADD = 1;

        // QUERY_USER_NOTIFICATION_STATE
        public const int QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

        const uint TOKEN_QUERY = 0x0008;
        const int TokenUIAccess = 26;

        // ---------- helpers ----------
        public static void SetTopmost(IntPtr h, bool on)
        {
            SetWindowPos(h, on ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_TOPMOST_FLAGS);
        }

        /// <summary>Kick the window back to the top of its band. Cheap; safe to repeat.</summary>
        public static void ReassertTopmost(IntPtr h)
        {
            SetWindowPos(h, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_TOPMOST_FLAGS);
            SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_TOPMOST_FLAGS);
        }

        public static void AddExStyle(IntPtr h, int bits)
        {
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | bits);
        }
        public static void RemoveExStyle(IntPtr h, int bits)
        {
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) & ~bits);
        }

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(80);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool TryMonitorRect(IntPtr h, out RECT monitorRect)
        {
            monitorRect = new RECT();
            IntPtr mon = MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero) return false;
            var mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(mon, ref mi)) return false;
            monitorRect = mi.rcMonitor;
            return true;
        }

        /// <summary>
        /// Is this process actually running with uiAccess? If the signature was invalidated or
        /// the exe moved out of Program Files, Windows refuses the flag *silently* and the
        /// widget just starts drawing under the taskbar. Worth logging rather than guessing.
        /// </summary>
        public static bool HasUiAccess()
        {
            IntPtr token = IntPtr.Zero;
            IntPtr buf = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out token)) return false;
                buf = Marshal.AllocHGlobal(sizeof(int));
                int ret;
                if (!GetTokenInformation(token, TokenUIAccess, buf, sizeof(int), out ret)) return false;
                return Marshal.ReadInt32(buf) != 0;
            }
            catch { return false; }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }
    }
}
