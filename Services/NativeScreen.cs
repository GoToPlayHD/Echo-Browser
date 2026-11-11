using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Win32-Hilfen für das Ziehen von Tabs über Fenster- und Monitorgrenzen. Alle Koordinaten sind physische
    /// Bildschirm-Pixel – dieselben, die WPF bei PointToScreen/PointFromScreen verwendet.
    /// </summary>
    internal static class NativeScreen
    {
        /// <summary>Mauszeiger in Bildschirm-Pixeln (unabhängig davon, welches Fenster die Maus gerade hat).</summary>
        public static Point CursorPosition()
        {
            return GetCursorPos(out var point) ? new Point(point.X, point.Y) : new Point(double.NaN, double.NaN);
        }

        /// <summary>Oberstes Hauptfenster unter dem Punkt (durchklickbare Fenster wie die Drag-Vorschau zählen nicht).</summary>
        public static IntPtr TopLevelWindowAt(Point screen)
        {
            var child = WindowFromPoint(new POINT { X = (int)Math.Round(screen.X), Y = (int)Math.Round(screen.Y) });
            return child == IntPtr.Zero ? IntPtr.Zero : GetAncestor(child, GaRoot);
        }

        /// <summary>Arbeitsbereich (ohne Taskleiste) und Skalierung des Monitors unter dem Punkt.</summary>
        public static (Rect WorkArea, double Scale) MonitorAt(Point screen)
        {
            var monitor = MonitorFromPoint(new POINT { X = (int)Math.Round(screen.X), Y = (int)Math.Round(screen.Y) }, MonitorDefaultToNearest);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            Rect work = GetMonitorInfo(monitor, ref info)
                ? new Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top)
                : SystemParameters.WorkArea;

            double scale = 1.0;
            try
            {
                // Liefert je nach DPI-Modus des Prozesses die echte oder die System-Skalierung – passend zu den Koordinaten
                if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint dpiX, out _) == 0 && dpiX > 0) scale = dpiX / 96.0;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Sehr altes Windows: 100 % annehmen
            }
            return (work, scale);
        }

        public static Rect WindowBounds(IntPtr hwnd) =>
            GetWindowRect(hwnd, out var r) ? new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : Rect.Empty;

        /// <summary>Fenster in Pixeln verschieben (Größe bleibt), ohne es zu aktivieren.</summary>
        public static void MoveWindow(IntPtr hwnd, Point topLeft) =>
            SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y), 0, 0,
                SwpNoSize | SwpNoZOrder | SwpNoActivate);

        /// <summary>Drag-Vorschau: durchklickbar, ohne Taskleisten-Eintrag und ohne den Fokus zu nehmen.</summary>
        public static void MakeClickThroughToolWindow(IntPtr hwnd)
        {
            int style = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExLayered);
        }

        /// <summary>Esc gedrückt? Wirkt auch, wenn die Webseite den Tastaturfokus hat und WPF keine Taste sieht.</summary>
        public static bool IsEscapePressed() => (GetAsyncKeyState(VkEscape) & 0x8000) != 0;

        /// <summary>Ist die (logische) linke Maustaste noch gedrückt? Berücksichtigt vertauschte Tasten.</summary>
        public static bool IsPrimaryButtonPressed()
        {
            int key = GetSystemMetrics(SmSwapButton) != 0 ? VkRButton : VkLButton;
            return (GetAsyncKeyState(key) & 0x8000) != 0;
        }

        private const uint MonitorDefaultToNearest = 2;
        private const int MdtEffectiveDpi = 0;
        private const uint GaRoot = 2;
        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExLayered = 0x80000, WsExNoActivate = 0x08000000;
        private const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
        private const int VkLButton = 0x01, VkRButton = 0x02, VkEscape = 0x1B;
        private const int SmSwapButton = 23;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    }
}
