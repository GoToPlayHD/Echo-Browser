using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

public class AdBlockerServiceTests
{
    [Fact]
    public void ParseHostsList_ReadsHostsFormatAndPlainDomains()
    {
        const string content = """
            # Kommentar
            ! Adblock-Kommentar
            0.0.0.0 ads.example.com
            127.0.0.1	tracker.example.net
            plain-domain.org
            0.0.0.0 trailing-dot.com.

            """;

        var domains = AdBlockerService.ParseHostsList(content).ToList();

        Assert.Equal(new[] { "ads.example.com", "tracker.example.net", "plain-domain.org", "trailing-dot.com" }, domains);
    }

    [Fact]
    public void ParseHostsList_SkipsLocalhostAndNamesWithoutDot()
    {
        const string content = """
            127.0.0.1 localhost
            0.0.0.0 local
            255.255.255.255 broadcasthost
            0.0.0.0 0.0.0.0
            0.0.0.0
            intranet
            """;

        var domains = AdBlockerService.ParseHostsList(content).ToList();

        // "0.0.0.0 0.0.0.0" ergibt die Adresse selbst – die ist harmlos, aber alles andere muss raus
        Assert.DoesNotContain("localhost", domains);
        Assert.DoesNotContain("local", domains);
        Assert.DoesNotContain("broadcasthost", domains);
        Assert.DoesNotContain("intranet", domains);
    }

    [Fact]
    public void BundledDefaults_BlockKnownTrackerAndSubdomains()
    {
        var blocker = AdBlockerService.Instance;
        blocker.LoadBundledDefaults();

        Assert.True(blocker.IsBlocked("doubleclick.net"));
        Assert.True(blocker.IsBlocked("ad.doubleclick.net"));
        Assert.True(blocker.IsBlocked("DOUBLECLICK.NET"));
        Assert.True(blocker.IsBlocked("doubleclick.net."));
    }

    [Theory]
    [InlineData("github.com")]
    [InlineData("example.org")]
    [InlineData("net")]
    [InlineData("")]
    [InlineData(null)]
    public void NormalSitesAndTopLevelDomains_AreNotBlocked(string? host)
    {
        var blocker = AdBlockerService.Instance;
        blocker.LoadBundledDefaults();

        Assert.False(blocker.IsBlocked(host));
    }

    [Fact]
    public void SimilarLookingDomain_IsNotBlocked()
    {
        var blocker = AdBlockerService.Instance;
        blocker.LoadBundledDefaults();

        // Nur echte Subdomains zählen, nicht Domains, die zufällig gleich enden
        Assert.False(blocker.IsBlocked("notdoubleclick.net"));
    }
}
