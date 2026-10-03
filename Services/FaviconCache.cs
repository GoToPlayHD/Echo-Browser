using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Website-Symbole für Lesezeichen, Verlauf und Tabs. Ein Symbol pro Host, im Speicher und als PNG unter
    /// %LOCALAPPDATA%\EchoBrowser\Favicons. Muss auf dem UI-Thread benutzt werden.
    /// </summary>
    public sealed class FaviconCache
    {
        private static FaviconCache? _instance;
        public static FaviconCache Instance => _instance ??= new FaviconCache();

        private readonly Dictionary<string, ImageSource?> _memory = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _folder = Path.Combine(AppPaths.DataFolder, "Favicons");

        /// <summary>Ein Symbol wurde neu gespeichert (Parameter: Host-Schlüssel, siehe <see cref="HostKey"/>).</summary>
        public event Action<string>? Updated;

        private FaviconCache() { }

        /// <summary>"https://www.github.com/x" -> "github.com". Null für Adressen ohne Host.</summary>
        public static string? HostKey(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

            string host = uri.Host.ToLowerInvariant();
            return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
        }

        public ImageSource? Get(string? url)
        {
            string? key = HostKey(url);
            if (key == null) return null;
            if (_memory.TryGetValue(key, out var cached)) return cached;

            ImageSource? image = null;
            string file = FileFor(key);
            try
            {
                if (File.Exists(file))
                {
                    image = Decode(File.ReadAllBytes(file));
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Favicon {key} konnte nicht gelesen werden", ex);
            }

            _memory[key] = image;
            return image;
        }

        /// <summary>Gespeichertes PNG einer Website ("github.com" oder eine Adresse) oder null.</summary>
        public byte[]? ReadPng(string hostOrUrl)
        {
            string key = hostOrUrl.Contains("://", StringComparison.Ordinal)
                ? HostKey(hostOrUrl) ?? ""
                : hostOrUrl.Trim().ToLowerInvariant();
            if (key.StartsWith("www.", StringComparison.Ordinal)) key = key[4..];
            if (key.Length == 0) return null;

            string file = FileFor(key);
            try
            {
                return File.Exists(file) ? File.ReadAllBytes(file) : null;
            }
            catch (Exception ex)
            {
                Log.Warn($"Favicon {key} konnte nicht gelesen werden", ex);
                return null;
            }
        }

        /// <summary>Neues Symbol für die Website merken und auf der Festplatte ablegen.</summary>
        public ImageSource? Store(string url, byte[] png)
        {
            string? key = HostKey(url);
            var image = Decode(png);
            if (key == null || image == null) return image;

            _memory[key] = image;
            string file = FileFor(key);
            _ = Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(_folder);
                    File.WriteAllBytes(file, png);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Favicon {key} konnte nicht gespeichert werden", ex);
                }
            });

            Updated?.Invoke(key);
            return image;
        }

        /// <summary>PNG-Bytes in ein (eingefrorenes) Bild umwandeln; null bei leeren oder kaputten Daten.</summary>
        public static ImageSource? Decode(byte[]? png)
        {
            if (png == null || png.Length == 0) return null;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(png);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 32;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private string FileFor(string key)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                key = key.Replace(c, '_');
            }
            return Path.Combine(_folder, key + ".png");
        }
    }
}
