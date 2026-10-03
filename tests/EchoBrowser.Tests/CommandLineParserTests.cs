using System.IO;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Adressen, die Windows oder andere Apps beim Start übergeben.</summary>
public class CommandLineParserTests
{
    [Fact]
    public void WebAddresses_AreTakenOver()
    {
        var urls = CommandLineParser.GetUrls(new[] { "https://example.com/a?b=1", "http://localhost:3000/" });

        Assert.Equal(new[] { "https://example.com/a?b=1", "http://localhost:3000/" }, urls);
    }

    [Fact]
    public void SwitchesAndEmptyArguments_AreSkipped()
    {
        var urls = CommandLineParser.GetUrls(new[] { "--veloapp-updated", "-incognito", "", "  ", "https://example.com/" });

        Assert.Equal(new[] { "https://example.com/" }, urls);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox")]
    [InlineData("ms-settings:defaultapps")]
    [InlineData("kein-link")]
    public void DangerousOrUnknownArguments_AreDropped(string arg)
    {
        Assert.Empty(CommandLineParser.GetUrls(new[] { arg }));
    }

    [Fact]
    public void WindowsPaths_BecomeFileUrls()
    {
        string file = Path.Combine(Path.GetTempPath(), $"echo-test-{Guid.NewGuid():N}.html");
        File.WriteAllText(file, "<p>test</p>");
        try
        {
            var url = Assert.Single(CommandLineParser.GetUrls(new[] { file }));
            Assert.StartsWith("file:///", url);
            Assert.EndsWith(".html", url);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void QuotedArguments_AreUnquoted()
    {
        Assert.Equal(new[] { "https://example.com/" }, CommandLineParser.GetUrls(new[] { "\"https://example.com/\"" }));
    }

    [Fact]
    public void InternalPages_AreAllowed()
    {
        Assert.Equal(new[] { "echo://settings" }, CommandLineParser.GetUrls(new[] { "echo://settings" }));
    }

    [Fact]
    public void Null_GivesEmptyList()
    {
        Assert.Empty(CommandLineParser.GetUrls(null));
    }
}
