using EchoBrowser.Services;
using Microsoft.Win32;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>
/// Windows-Registrierung als Browser. Geschrieben wird nur unter einem eigenen Test-Schlüssel
/// (HKCU\Software\EchoBrowser-Tests-…), der danach wieder gelöscht wird – das echte System bleibt unberührt.
/// </summary>
public class DefaultBrowserServiceTests : IDisposable
{
    private readonly string _testRootPath = @"Software\EchoBrowser-Tests-" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;

    public DefaultBrowserServiceTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_testRootPath);
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_testRootPath, throwOnMissingSubKey: false);
    }

    private string? Value(string key, string name = "") => _root.OpenSubKey(key)?.GetValue(name) as string;

    [Fact]
    public void Register_WritesUrlAndFileAssociations()
    {
        DefaultBrowserService.Register(@"C:\Apps\Echo\EchoBrowser.exe", _root);

        Assert.Equal("\"C:\\Apps\\Echo\\EchoBrowser.exe\" \"%1\"", Value(@"Software\Classes\EchoBrowserURL\shell\open\command"));
        Assert.Equal("", Value(@"Software\Classes\EchoBrowserURL", "URL Protocol"));
        Assert.Null(Value(@"Software\Classes\EchoBrowserHTML", "URL Protocol"));

        const string caps = @"Software\Clients\StartMenuInternet\EchoBrowser\Capabilities";
        Assert.Equal("EchoBrowserURL", Value(caps + @"\URLAssociations", "https"));
        Assert.Equal("EchoBrowserURL", Value(caps + @"\URLAssociations", "http"));
        Assert.Equal("EchoBrowserHTML", Value(caps + @"\FileAssociations", ".html"));
        Assert.Equal("EchoBrowserHTML", Value(caps + @"\FileAssociations", ".pdf"));
        Assert.Equal(caps, Value(@"Software\RegisteredApplications", "EchoBrowser"));
    }

    [Fact]
    public void Unregister_RemovesEverything()
    {
        DefaultBrowserService.Register(@"C:\Apps\Echo\EchoBrowser.exe", _root);
        DefaultBrowserService.Unregister(_root);

        Assert.Null(_root.OpenSubKey(@"Software\Classes\EchoBrowserURL"));
        Assert.Null(_root.OpenSubKey(@"Software\Classes\EchoBrowserHTML"));
        Assert.Null(_root.OpenSubKey(@"Software\Clients\StartMenuInternet\EchoBrowser"));
        Assert.Null(Value(@"Software\RegisteredApplications", "EchoBrowser"));
    }

    [Fact]
    public void Unregister_WithoutRegistration_DoesNotThrow()
    {
        DefaultBrowserService.Unregister(_root);
    }
}
