using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EchoBrowser.Services
{
    public class AdBlockerService
    {
        private static AdBlockerService? _instance;
        public static AdBlockerService Instance => _instance ??= new AdBlockerService();

        private readonly HashSet<string> _blockedDomains = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private readonly string _cacheFilePath;
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

        public bool IsInitialized { get; private set; }
        public int BlockedDomainCount
        {
            get
            {
                lock (_lock)
                {
                    return _blockedDomains.Count;
                }
            }
        }
        public int BlockedDomainsCount => BlockedDomainCount;

        public Task<int> DownloadAndCacheBlocklistAsync(string? filterUrl = null, bool force = true)
        {
            if (!string.IsNullOrWhiteSpace(filterUrl))
            {
                AppSettingsService.Instance.Settings.AdBlockerFilterUrl = filterUrl;
            }
            return UpdateFilterListAsync(force);
        }

        public event Action? FilterListUpdated;

        private AdBlockerService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "EchoBrowser");
            Directory.CreateDirectory(folder);
            _cacheFilePath = Path.Combine(folder, "blocklist.txt");
        }

        public async Task InitializeAsync()
        {
            if (IsInitialized) return;

            // 1. Load bundled fallback domains immediately
            LoadBundledDefaults();

            // 2. Load cached list if available
            bool cacheExists = File.Exists(_cacheFilePath);
            if (cacheExists)
            {
                try
                {
                    LoadFromFile(_cacheFilePath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading blocklist cache: {ex.Message}");
                }
            }

            IsInitialized = true;
            FilterListUpdated?.Invoke();

            // 3. If cache doesn't exist or is older than 7 days, update in background
            bool shouldUpdate = !cacheExists;
            if (cacheExists)
            {
                try
                {
                    var fileAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(_cacheFilePath);
                    if (fileAge.TotalDays >= 7)
                    {
                        shouldUpdate = true;
                    }
                }
                catch { }
            }

            if (shouldUpdate)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await UpdateFilterListAsync(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Background filter list update failed: {ex.Message}");
                    }
                });
            }
        }

        public bool IsBlocked(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;

            // Normalize host
            string cleanHost = host.Trim().TrimEnd('.');
            if (cleanHost.Length == 0) return false;

            // Direct check
            lock (_lock)
            {
                if (_blockedDomains.Contains(cleanHost))
                    return true;
            }

            // Recursive subdomain check (e.g. ads.example.com -> example.com)
            int dotIndex = cleanHost.IndexOf('.');
            while (dotIndex > 0 && dotIndex < cleanHost.Length - 1)
            {
                cleanHost = cleanHost.Substring(dotIndex + 1);
                // Don't check top-level domain alone (e.g. "com", "org", "de")
                if (!cleanHost.Contains('.')) break;

                lock (_lock)
                {
                    if (_blockedDomains.Contains(cleanHost))
                        return true;
                }

                dotIndex = cleanHost.IndexOf('.');
            }

            return false;
        }

        public async Task<int> UpdateFilterListAsync(bool forceDownload = true)
        {
            string url = AppSettingsService.Instance.Settings.AdBlockerFilterUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts";
            }

            string content;
            try
            {
                content = await _httpClient.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to download blocklist from {url}: {ex.Message}");
                if (forceDownload) throw;
                return BlockedDomainCount;
            }

            var newSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Re-seed defaults first
            foreach (var d in GetBundledDomains())
            {
                newSet.Add(d);
            }

            using (var reader = new StringReader(content))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith('#') || line.StartsWith('!'))
                        continue;

                    // Parse hosts file format: 0.0.0.0 domain.com or 127.0.0.1 domain.com
                    string domain = line;
                    if (domain.StartsWith("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                        domain.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = domain.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            domain = parts[1];
                        }
                        else
                        {
                            continue;
                        }
                    }

                    domain = domain.Trim().TrimEnd('.');
                    if (domain.Length > 0 &&
                        !domain.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
                        !domain.Equals("local", StringComparison.OrdinalIgnoreCase) &&
                        !domain.Equals("broadcasthost", StringComparison.OrdinalIgnoreCase) &&
                        domain.Contains('.'))
                    {
                        newSet.Add(domain);
                    }
                }
            }

            // Save to cache file
            try
            {
                var sb = new StringBuilder();
                foreach (var d in newSet)
                {
                    sb.AppendLine(d);
                }
                File.WriteAllText(_cacheFilePath, sb.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to write blocklist cache: {ex.Message}");
            }

            lock (_lock)
            {
                _blockedDomains.Clear();
                foreach (var d in newSet)
                {
                    _blockedDomains.Add(d);
                }
            }

            FilterListUpdated?.Invoke();
            return newSet.Count;
        }

        private void LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return;

            var newDomains = new List<string>(50000);
            foreach (var line in File.ReadLines(filePath))
            {
                string d = line.Trim().TrimEnd('.');
                if (d.Length > 0 && !d.StartsWith('#') && d.Contains('.'))
                {
                    newDomains.Add(d);
                }
            }

            lock (_lock)
            {
                foreach (var d in newDomains)
                {
                    _blockedDomains.Add(d);
                }
            }
        }

        private void LoadBundledDefaults()
        {
            lock (_lock)
            {
                foreach (var d in GetBundledDomains())
                {
                    _blockedDomains.Add(d);
                }
            }
        }

        public string GetCosmeticScript()
        {
            return @"(function() {
    const css = `
        .ad-container, .adsbox, .ad-box, .ad-banner, .advertisement,
        [id^='google_ads'], [id^='div-gpt-ad'], [class*='adsbygoogle'],
        .taboola, .outbrain, .sponsor-box, [data-ad-unit],
        [aria-label='advertisement'], .ad_wrapper, .ad-placeholder,
        .ads-area, .ad-slot, .ad-unit, .advert, .advert-box,
        #header-ad, #footer-ad, #sidebar-ad, .ad-sidebar,
        [class*='sponsored-post'], [class*='promoted-item'],
        .sponsored-content, .ad-leaderboard, .ad-rectangle,
        [id^='ad_'], [id$='_ad'], [class^='ad-'], [class$='-ad'] {
            display: none !important;
            visibility: hidden !important;
            height: 0 !important;
            min-height: 0 !important;
            opacity: 0 !important;
            pointer-events: none !important;
        }
    `;

    function inject() {
        if (document.getElementById('echo-shield-cosmetic')) return;
        const style = document.createElement('style');
        style.id = 'echo-shield-cosmetic';
        style.type = 'text/css';
        style.textContent = css;
        const target = document.head || document.documentElement;
        if (target) {
            target.appendChild(style);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', inject);
    } else {
        inject();
    }
})();";
        }

        private static IEnumerable<string> GetBundledDomains()
        {
            // Core curated list of high-traffic ad networks, tracking platforms, and telemetrics
            return new[]
            {
                "doubleclick.net",
                "googleadservices.com",
                "googlesyndication.com",
                "adservice.google.com",
                "pagead2.googlesyndication.com",
                "adclick.g.doubleclick.net",
                "stats.g.doubleclick.net",
                "google-analytics.com",
                "analytics.google.com",
                "ssl.google-analytics.com",
                "facebook.net",
                "connect.facebook.net",
                "pixel.facebook.com",
                "an.facebook.com",
                "scorecardresearch.com",
                "quantserve.com",
                "quantcount.com",
                "amazon-adsystem.com",
                "aax.amazon-adsystem.com",
                "c.amazon-adsystem.com",
                "fls-na.amazon.com",
                "rubiconproject.com",
                "fastclick.net",
                "outbrain.com",
                "widgets.outbrain.com",
                "taboola.com",
                "cdn.taboola.com",
                "trc.taboola.com",
                "openx.net",
                "adnxs.com",
                "ib.adnxs.com",
                "pubmatic.com",
                "ads.pubmatic.com",
                "criteo.com",
                "static.criteo.net",
                "applovin.com",
                "unityads.unity3d.com",
                "chartbeat.com",
                "chartbeat.net",
                "hotjar.com",
                "static.hotjar.com",
                "script.hotjar.com",
                "segment.io",
                "api.segment.io",
                "branch.io",
                "app.link",
                "mixpanel.com",
                "api.mixpanel.com",
                "clarity.ms",
                "c.clarity.ms",
                "adroll.com",
                "d.adroll.com",
                "smartadserver.com",
                "casalemedia.com",
                "lijit.com",
                "sovrn.com",
                "yieldmo.com",
                "spotxchange.com",
                "inmobi.com",
                "moatads.com",
                "serving-sys.com",
                "exponential.com",
                "trafficjunky.com",
                "zergnet.com",
                "bidswitch.net",
                "advertising.com",
                "adcolony.com",
                "vungle.com",
                "ironsrc.com",
                "adjust.com",
                "appsflyer.com",
                "flurry.com",
                "statcounter.com",
                "yandex.ru/metrika",
                "mc.yandex.ru",
                "an.yandex.ru",
                "bat.bing.com",
                "ads.linkedin.com",
                "snap.licdn.com",
                "ads-twitter.com",
                "static.ads-twitter.com",
                "ads.pinterest.com",
                "ct.pinterest.com",
                "ads.tiktok.com",
                "analytics.tiktok.com",
                "tracking.kueez.com",
                "revcontent.com",
                "adblade.com",
                "buysellads.com",
                "carbonads.net",
                "srv.carbonads.net",
                "chitika.net",
                "media.net",
                "contextweb.com",
                "adtechus.com",
                "tribalfusion.com",
                "yieldlove.com",
                "adform.net",
                "adsafeprotected.com",
                "iasds.com",
                "innovid.com",
                "mathtag.com",
                "rlcdn.com",
                "demdex.net",
                "omtrdc.net",
                "everesttech.net",
                "bluekai.com",
                "tags.bluekai.com",
                "agkn.com",
                "crwdcntrl.net",
                "tapad.com",
                "adtruth.com",
                "imrworldwide.com",
                "sensic.net",
                "clicktale.net",
                "crazyegg.com",
                "mouseflow.com",
                "optimizely.com",
                "loggly.com",
                "newrelic.com",
                "bam.nr-data.net",
                "bugsnag.com",
                "sentry.io",
                "datadoghq.com"
            };
        }
    }
}
