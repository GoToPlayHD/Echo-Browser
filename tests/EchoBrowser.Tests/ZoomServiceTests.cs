using System.IO;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Zoomstufen und Zoom pro Website.</summary>
public class ZoomServiceTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"echo-zoom-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }

    [Theory]
    [InlineData(1.0, 1, 1.1)]
    [InlineData(1.0, -1, 0.9)]
    [InlineData(1.1, 1, 1.25)]
    [InlineData(1.05, 1, 1.1)]   // krumme Werte (Strg+Mausrad) springen zur nächsten Stufe
    [InlineData(1.05, -1, 1.0)]
    [InlineData(5.0, 1, 5.0)]    // Obergrenze
    [InlineData(0.25, -1, 0.25)] // Untergrenze
    public void Next_FollowsChromeSteps(double current, int direction, double expected)
    {
        Assert.Equal(expected, ZoomLevels.Next(current, direction), 3);
    }

    [Theory]
    [InlineData(1.0, "100 %")]
    [InlineData(1.1, "110 %")]
    [InlineData(0.67, "67 %")]
    public void Format_ShowsPercent(double factor, string expected)
    {
        Assert.Equal(expected, ZoomLevels.Format(factor));
    }

    [Fact]
    public void SiteZoom_IsRememberedPerHost_AndSurvivesRestart()
    {
        var zoom = new ZoomService(_file);
        zoom.Set("https://example.com/a", 1.25, defaultFactor: 1.0);

        var reloaded = new ZoomService(_file);
        Assert.Equal(1.25, reloaded.Get("https://EXAMPLE.com/other/page"));
        Assert.Null(reloaded.Get("https://example.org/"));
    }

    [Fact]
    public void DefaultZoom_IsNotStored()
    {
        var zoom = new ZoomService(_file);
        zoom.Set("https://example.com/", 1.5, defaultFactor: 1.0);
        zoom.Set("https://example.com/", 1.0, defaultFactor: 1.0);

        Assert.Null(new ZoomService(_file).Get("https://example.com/"));
    }

    [Theory]
    [InlineData("echo://settings")]
    [InlineData("file:///C:/seite.html")]
    [InlineData("about:blank")]
    [InlineData(null)]
    public void InternalAndLocalPages_GetNoSiteZoom(string? url)
    {
        var zoom = new ZoomService(_file);
        zoom.Set(url, 2.0, defaultFactor: 1.0);

        Assert.Null(zoom.Get(url));
        Assert.False(File.Exists(_file));
    }
}
