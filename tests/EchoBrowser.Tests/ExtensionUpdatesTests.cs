using System.IO;
using System.IO.Compression;
using System.Text;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>
/// Erweiterungs-Updates (Audit S1): Abfrage beim Web Store, Versionsvergleich, neue Rechte,
/// Store-ID aus dem CRX3-Kopf und das Einspielen in denselben Ordner beim Start.
/// </summary>
public class ExtensionUpdatesTests
{
    private const string IdA = "cjpalhdlnbpafiamejdnhcphjbkeiagm";
    private const string IdB = "nngceckbapebfimnlniiiahkandclblb";

    [Fact]
    public void UpdateCheckUrl_ListsEveryExtensionWithItsVersion()
    {
        string url = ExtensionUpdates.BuildUpdateCheckUrl(new[] { (IdA, "1.60.0"), (IdB, "2024.1.2") }, "142.0.1.2");

        Assert.StartsWith("https://clients2.google.com/service/update2/crx?response=updatecheck&acceptformat=crx3&prodversion=142.0.1.2", url);
        Assert.Contains($"&x=id%3D{IdA}%26v%3D1.60.0%26uc", url);
        Assert.Contains($"&x=id%3D{IdB}%26v%3D2024.1.2%26uc", url);
    }

    [Fact]
    public void UpdateCheckResponse_KeepsOnlyRealHttpsUpdates()
    {
        string xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gupdate xmlns="http://www.google.com/update2/response" protocol="2.0" server="prod">
              <daystart elapsed_days="6880" elapsed_seconds="100"/>
              <app appid="{IdA}" status="ok">
                <updatecheck codebase="https://clients2.googleusercontent.com/crx/blobs/abc/ublock.crx" hash_sha256="ABCDEF0123" status="ok" version="1.61.0"/>
              </app>
              <app appid="{IdB}" status="ok"><updatecheck status="noupdate"/></app>
              <app appid="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" status="ok">
                <updatecheck codebase="http://insecure.example/x.crx" status="ok" version="9.9"/>
              </app>
              <app appid="not-an-id" status="ok">
                <updatecheck codebase="https://example.com/x.crx" status="ok" version="1.0"/>
              </app>
            </gupdate>
            """;

        var offer = Assert.Single(ExtensionUpdates.ParseUpdateCheck(xml));
        Assert.Equal(IdA, offer.Id);
        Assert.Equal("1.61.0", offer.Version);
        Assert.Equal("https://clients2.googleusercontent.com/crx/blobs/abc/ublock.crx", offer.Codebase);
        Assert.Equal("abcdef0123", offer.Sha256);
    }

    [Fact]
    public void UpdateCheckResponse_Broken_GivesNothing()
    {
        Assert.Empty(ExtensionUpdates.ParseUpdateCheck("<gupdate><app"));
    }

    [Theory]
    [InlineData("1.2.10", "1.2.9", true)]
    [InlineData("2", "1.9.9.9", true)]
    [InlineData("1.2", "1.2.0", false)]
    [InlineData("1.0", "1.0.1", false)]
    [InlineData("1.0", "1.0", false)]
    [InlineData("abc", "1.0", false)]
    [InlineData("1.0.0.0.1", "1.0", false)]
    [InlineData(null, "1.0", false)]
    public void VersionComparison(string? candidate, string installed, bool expected)
    {
        Assert.Equal(expected, ExtensionUpdates.IsNewerVersion(candidate, installed));
    }

    private static ExtensionPermissionSummary Perms(bool all = false, string[]? sites = null, string[]? warnings = null, string[]? other = null) =>
        new(all, sites ?? Array.Empty<string>(), warnings ?? Array.Empty<string>(), other ?? Array.Empty<string>());

    [Fact]
    public void NewConsent_OnlyWhenTheUpdateWantsMore()
    {
        var installed = Perms(sites: new[] { "mail.example" }, warnings: new[] { "Ext_Perm_Tabs" });

        Assert.False(ExtensionUpdates.NeedsNewConsent(installed, installed));
        Assert.False(ExtensionUpdates.NeedsNewConsent(installed, Perms()));                                       // weniger
        Assert.True(ExtensionUpdates.NeedsNewConsent(installed, Perms(all: true)));                               // alle Websites
        Assert.True(ExtensionUpdates.NeedsNewConsent(installed, Perms(sites: new[] { "mail.example", "bank.example" })));
        Assert.True(ExtensionUpdates.NeedsNewConsent(installed, Perms(warnings: new[] { "Ext_Perm_History" })));
        Assert.True(ExtensionUpdates.NeedsNewConsent(installed, Perms(other: new[] { "somethingNew" })));
        Assert.False(ExtensionUpdates.NeedsNewConsent(Perms(all: true), Perms(sites: new[] { "bank.example" }))); // schon alle
    }

    [Fact]
    public void IdFromBytes_MapsHexDigitsToAtoP()
    {
        byte[] bytes = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        Assert.Equal("aaabacadaeafagahaiajakalamanaoap", ExtensionUpdates.IdFromBytes(bytes));
    }

    [Fact]
    public void CrxId_IsReadFromTheCrx3Header()
    {
        byte[] crx = BuildCrx3(IdA, ZipWith(new Dictionary<string, string> { ["manifest.json"] = "{}" }));
        Assert.Equal(IdA, ExtensionUpdates.ReadCrxId(crx));
    }

    [Fact]
    public void CrxId_IsNullWithoutCrx3Header()
    {
        byte[] zip = ZipWith(new Dictionary<string, string> { ["manifest.json"] = "{}" });
        byte[] crx2 = "Cr24"u8.ToArray().Concat(BitConverter.GetBytes(2u)).Concat(new byte[8]).Concat(zip).ToArray();
        byte[] truncated = BuildCrx3(IdA, zip)[..20];

        Assert.Null(ExtensionUpdates.ReadCrxId(zip));
        Assert.Null(ExtensionUpdates.ReadCrxId(crx2));
        Assert.Null(ExtensionUpdates.ReadCrxId(truncated));
    }

    [Theory]
    [InlineData("download_" + IdA + "_0123abcd_4567ef", IdA)]
    [InlineData("MyExtension_0123abcd", null)]
    [InlineData("download_tooshort_0123", null)]
    public void LegacyFolderNames_RevealTheStoreId(string folder, string? expected)
    {
        Assert.Equal(expected, ExtensionUpdates.StoreIdFromFolderName(folder));
    }

    [Fact]
    public void PrepareFromCrx_ReadsTheStoreId()
    {
        string crx = WriteTemp(BuildCrx3(IdB, ZipWith(Manifest("1.0"))));
        var prepared = ExtensionService.Instance.PrepareFromCrx(crx);
        try
        {
            Assert.Equal(IdB, prepared.StoreId);
        }
        finally
        {
            ExtensionService.Instance.DiscardPrepared(prepared);
            File.Delete(crx);
        }
    }

    [Fact]
    public void SwapFolder_ReplacesContent_AndRollsBackOnFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), "echo-swap-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(root, "ext");
        string replacement = Path.Combine(root, "new");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(replacement);
        File.WriteAllText(Path.Combine(target, "v.txt"), "1");
        File.WriteAllText(Path.Combine(replacement, "v.txt"), "2");
        try
        {
            ExtensionService.SwapFolder(target, replacement);
            Assert.Equal("2", File.ReadAllText(Path.Combine(target, "v.txt")));
            Assert.False(Directory.Exists(replacement));
            Assert.Single(Directory.GetDirectories(root));   // keine alte Kopie übrig

            // Ersatz fehlt: die bisherige Version muss bleiben
            Assert.ThrowsAny<IOException>(() => ExtensionService.SwapFolder(target, Path.Combine(root, "missing")));
            Assert.Equal("2", File.ReadAllText(Path.Combine(target, "v.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PendingUpdate_IsAppliedIntoTheSameFolder()
    {
        var service = ExtensionService.Instance;
        string extensions = Path.Combine(AppPaths.DataFolder, "Extensions");
        string target = Path.Combine(extensions, $"download_{IdA}_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "manifest.json"), Manifest("1.0")["manifest.json"]);
        File.WriteAllText(Path.Combine(target, "old-only.js"), "// alte Version");

        Directory.CreateDirectory(service.UpdateStagingDirectory);
        string crx = WriteTemp(BuildCrx3(IdA, ZipWith(Manifest("2.0"))));
        var staged = service.PrepareFromCrx(crx, service.UpdateStagingDirectory);
        File.Delete(crx);
        try
        {
            Assert.Equal(IdA, service.GetStoreId(target));   // aus dem Ordnernamen älterer Installationen

            service.AddPendingUpdate(new PendingExtensionUpdate(target, staged.RootFolder, staged.ManifestFolder, IdA, "2.0"));
            Assert.True(service.UpdateStatus.PendingCount >= 1);

            service.ApplyPendingUpdates();

            Assert.Contains("\"2.0\"", File.ReadAllText(Path.Combine(target, "manifest.json")));
            Assert.False(File.Exists(Path.Combine(target, "old-only.js")));
            Assert.False(Directory.Exists(staged.RootFolder));
            Assert.Equal(0, service.UpdateStatus.PendingCount);
            Assert.Empty(Directory.GetDirectories(extensions, "*.echo-old-*"));
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            service.DiscardPrepared(staged);
        }
    }

    private static Dictionary<string, string> Manifest(string version) => new()
    {
        ["manifest.json"] = $$"""{ "manifest_version": 3, "name": "Test", "version": "{{version}}", "permissions": ["storage"] }"""
    };

    private static byte[] ZipWith(Dictionary<string, string> files)
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        return zip.ToArray();
    }

    /// <summary>CRX3: "Cr24", Version 3, Kopf (Protobuf) mit einem Schlüsselfeld und signed_header_data.crx_id.</summary>
    private static byte[] BuildCrx3(string id, byte[] zip)
    {
        byte[] crxId = Enumerable.Range(0, 16).Select(i => (byte)(((id[2 * i] - 'a') << 4) | (id[2 * i + 1] - 'a'))).ToArray();
        byte[] signedData = new byte[] { 0x0A, 16 }.Concat(crxId).ToArray();                       // SignedData.crx_id = 1

        var header = new List<byte> { 0x12, 3, 0xAA, 0xBB, 0xCC };                                    // sha256_with_rsa = 2 (Dummy)
        header.AddRange(Varint((10000 << 3) | 2));                                                    // signed_header_data = 10000
        header.AddRange(Varint(signedData.Length));
        header.AddRange(signedData);

        return "Cr24"u8.ToArray()
            .Concat(BitConverter.GetBytes(3u))
            .Concat(BitConverter.GetBytes((uint)header.Count))
            .Concat(header)
            .Concat(zip)
            .ToArray();
    }

    private static IEnumerable<byte> Varint(int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            yield return (byte)(v | 0x80);
            v >>= 7;
        }
        yield return (byte)v;
    }

    private static string WriteTemp(byte[] data)
    {
        string path = Path.Combine(Path.GetTempPath(), $"echo-update-test-{Guid.NewGuid():N}.crx");
        File.WriteAllBytes(path, data);
        return path;
    }
}
