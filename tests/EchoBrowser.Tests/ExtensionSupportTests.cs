using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Erweiterungen: Download aus dem Web Store, CRX-Kopf, angeheftete IDs.</summary>
public class ExtensionSupportTests
{
    [Fact]
    public void WebStoreDownload_ReportsTheRealBrowserVersion()
    {
        // 1Password verlangt Chrome 140 – mit der früher fest eingetragenen 128 lieferte Google eine leere Datei
        string url = ExtensionService.BuildWebStoreDownloadUrl("aeblfdkhhhdcdjpifhhbdiojplfjncoa", "156.0.4314.58");
        Assert.Contains("prodversion=156.0.4314.58", url);
        Assert.Contains("x=id%3Daeblfdkhhhdcdjpifhhbdiojplfjncoa%26uc", url);
    }

    [Fact]
    public void Crx3_ZipStartsAfterTheHeader_EvenIfTheHeaderContainsPK()
    {
        // Kopf mit "PK\x03\x04" mitten in der (Signatur-)Länge – darf nicht als ZIP-Beginn gelten
        byte[] header = { 0x00, 0x50, 0x4B, 0x03, 0x04, 0x11, 0x22, 0x33 };
        byte[] zip = { 0x50, 0x4B, 0x03, 0x04, 0xAA, 0xBB };
        var crx = new List<byte>();
        crx.AddRange("Cr24"u8.ToArray());
        crx.AddRange(BitConverter.GetBytes(3u));
        crx.AddRange(BitConverter.GetBytes((uint)header.Length));
        crx.AddRange(header);
        crx.AddRange(zip);

        Assert.Equal(12 + header.Length, ExtensionService.FindZipSignatureOffset(crx.ToArray()));
    }

    [Fact]
    public void PlainZip_IsStillFound()
    {
        byte[] data = { 0x01, 0x02, 0x50, 0x4B, 0x03, 0x04, 0x00 };
        Assert.Equal(2, ExtensionService.FindZipSignatureOffset(data));
    }

    [Fact]
    public void PinnedExtensions_KeepTheirPlace_WhenTheirIdChanges()
    {
        var pinned = new List<string> { "aaa", "bbb", "ccc" };
        bool changed = ExtensionService.RenamePinnedIds(pinned,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["BBB"] = "neu" });

        Assert.True(changed);
        Assert.Equal(new[] { "aaa", "neu", "ccc" }, pinned);
        Assert.False(ExtensionService.RenamePinnedIds(pinned, new Dictionary<string, string>()));
    }
}
