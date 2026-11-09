using System.IO;
using System.Text.Json;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Kompatibilitätsschicht vor dem Service Worker einer Erweiterung (chrome.tabs.create liefert in WebView2 kein Tab).</summary>
public class ExtensionCompatTests
{
    private static string NewExtension(string manifest)
    {
        string dir = Path.Combine(Path.GetTempPath(), "echo-compat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), manifest);
        return dir;
    }

    private static string Worker(string dir) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")))
            .RootElement.GetProperty("background").GetProperty("service_worker").GetString()!;

    [Fact]
    public void ClassicServiceWorker_LoadsCompatFirst()
    {
        string dir = NewExtension("""{ "manifest_version": 3, "name": "WEB.DE MailCheck", "background": { "service_worker": "js/background-mailcheck.js" } }""");
        try
        {
            Assert.True(ExtensionCompat.Apply(dir));

            Assert.Equal(ExtensionCompat.WrapperFileName, Worker(dir));
            string wrapper = File.ReadAllText(Path.Combine(dir, ExtensionCompat.WrapperFileName));
            Assert.Contains("importScripts('echo-compat.js', 'js/background-mailcheck.js')", wrapper);
            Assert.Contains("chrome.tabs", File.ReadAllText(Path.Combine(dir, ExtensionCompat.CompatFileName)));
            // Name mit Sonderzeichen bleibt lesbar
            Assert.Contains("\"WEB.DE MailCheck\"", File.ReadAllText(Path.Combine(dir, "manifest.json")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ModuleServiceWorker_UsesImports()
    {
        string dir = NewExtension("""{ "manifest_version": 3, "background": { "service_worker": "/background/background.js", "type": "module" } }""");
        try
        {
            Assert.True(ExtensionCompat.Apply(dir));
            string wrapper = File.ReadAllText(Path.Combine(dir, ExtensionCompat.WrapperFileName));
            Assert.Contains("import './echo-compat.js';", wrapper);
            Assert.Contains("import './background/background.js';", wrapper);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ApplyingTwice_KeepsTheOriginalWorker()
    {
        string dir = NewExtension("""{ "manifest_version": 3, "background": { "service_worker": "bg.js" } }""");
        try
        {
            ExtensionCompat.Apply(dir);
            ExtensionCompat.Apply(dir);
            Assert.Equal(ExtensionCompat.WrapperFileName, Worker(dir));
            Assert.Contains("'bg.js'", File.ReadAllText(Path.Combine(dir, ExtensionCompat.WrapperFileName)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ServiceWorkerRefresh_NeededOnceAfterSwitchingToWrapper()
    {
        string dir = NewExtension("""{ "manifest_version": 3, "background": { "service_worker": "bg.js" } }""");
        try
        {
            Assert.False(ExtensionCompat.NeedsServiceWorkerRefresh(dir));

            ExtensionCompat.Apply(dir);
            Assert.True(ExtensionCompat.NeedsServiceWorkerRefresh(dir));

            ExtensionCompat.MarkServiceWorkerRegistered(dir);
            Assert.False(ExtensionCompat.NeedsServiceWorkerRefresh(dir));

            // Weitere Starts ändern das Manifest nicht mehr – kein erneutes Aus- und Einschalten
            ExtensionCompat.Apply(dir);
            Assert.False(ExtensionCompat.NeedsServiceWorkerRefresh(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MarkServiceWorkerRegistered_WithoutWrapper_WritesNothing()
    {
        string dir = NewExtension("""{ "manifest_version": 3, "name": "eigener Ordner" }""");
        try
        {
            ExtensionCompat.MarkServiceWorkerRegistered(dir);
            Assert.False(File.Exists(Path.Combine(dir, ExtensionCompat.RegisteredMarkerFileName)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("""{ "manifest_version": 2, "background": { "scripts": ["bg.js"] } }""")]
    [InlineData("""{ "manifest_version": 3, "name": "nur Inhaltsskripte" }""")]
    public void WithoutServiceWorker_NothingChanges(string manifest)
    {
        string dir = NewExtension(manifest);
        try
        {
            Assert.False(ExtensionCompat.Apply(dir));
            Assert.Equal(manifest, File.ReadAllText(Path.Combine(dir, "manifest.json")));
            Assert.False(File.Exists(Path.Combine(dir, ExtensionCompat.WrapperFileName)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ManifestWithCommentsAndTrailingCommas_IsAccepted()
    {
        string dir = NewExtension("""
            {
              // Chrome erlaubt Kommentare im Manifest
              "manifest_version": 3,
              "background": { "service_worker": "bg.js", },
            }
            """);
        try
        {
            Assert.True(ExtensionCompat.Apply(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
