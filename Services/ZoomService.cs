using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EchoBrowser.Services
{
    /// <summary>Zoomstufen wie in Chrome (Strg+Plus/Minus springt zur nächsten Stufe).</summary>
    public static class ZoomLevels
    {
        public static readonly double[] Steps =
            { 0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 4.0, 5.0 };

        public const double Min = 0.25;
        public const double Max = 5.0;

        /// <summary>Nächste Stufe in Richtung +1 (größer) bzw. -1 (kleiner); bleibt an den Grenzen stehen.</summary>
        public static double Next(double current, int direction)
        {
            const double epsilon = 0.001;
            return direction > 0
                ? Steps.FirstOrDefault(s => s > current + epsilon, Max)
                : Steps.LastOrDefault(s => s < current - epsilon, Min);
        }

        /// <summary>"110 %" – gerundet wie in der Anzeige von Chrome.</summary>
        public static string Format(double factor) => $"{Math.Round(factor * 100)} %";

        public static bool AreEqual(double a, double b) => Math.Abs(a - b) < 0.001;
    }

    /// <summary>
    /// Merkt sich den Zoom pro Website (Host) in zoom.json. Websites mit Standard-Zoom werden nicht gespeichert.
    /// </summary>
    public sealed class ZoomService
    {
        private static ZoomService? _instance;
        public static ZoomService Instance => _instance ??= new ZoomService(AppPaths.File("zoom.json"));

        private readonly string _filePath;
        private readonly Dictionary<string, double> _levels;

        public ZoomService(string filePath)
        {
            _filePath = filePath;
            _levels = Load(filePath);
        }

        /// <summary>Standard-Zoom aus den Einstellungen (100 % = 1.0).</summary>
        public static double DefaultFactor =>
            Math.Clamp(AppSettingsService.Instance.Settings.DefaultZoomPercent / 100.0, ZoomLevels.Min, ZoomLevels.Max);

        /// <summary>Gespeicherter Zoom für die Website oder null (dann gilt der Standard).</summary>
        public double? Get(string? url)
        {
            string? host = HostOf(url);
            return host != null && _levels.TryGetValue(host, out double factor) ? factor : null;
        }

        /// <summary>Zoom für die Website merken; entspricht er dem Standard, wird der Eintrag entfernt.</summary>
        public void Set(string? url, double factor, double defaultFactor)
        {
            string? host = HostOf(url);
            if (host == null) return;

            bool changed = ZoomLevels.AreEqual(factor, defaultFactor)
                ? _levels.Remove(host)
                : !_levels.TryGetValue(host, out double old) || !ZoomLevels.AreEqual(old, factor);

            if (!ZoomLevels.AreEqual(factor, defaultFactor))
            {
                _levels[host] = Math.Clamp(factor, ZoomLevels.Min, ZoomLevels.Max);
            }

            if (changed)
            {
                Save();
            }
        }

        /// <summary>Nur echte Websites (http/https) bekommen einen eigenen Zoom.</summary>
        public static string? HostOf(string? url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
            return string.IsNullOrEmpty(uri.Host) ? null : uri.Host.ToLowerInvariant();
        }

        private static Dictionary<string, double> Load(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var stored = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(filePath));
                    if (stored != null)
                    {
                        return new Dictionary<string, double>(stored, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("zoom.json konnte nicht gelesen werden", ex);
            }
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        private void Save()
        {
            try
            {
                AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(_levels));
            }
            catch (Exception ex)
            {
                Log.Warn("zoom.json konnte nicht gespeichert werden", ex);
            }
        }
    }
}
