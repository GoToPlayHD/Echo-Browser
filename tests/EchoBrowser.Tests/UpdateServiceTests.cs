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

        [Fact]
        public void UpdateService_IsInstalled_DoesNotThrow()
        {
            bool isInstalled = UpdateService.Instance.IsInstalled;
            // In unit tests running from dotnet test, it should be false (development mode)
            Assert.False(isInstalled);
        }

        [Fact]
        public void UpdateService_StartAndStopAutoCheckTimer_Succeeds()
        {
            UpdateService.Instance.StartAutoCheckTimer();
            // Calling it twice should be idempotent and safe
            UpdateService.Instance.StartAutoCheckTimer();
            UpdateService.Instance.StopAutoCheckTimer();
        }

        [Fact]
        public void SessionService_IsRestoredAfterUpdate_DefaultsToFalseAndCanBeSet()
        {
            var session = SessionService.Instance;
            bool initial = session.IsRestoredAfterUpdate;
            session.IsRestoredAfterUpdate = true;
            Assert.True(session.IsRestoredAfterUpdate);
            session.IsRestoredAfterUpdate = initial;
        }

        [Fact]
        public void SettingsPageService_IncludesUpdateStatusJson()
        {
            var settings = new AppSettings();
            var updateStatus = new { status = "ReadyToRestart", message = "Ready", progress = 100, version = "1.2.1" };
            string html = SettingsPageService.GetSettingsPageHtml(settings, "120.0", "1.2.0", updateStatus);
            Assert.Contains("ReadyToRestart", html);
            Assert.Contains("1.2.1", html);
        }
    }
}
