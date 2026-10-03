using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Windows-11-Fensteroptik über DWM: Mica-Hintergrund ("Mica Alt" wie in Edge-Tableisten),
    /// dunkle bzw. helle Fensterrahmen passend zum Theme und abgerundete Ecken.
    /// Unter Windows 10 bleiben die Aufrufe ohne Wirkung – das Fenster ist dann einfach deckend.
    /// </summary>
    public static class WindowEffects
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaSystemBackdropType = 38;

        private const int DwmwcpRound = 2;
        private const int DwmsbtNone = 1;
        private const int DwmsbtTabbedWindow = 4; // "Mica Alt"

        /// <summary>Mica gibt es ab Windows 11 22H2 (Build 22621) über DWMWA_SYSTEMBACKDROP_TYPE.</summary>
        public static bool IsMicaSupported => Environment.OSVersion.Version.Build >= 22621;

        /// <summary>
        /// Rahmenfarbe (hell/dunkel), runde Ecken und optional Mica anwenden. Gibt zurück, ob Mica aktiv ist –
        /// dann muss der Aufrufer die Flächen, durch die Mica scheinen soll, durchsichtig machen.
        /// </summary>
        public static bool Apply(Window window, bool isDark, bool useMica)
        {
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            if (hwnd == IntPtr.Zero) return false;

            int dark = isDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            int corner = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));

            bool mica = useMica && IsMicaSupported;
            int backdrop = mica ? DwmsbtTabbedWindow : DwmsbtNone;
            int result = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
            mica &= result == 0;

            // Ohne durchsichtigen Hintergrund des WPF-Renderziels sähe man vom Mica-Effekt nichts
            if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
            {
                target.BackgroundColor = mica ? Colors.Transparent : SystemColors.WindowColor;
            }

            return mica;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
