<p align="center">
  <img src="app.png" alt="Echo-Browser Logo" width="96">
</p>

<h1 align="center">Echo-Browser</h1>

<p align="center">
  Ein schlanker Windows-Browser auf Basis von Chromium (WebView2) und WPF – mit eingebautem Werbeblocker,
  Chrome-Erweiterungen und einem ruhigen Silber-/Anthrazit-Design.
</p>

---

## Funktionen

- **Tabs** mit Drag & Drop, Inkognito-Fenster und Sitzungswiederherstellung
- **Echo Shield** – integrierter Werbe- & Tracker-Blocker
  - Netzwerkfilter (StevenBlack-Hosts-Liste, ca. 75.000 Domains, lokal zwischengespeichert)
  - Kosmetisches Ausblenden von Werbeflächen
  - pro Website abschaltbar, mit Zähler in der Adressleiste
- **Chrome-Erweiterungen** direkt aus dem Chrome Web Store installieren, anheften und über ihr Popup bedienen
- **Lesezeichenleiste** mit Gruppen/Ordnern und Drag & Drop, **Favoriten-Seitenleiste**
- **Eigene Startseite** mit Schnellzugriff-Kacheln und wählbarer Suchmaschine
  (DuckDuckGo, Google, Bing, Ecosia, Brave Search, Startpage)
- **Einstellungsseite** (`echo://settings`) für Startverhalten, Suche, Design, Datenschutz, Downloads und Tabs
- **Themes**: Silber & Anthrazit, Midnight OLED, Titanium Light, Cobalt Slate – plus frei wählbare Akzentfarbe
- **Anpassbare Symbolleiste** – Schaltflächen per Rechtsklick ein- und ausblenden
- **Mehrsprachig**: Deutsch und Englisch vollständig, Französisch und Spanisch teilweise; Sprachwechsel ohne Neustart

## Voraussetzungen

| | |
|---|---|
| Betriebssystem | Windows 10 oder 11 (x64 / ARM64) |
| .NET | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (zum Bauen) bzw. .NET 8 Desktop Runtime (zum Ausführen) |
| WebView2 | [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) – unter Windows 11 bereits vorinstalliert |

## Bauen & Starten

```bash
git clone https://github.com/GoToPlayHD/Echo-Browser.git
cd Echo-Browser
dotnet run
```

Nur bauen (die EXE liegt danach unter `bin/Debug/net8.0-windows/EchoBrowser.exe`):

```bash
dotnet build
```

Release-Version als eigenständige EXE veröffentlichen:

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

Alternativ kann `EchoBrowser.csproj` direkt in Visual Studio 2022 oder JetBrains Rider geöffnet werden.

## Tastenkürzel

| Kürzel | Aktion |
|---|---|
| `Strg+T` / `Strg+W` | Neuer Tab / Tab schließen |
| `Strg+N` / `Strg+Umschalt+N` | Neues Fenster / Neues Inkognito-Fenster |
| `Strg+L` / `Alt+D` | Adressleiste fokussieren |
| `Strg+R` / `F5` | Neu laden |
| `Strg+D` | Seite als Lesezeichen speichern |
| `Strg+Umschalt+B` | Lesezeichenleiste ein-/ausblenden |
| `Strg+J` / `Strg+H` | Downloads / Verlauf |
| `Strg+,` | Einstellungen |
| `F12` | Entwicklertools |

## Projektstruktur

```
├── MainWindow.xaml            Hauptfenster (Layout)
├── MainWindow.xaml.cs         Start, Fenster, Menü
├── MainWindow.*.cs            Teildateien nach Thema: Tabs, Navigation, Bookmarks, Sidebar,
│                              Shield, Extensions, Downloads, History, Session, Keyboard,
│                              Toolbar, WebMessages
├── Models/                    Datenklassen (Tab, Lesezeichen, Verlauf, Einstellungen …)
├── Services/                  Logik ohne UI
│   ├── AdBlockerService       Echo Shield (Filterliste, Blockprüfung, kosmetische Filter)
│   ├── ExtensionService       Installation & Verwaltung von Chrome-Erweiterungen
│   ├── StartPageService       HTML der Startseite
│   ├── SettingsPageService    HTML der Einstellungsseite
│   ├── WebMessageRouter       Nachrichten der internen Seiten an das Hauptfenster
│   ├── InternalPageSecurity   Schutz der Host-Bridge vor fremden Webseiten
│   ├── LocalizationService    Übersetzungen (siehe unten)
│   └── …                      Einstellungen, Verlauf, Lesezeichen, Themes, AtomicFile
├── Views/                     Weitere Fenster (Dialoge, Sprachauswahl, Erweiterungs-Popup)
├── Themes/                    Farben & Styles
└── Localization/              Sprachdateien (de.json, en.json, …)
```

Benutzerdaten (Einstellungen, Lesezeichen, Verlauf, Erweiterungen, Browserprofil) liegen unter
`%LOCALAPPDATA%\EchoBrowser`.

## Übersetzungen

Alle Texte stehen in `Localization/<sprache>.json` und werden in die EXE eingebettet.
Fehlt ein Text in einer Sprache, wird Englisch und danach Deutsch verwendet.

| Wo | Verwendung |
|---|---|
| XAML | `Text="{loc:Loc Nav_Back}"` |
| C# | `Tr.Get("Nav_Back")` bzw. `Tr.Format("Ext_AddedMessage", name)` |
| Start-/Einstellungsseite (HTML) | `{{t:Key}}` im HTML, `{{js:Key}}` in JavaScript |

**Neue Sprache hinzufügen:** `Localization/en.json` kopieren, z. B. als `it.json`, übersetzen und die Sprache
in der Sprachauswahl (`Views/LanguageSetupWindow`) sowie auf der Einstellungsseite ergänzen.

## Mitwirken

Offene Punkte und erledigte Änderungen stehen in [TODO.md](TODO.md).
Änderungen bitte über einen eigenen Branch und Pull Request einreichen.
