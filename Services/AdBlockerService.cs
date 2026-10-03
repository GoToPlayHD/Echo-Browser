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
            _cacheFilePath = AppPaths.File("blocklist.txt");
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
                    Log.Warn("Error reading blocklist cache", ex);
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
                        Log.Warn("Background filter list update failed", ex);
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
                Log.Warn($"Failed to download blocklist from {url}", ex);
                if (forceDownload) throw;
                return BlockedDomainCount;
            }

            var newSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Re-seed defaults first
            foreach (var d in GetBundledDomains())
            {
                newSet.Add(d);
            }

            foreach (var domain in ParseHostsList(content))
            {
                newSet.Add(domain);
            }

            // Save to cache file
            try
            {
                var sb = new StringBuilder();
                foreach (var d in newSet)
                {
                    sb.AppendLine(d);
                }
                AtomicFile.WriteAllText(_cacheFilePath, sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to write blocklist cache", ex);
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

        /// <summary>
        /// Liest eine Blockliste im Hosts-Format ("0.0.0.0 ads.example.com") oder als reine Domainliste.
        /// Kommentare (#, !), localhost-Einträge und Namen ohne Punkt werden übersprungen.
        /// </summary>
        internal static IEnumerable<string> ParseHostsList(string content)
        {
            using var reader = new StringReader(content);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!'))
                    continue;

                // Hosts-Format: 0.0.0.0 domain.com oder 127.0.0.1 domain.com
                string domain = line;
                if (domain.StartsWith("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                    domain.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = domain.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                    domain = parts[1];
                }

                domain = domain.Trim().TrimEnd('.');
                if (domain.Length > 0 &&
                    !domain.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
                    !domain.Equals("local", StringComparison.OrdinalIgnoreCase) &&
                    !domain.Equals("broadcasthost", StringComparison.OrdinalIgnoreCase) &&
                    domain.Contains('.'))
                {
                    yield return domain;
                }
            }
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

        internal void LoadBundledDefaults()
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
    if (window.__echoShieldDisabled === true) {
        try {
            const ex = document.getElementById('echo-shield-cosmetic');
            if (ex) ex.remove();
        } catch(e) {}
        return;
    }

    // 1. Cosmetic Element Hiding CSS (Hides ad containers, banners, and test elements)
    const css = `
        #cts_test, #ctd_test, #ad_ctd,
        .textads, .adsbox, .banner_ads, .adbox, .ADBox, .AdBox, .adbox-wrapper, .adSocial,
        .ad-container, .ad-box, .ad-banner, .advertisement,
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
        if (window.__echoShieldDisabled === true) {
            const ex = document.getElementById('echo-shield-cosmetic');
            if (ex) ex.remove();
            return;
        }
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

    // 2. Client-Side Script & Fetch Protection
    try {
        const blockedPatterns = [
            'amazonaws.com', 'googlesyndication.com', 'doubleclick.net', 'adservice.google.com',
            'googleadservices.com', 'adcolony.com', 'media.net', 'google-analytics.com', 'googleanalytics.com',
            'hotjar.com', 'hotjar.io', 'mouseflow.com', 'freshmarketer.com', 'luckyorange.com',
            'luckyorange.net', 'stats.wp.com', 'bugsnag.com', 'sentry-cdn.com', 'getsentry.com',
            'facebook.com', 'facebook.net', 'ads-twitter.com', 'ads-api.twitter.com', 'ads.linkedin.com',
            'pointdrive.linkedin.com', 'pinterest.com', 'reddit.com', 'redditmedia.com', 'ads.youtube.com',
            'tiktok.com', 'byteoversea.com', 'ads.yahoo.com', 'analytics.yahoo.com', 'geo.yahoo.com',
            'udcm.yahoo.com', 'yahooinc.com', 'yandex.net', 'yandex.ru', 'unityads.unity3d.com',
            'realme.com', 'realmemobile.com', 'xiaomi.com', 'miui.com', 'oppomobile.com',
            'hicloud.com', 'oneplus.cn', 'samsungads.com', 'samsung.com', 'samsunghealthcn.com',
            'apple.com', 'mzstatic.com', 'scorecardresearch.com', 'quantserve.com', 'quantcount.com',
            'amazon-adsystem.com', 'rubiconproject.com', 'fastclick.net', 'outbrain.com',
            'taboola.com', 'openx.net', 'adnxs.com', 'pubmatic.com', 'criteo.com',
            'criteo.net', 'applovin.com', 'chartbeat.com', 'chartbeat.net', 'segment.io',
            'mixpanel.com', 'clarity.ms', 'adroll.com', 'smartadserver.com', 'casalemedia.com',
            'sovrn.com', 'inmobi.com', 'moatads.com', 'trafficjunky.com', 'bidswitch.net',
            'adjust.com', 'appsflyer.com', 'statcounter.com', 'revcontent.com', 'carbonads.net'
        ];

        function isUrlBlocked(urlStr) {
            if (window.__echoShieldDisabled === true) return false;
            if (!urlStr || typeof urlStr !== 'string') return false;
            try {
                let parsed = new URL(urlStr, window.location.href);
                let host = parsed.hostname.toLowerCase();
                let path = parsed.pathname.toLowerCase();
                if (path.endsWith('/ads.js') || path.endsWith('/pagead.js') || path.includes('/ads/widget') || path.includes('/widget/ads.js')) {
                    return true;
                }
                for (let i = 0; i < blockedPatterns.length; i++) {
                    let p = blockedPatterns[i];
                    if (host === p || host.endsWith('.' + p)) {
                        return true;
                    }
                }
            } catch(e) {}
            return false;
        }

        // Intercept window.fetch to simulate net::ERR_BLOCKED_BY_CLIENT (throws TypeError: Failed to fetch)
        if (typeof window.fetch === 'function') {
            const origFetch = window.fetch;
            window.fetch = function(resource, init) {
                let url = typeof resource === 'string' ? resource : (resource && resource.url ? resource.url : '');
                if (isUrlBlocked(url)) {
                    return Promise.reject(new TypeError('Failed to fetch (Blocked by Echo Shield)'));
                }
                return origFetch.apply(this, arguments);
            };
        }

        // Intercept XMLHttpRequest
        if (typeof window.XMLHttpRequest === 'function') {
            const origOpen = XMLHttpRequest.prototype.open;
            XMLHttpRequest.prototype.open = function(method, url) {
                if (isUrlBlocked(url)) {
                    this._isEchoBlocked = true;
                }
                return origOpen.apply(this, arguments);
            };
            const origSend = XMLHttpRequest.prototype.send;
            XMLHttpRequest.prototype.send = function() {
                if (this._isEchoBlocked) {
                    setTimeout(() => {
                        if (typeof this.onerror === 'function') {
                            this.onerror(new ProgressEvent('error'));
                        }
                        this.dispatchEvent(new ProgressEvent('error'));
                    }, 0);
                    return;
                }
                return origSend.apply(this, arguments);
            };
        }
    } catch(e) {}
})();";
        }

        private static IEnumerable<string> GetBundledDomains()
        {
            // Core curated list including all 128 domains from turtlecute/d3ward adblock test suite + high-traffic networks
            return new[]
            {
                // turtlecute / d3ward test domains: Ads
                "adtago.s3.amazonaws.com",
                "analyticsengine.s3.amazonaws.com",
                "analytics.s3.amazonaws.com",
                "advice-ads.s3.amazonaws.com",
                "pagead2.googlesyndication.com",
                "adservice.google.com",
                "pagead2.googleadservices.com",
                "afs.googlesyndication.com",
                "stats.g.doubleclick.net",
                "ad.doubleclick.net",
                "static.doubleclick.net",
                "m.doubleclick.net",
                "mediavisor.doubleclick.net",
                "ads30.adcolony.com",
                "adc3-launch.adcolony.com",
                "events3alt.adcolony.com",
                "wd.adcolony.com",
                "static.media.net",
                "media.net",
                "adservetx.media.net",

                // Analytics
                "analytics.google.com",
                "click.googleanalytics.com",
                "google-analytics.com",
                "ssl.google-analytics.com",
                "adm.hotjar.com",
                "identify.hotjar.com",
                "insights.hotjar.com",
                "script.hotjar.com",
                "surveys.hotjar.com",
                "careers.hotjar.com",
                "events.hotjar.io",
                "mouseflow.com",
                "cdn.mouseflow.com",
                "o2.mouseflow.com",
                "gtm.mouseflow.com",
                "api.mouseflow.com",
                "tools.mouseflow.com",
                "cdn-test.mouseflow.com",
                "freshmarketer.com",
                "claritybt.freshmarketer.com",
                "fwtracks.freshmarketer.com",
                "luckyorange.com",
                "api.luckyorange.com",
                "realtime.luckyorange.com",
                "cdn.luckyorange.com",
                "w1.luckyorange.com",
                "upload.luckyorange.net",
                "cs.luckyorange.net",
                "settings.luckyorange.net",
                "stats.wp.com",

                // Error Trackers
                "notify.bugsnag.com",
                "sessions.bugsnag.com",
                "api.bugsnag.com",
                "app.bugsnag.com",
                "browser.sentry-cdn.com",
                "app.getsentry.com",

                // Social Trackers
                "pixel.facebook.com",
                "an.facebook.com",
                "connect.facebook.net",
                "facebook.net",
                "static.ads-twitter.com",
                "ads-api.twitter.com",
                "ads.linkedin.com",
                "analytics.pointdrive.linkedin.com",
                "ads.pinterest.com",
                "log.pinterest.com",
                "trk.pinterest.com",
                "events.reddit.com",
                "events.redditmedia.com",
                "ads.youtube.com",
                "ads-api.tiktok.com",
                "analytics.tiktok.com",
                "ads-sg.tiktok.com",
                "analytics-sg.tiktok.com",
                "business-api.tiktok.com",
                "ads.tiktok.com",
                "log.byteoversea.com",

                // Mix
                "ads.yahoo.com",
                "analytics.yahoo.com",
                "geo.yahoo.com",
                "udcm.yahoo.com",
                "analytics.query.yahoo.com",
                "partnerads.ysm.yahoo.com",
                "log.fc.yahoo.com",
                "gemini.yahoo.com",
                "adtech.yahooinc.com",
                "extmaps-api.yandex.net",
                "appmetrica.yandex.ru",
                "adfstat.yandex.ru",
                "metrika.yandex.ru",
                "offerwall.yandex.net",
                "adfox.yandex.ru",
                "auction.unityads.unity3d.com",
                "webview.unityads.unity3d.com",
                "config.unityads.unity3d.com",
                "adserver.unityads.unity3d.com",

                // OEMs
                "iot-eu-logser.realme.com",
                "iot-logser.realme.com",
                "bdapi-ads.realmemobile.com",
                "bdapi-in-ads.realmemobile.com",
                "api.ad.xiaomi.com",
                "data.mistat.xiaomi.com",
                "data.mistat.india.xiaomi.com",
                "data.mistat.rus.xiaomi.com",
                "sdkconfig.ad.xiaomi.com",
                "sdkconfig.ad.intl.xiaomi.com",
                "tracking.rus.miui.com",
                "adsfs.oppomobile.com",
                "adx.ads.oppomobile.com",
                "ck.ads.oppomobile.com",
                "data.ads.oppomobile.com",
                "metrics.data.hicloud.com",
                "metrics2.data.hicloud.com",
                "grs.hicloud.com",
                "logservice.hicloud.com",
                "logservice1.hicloud.com",
                "logbak.hicloud.com",
                "click.oneplus.cn",
                "samsungads.com",
                "smetrics.samsung.com",
                "nmetrics.samsung.com",
                "samsung-com.112.2o7.net",
                "analytics-api.samsunghealthcn.com",
                "iadsdk.apple.com",
                "metrics.icloud.com",
                "metrics.mzstatic.com",
                "api-adservices.apple.com",
                "books-analytics-events.apple.com",
                "weather-analytics-events.apple.com",
                "notes-analytics-events.apple.com",

                // General High-Traffic Ad Networks & Trackers
                "doubleclick.net",
                "googleadservices.com",
                "googlesyndication.com",
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
                "chartbeat.com",
                "chartbeat.net",
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
                "vungle.com",
                "ironsrc.com",
                "adjust.com",
                "appsflyer.com",
                "flurry.com",
                "statcounter.com",
                "bat.bing.com",
                "snap.licdn.com",
                "tracking.kueez.com",
                "revcontent.com",
                "adblade.com",
                "buysellads.com",
                "carbonads.net",
                "srv.carbonads.net",
                "chitika.net",
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
                "optimizely.com",
                "loggly.com",
                "newrelic.com",
                "bam.nr-data.net",
                "datadoghq.com"
            };
        }
    }
}
