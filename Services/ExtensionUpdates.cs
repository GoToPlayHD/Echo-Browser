using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EchoBrowser.Services
{
    /// <summary>Neue Version einer Erweiterung laut Update-Server des Chrome Web Store.</summary>
    public sealed record ExtensionUpdateOffer(string Id, string Version, string Codebase, string? Sha256);

    /// <summary>
    /// Bausteine für Erweiterungs-Updates (Audit S1): Store-ID aus dem CRX-Kopf, Update-Abfrage beim
    /// Chrome Web Store (Protokoll »update2«, wie Chrome), Versionsvergleich und neue Berechtigungen.
    /// </summary>
    public static class ExtensionUpdates
    {
        public const string UpdateServiceUrl = "https://clients2.google.com/service/update2/crx";

        /// <summary>Abfrage für mehrere Erweiterungen auf einmal: x=id%3D…%26v%3D…%26uc je Erweiterung.</summary>
        public static string BuildUpdateCheckUrl(IEnumerable<(string Id, string Version)> extensions, string browserVersion)
        {
            var url = new StringBuilder(UpdateServiceUrl)
                .Append("?response=updatecheck&acceptformat=crx3&prodversion=").Append(Uri.EscapeDataString(browserVersion));
            foreach (var (id, version) in extensions)
            {
                url.Append("&x=").Append(Uri.EscapeDataString($"id={id}&v={version}&uc"));
            }
            return url.ToString();
        }

        /// <summary>
        /// Antwort des Update-Servers auswerten. Nur Einträge mit status="ok", Download-Adresse und gültiger ID;
        /// »noupdate« und Fehler fallen weg.
        /// </summary>
        public static IReadOnlyList<ExtensionUpdateOffer> ParseUpdateCheck(string xml)
        {
            var offers = new List<ExtensionUpdateOffer>();
            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml);
            }
            catch (Exception ex)
            {
                Log.Warn("Antwort des Erweiterungs-Update-Servers nicht lesbar", ex);
                return offers;
            }

            foreach (var app in doc.Descendants().Where(e => e.Name.LocalName == "app"))
            {
                string? id = (string?)app.Attribute("appid");
                var check = app.Elements().FirstOrDefault(e => e.Name.LocalName == "updatecheck");
                if (check == null || !ExtensionService.IsValidExtensionId(id) || (string?)check.Attribute("status") != "ok") continue;

                string? codebase = (string?)check.Attribute("codebase");
                string? version = (string?)check.Attribute("version");
                if (string.IsNullOrWhiteSpace(version) || !Uri.TryCreate(codebase, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;

                string? sha256 = (string?)check.Attribute("hash_sha256");
                offers.Add(new ExtensionUpdateOffer(id!, version, codebase!, string.IsNullOrWhiteSpace(sha256) ? null : sha256.ToLowerInvariant()));
            }
            return offers;
        }

        /// <summary>Chrome-Versionen: bis zu vier Zahlen mit Punkt, fehlende Teile zählen als 0.</summary>
        public static bool IsNewerVersion(string? candidate, string? installed)
        {
            if (!TryParseVersion(candidate, out var a) || !TryParseVersion(installed, out var b)) return false;
            for (int i = 0; i < 4; i++)
            {
                if (a[i] != b[i]) return a[i] > b[i];
            }
            return false;
        }

        private static bool TryParseVersion(string? text, out long[] parts)
        {
            parts = new long[4];
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] pieces = text.Trim().Split('.');
            if (pieces.Length > 4) return false;
            for (int i = 0; i < pieces.Length; i++)
            {
                if (!long.TryParse(pieces[i], System.Globalization.NumberStyles.None, null, out parts[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// Will die neue Version mehr als die alte? Dann fragt Echo vor dem Update nach (wie Chrome, das die
        /// Erweiterung bis zur Zustimmung abschaltet). Weniger Rechte oder gleiche Rechte: kein Nachfragen.
        /// </summary>
        public static bool NeedsNewConsent(ExtensionPermissionSummary installed, ExtensionPermissionSummary update)
        {
            if (update.AllSites && !installed.AllSites) return true;
            if (!installed.AllSites && update.Sites.Except(installed.Sites, StringComparer.OrdinalIgnoreCase).Any()) return true;
            return update.WarningKeys.Except(installed.WarningKeys).Any()
                || update.OtherPermissions.Except(installed.OtherPermissions).Any();
        }

        /// <summary>
        /// Store-ID aus dem Kopf einer CRX3-Datei (signed_header_data.crx_id), oder null bei CRX2 bzw.
        /// unlesbarem Kopf. Die Signatur selbst wird nicht geprüft – die ID dient dem Abgleich mit der erwarteten.
        /// </summary>
        public static string? ReadCrxId(ReadOnlySpan<byte> crx)
        {
            if (crx.Length < 12 || crx[0] != 'C' || crx[1] != 'r' || crx[2] != '2' || crx[3] != '4') return null;
            if (BitConverter.ToUInt32(crx[4..8]) != 3) return null;

            long headerSize = BitConverter.ToUInt32(crx[8..12]);
            if (12 + headerSize > crx.Length) return null;
            var header = crx.Slice(12, (int)headerSize);

            // CrxFileHeader.signed_header_data = 10000, darin SignedData.crx_id = 1 (16 Bytes)
            if (FindLengthDelimited(header, 10000) is not { } signedData) return null;
            if (FindLengthDelimited(header.Slice(signedData.Start, signedData.Length), 1) is not { Length: 16 } crxId) return null;

            return IdFromBytes(header.Slice(signedData.Start + crxId.Start, 16));
        }

        /// <summary>Erweiterungs-ID aus 16 Bytes: jede Hex-Ziffer 0–f wird zu a–p (wie Chrome).</summary>
        public static string IdFromBytes(ReadOnlySpan<byte> bytes)
        {
            var id = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                id.Append((char)('a' + (b >> 4))).Append((char)('a' + (b & 0x0F)));
            }
            return id.ToString();
        }

        /// <summary>Ältere Installationen aus dem Web Store liegen in »download_&lt;id&gt;_…«.</summary>
        public static string? StoreIdFromFolderName(string folderName)
        {
            var match = Regex.Match(folderName, "^download_([a-p]{32})_");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>Erstes Feld mit dieser Nummer und Typ »length-delimited« in einer Protobuf-Nachricht.</summary>
        private static (int Start, int Length)? FindLengthDelimited(ReadOnlySpan<byte> message, int field)
        {
            int pos = 0;
            while (pos < message.Length)
            {
                if (!TryReadVarint(message, ref pos, out ulong tag)) return null;
                int wireType = (int)(tag & 7);
                ulong number = tag >> 3;

                switch (wireType)
                {
                    case 0:
                        if (!TryReadVarint(message, ref pos, out _)) return null;
                        break;
                    case 1:
                        pos += 8;
                        break;
                    case 2:
                        if (!TryReadVarint(message, ref pos, out ulong length) || length > (ulong)(message.Length - pos)) return null;
                        if (number == (ulong)field) return (pos, (int)length);
                        pos += (int)length;
                        break;
                    case 5:
                        pos += 4;
                        break;
                    default:
                        return null;
                }
            }
            return null;
        }

        private static bool TryReadVarint(ReadOnlySpan<byte> data, ref int pos, out ulong value)
        {
            value = 0;
            for (int shift = 0; shift < 64 && pos < data.Length; shift += 7)
            {
                byte b = data[pos++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return true;
            }
            return false;
        }
    }
}
