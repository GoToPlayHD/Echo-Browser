using System.IO;
using EchoBrowser.Models;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Gespeicherte Download-Liste und Gesamtfortschritt.</summary>
public class DownloadServiceTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"echo-downloads-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }

    [Fact]
    public void FinishedDownloads_SurviveRestart_RunningOnesBecomeCancelled()
    {
        var service = new DownloadService(_file);
        service.Add(new DownloadItem { FileName = "fertig.zip", FilePath = @"C:\x\fertig.zip", IsCompleted = true, TotalBytes = 10, BytesReceived = 10 });
        service.Add(new DownloadItem { FileName = "laeuft.iso", TotalBytes = 100, BytesReceived = 40 });

        var reloaded = new DownloadService(_file);

        Assert.Equal(2, reloaded.Items.Count);
        Assert.True(reloaded.Items.Single(i => i.FileName == "laeuft.iso").IsCancelled);
        Assert.True(reloaded.Items.Single(i => i.FileName == "fertig.zip").IsCompleted);
    }

    [Fact]
    public void NewestDownload_IsFirst()
    {
        var service = new DownloadService(_file);
        service.Add(new DownloadItem { FileName = "a" });
        service.Add(new DownloadItem { FileName = "b" });

        Assert.Equal("b", service.Items[0].FileName);
    }

    [Fact]
    public void OverallProgress_CombinesRunningDownloads()
    {
        var service = new DownloadService(_file);
        Assert.Null(service.OverallProgress());

        service.Add(new DownloadItem { TotalBytes = 100, BytesReceived = 50 });
        service.Add(new DownloadItem { TotalBytes = 300, BytesReceived = 50 });
        service.Add(new DownloadItem { IsCompleted = true, TotalBytes = 1000, BytesReceived = 1000 });

        Assert.Equal(0.25, service.OverallProgress()!.Value, 3);
    }

    [Fact]
    public void UnknownSize_GivesIndeterminateProgress()
    {
        var service = new DownloadService(_file);
        service.Add(new DownloadItem { BytesReceived = 50 });

        Assert.Equal(-1, service.OverallProgress());
    }

    [Fact]
    public void ClearFinished_KeepsRunningDownloads()
    {
        var service = new DownloadService(_file);
        service.Add(new DownloadItem { FileName = "fertig", IsCompleted = true });
        service.Add(new DownloadItem { FileName = "abgebrochen", IsCancelled = true });
        service.Add(new DownloadItem { FileName = "läuft" });

        service.ClearFinished();

        Assert.Equal("läuft", Assert.Single(service.Items).FileName);
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5 * 1024 * 1024, "5.0 MB")]
    public void FormatBytes_IsReadable(long bytes, string expected)
    {
        Assert.Equal(expected, DownloadItem.FormatBytes(bytes).Replace(',', '.'));
    }
}
