using System.Text.RegularExpressions;
using EchoBrowser.Models;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests
{
    public class UpdateServiceTests
    {
        [Fact]
        public void UpdateService_Instance_IsNotNull()
        {
            Assert.NotNull(UpdateService.Instance);
        }

        [Fact]
        public void UpdateService_CurrentVersion_ReturnsValidVersionString()
        {
            string version = UpdateService.Instance.CurrentVersion;
            Assert.False(string.IsNullOrWhiteSpace(version));
            Assert.Matches(@"^\d+\.\d+(\.\d+)?", version);
        }

        [Fact]
        public void AppSettings_UpdateDefaults_AreCorrect()
        {
            var settings = new AppSettings();
            Assert.True(settings.AutoCheckForUpdates);
            Assert.Equal("https://github.com/GoToPlayHD/Echo-Browser", settings.UpdateUrl);
            Assert.False(settings.CheckPrereleaseUpdates);
        }

        [Fact]
        public void UpdateService_DefaultUpdateUrl_IsGitHubRepo()
        {
            Assert.Equal("https://github.com/GoToPlayHD/Echo-Browser", UpdateService.DefaultUpdateUrl);
            Assert.Equal("https://github.com/GoToPlayHD/Echo-Browser", UpdateService.Instance.UpdateUrl);
        }

        [Fact]
        public void UpdateCheckResult_ConstructsCorrectly()
        {
            var result = new UpdateCheckResult(true, UpdateStatus.UpToDate, "All good", "1.2.0");
            Assert.True(result.Success);
            Assert.Equal(UpdateStatus.UpToDate, result.Status);
            Assert.Equal("All good", result.Message);
            Assert.Equal("1.2.0", result.AvailableVersion);
            Assert.Null(result.Error);
        }

        [Fact]
        public void UpdateStatusEventArgs_ConstructsCorrectly()
        {
            var args = new UpdateStatusEventArgs(UpdateStatus.Downloading, "Downloading...", 42, "1.3.0");
            Assert.Equal(UpdateStatus.Downloading, args.Status);
            Assert.Equal("Downloading...", args.Message);
            Assert.Equal(42, args.Progress);
            Assert.Equal("1.3.0", args.AvailableVersion);
        }
    }
}
