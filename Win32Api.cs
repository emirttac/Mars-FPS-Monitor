using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FPSOverlay
{
    public static class Win32Api
    {
        // --- ghost mode: layered + click-through ---
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        // --- who's the foreground window? ---
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // --- stay on TOP no matter what ---
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

        // QUERY_USER_NOTIFICATION_STATE (shellapi.h) — Focus Assist / Game Mode awareness
        public const int QUNS_NOT_PRESENT = 1;
        public const int QUNS_BUSY = 2;
        /// <summary>True exclusive D3D fullscreen — DWM skipped, WPF overlay invisible.</summary>
        public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        public const int QUNS_PRESENTATION_MODE = 4;
        public const int QUNS_ACCEPTS_NOTIFICATIONS = 5;
        public const int QUNS_QUIET_TIME = 6;
        public const int QUNS_APP = 7;

        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int pquns);

        /// <summary>
        /// Current user notification state via SHQueryUserNotificationState.
        /// Returns QUNS_ACCEPTS_NOTIFICATIONS when the query fails.
        /// </summary>
        public static int GetUserNotificationState()
        {
            try
            {
                if (SHQueryUserNotificationState(out int state) != 0)
                    return QUNS_ACCEPTS_NOTIFICATIONS;
                return state;
            }
            catch
            {
                return QUNS_ACCEPTS_NOTIFICATIONS;
            }
        }

        /// <summary>
        /// True when Windows is likely suppressing standard toasts
        /// (exclusive fullscreen, busy, presentation, quiet time).
        /// </summary>
        public static bool IsNotificationSuppressedState(int state)
            => state == QUNS_BUSY
               || state == QUNS_RUNNING_D3D_FULL_SCREEN
               || state == QUNS_PRESENTATION_MODE
               || state == QUNS_QUIET_TIME;

        public static bool IsExclusiveD3DFullscreen()
        {
            try
            {
                return GetUserNotificationState() == QUNS_RUNNING_D3D_FULL_SCREEN;
            }
            catch
            {
                return false;
            }
        }

        // --- hide companion windows (RTSS UI) ---
        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
        public const int SW_MINIMIZE = 6;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // --- game detection: window bounds vs primary monitor ---
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);
    }
}
