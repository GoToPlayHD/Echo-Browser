using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;

namespace EchoBrowser.Services
{
    /// <summary>
    /// HTML-Vorlagen der internen Seiten (InternalPages/*.html, in die EXE eingebettet) – als eigene Dateien
    /// statt riesiger C#-Strings, damit sie sich mit Editor-Unterstützung für HTML/CSS/JS pflegen lassen.
    /// <para>
    /// Übersetzte Vorlagen werden pro Sprache zwischengespeichert: Die Seiten werden bei jedem Aufruf neu erzeugt
    /// (Theme, Token, Nutzerdaten), das Übersetzen der ~3000 Zeilen muss aber nur einmal pro Sprache passieren.
    /// </para>
    /// </summary>
    public static class PageTemplates
    {
        private static readonly ConcurrentDictionary<string, string> RawCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string> LocalizedCache = new(StringComparer.Ordinal);

        /// <summary>Unveränderte Vorlage, z.B. "start.html".</summary>
        public static string Raw(string name) => RawCache.GetOrAdd(name, static n =>
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("InternalPages." + n)
                ?? throw new FileNotFoundException($"Interne Seite {n} ist nicht eingebettet");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });

        /// <summary>
        /// Übersetzte Vorlage aus dem Zwischenspeicher. <paramref name="variant"/> unterscheidet Fassungen derselben
        /// Seite (z.B. Inkognito); <paramref name="build"/> erzeugt sie beim ersten Bedarf je Sprache.
        /// </summary>
        public static string Localized(string variant, Func<string> build) =>
            LocalizedCache.GetOrAdd(variant + "|" + LocalizationService.Instance.CurrentLanguage, _ => build());
    }
}
