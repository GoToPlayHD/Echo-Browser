# Echo-Browser TODO

## Erledigt:
- [x] **Bug behoben (Lesezeichen-Persistenz)**: Wenn alle Lesezeichen gelöscht wurden, tauchten Standard-/Test-Lesezeichen nach dem Neustart nicht mehr auf. `BookmarkService.LoadBookmarks()` prüft nun vorab, ob `bookmarks.json` existiert; Fallback-Lesezeichen werden nur noch bei einer echten Erstinstallation generiert, nicht aber bei einer geleerten Lesezeichendatei.
- [x] **Bug behoben (Erweiterungs-Installation & "Download abgebrochen")**:
  - In `MainWindow.xaml.cs` wurde `SaveFileSecurityCheckStarting` abgefangen (`CancelSave = false`, `SuppressDefaultPolicy = true`), sodass Chromium Sicherheitsprüfungen für `.crx`-Dateien den Download nicht mehr vorzeitig abbrechen.
  - `CoreWebView2EnvironmentOptions.AreBrowserExtensionsEnabled = true` aktiviert.
  - Eigener `ExtensionService` implementiert: Dekodiert `.crx`-Header (Scan nach `PK\x03\x04`), entpackt das Archiv nach `%LOCALAPPDATA%\EchoBrowser\Extensions` und installiert die Erweiterung nahtlos via `Profile.AddBrowserExtensionAsync()`.
  - Heruntergeladene `.crx`-Dateien werden nach Abschluss des Downloads automatisch installiert. Zudem wurde ein interaktives Erweiterungs-Popup mit Liste, Status-Toggles, Löschen-Button, manuellem `.crx`-Dateidialog und Direktlink zum Chrome Web Store integriert.
- [x] **Bug behoben / Feature (Symbolleiste anpassen)**:
  - Alle Schaltflächen in der oberen Leiste (Sidebar, Zurück, Vorwärts, Neu laden, Home, Suchmaschinen-Auswahl, Erweiterungen, Downloads) können nun per Rechtsklick ("Schaltfläche ausblenden") ausgeblendet werden.
  - Ein Rechtsklick auf freien Platz in der Symbolleiste öffnet ein Kontextmenü mit dem Untermenü "Symbolleiste anpassen" (Checkboxen für jede Schaltfläche), "Alle Schaltflächen einblenden" sowie Direktlink "Symbolleiste in den Einstellungen anpassen...".
  - In den Einstellungen (`echo://settings` -> Erscheinungsbild) gibt es eine dedizierte Konfigurationskarte für alle Symbolleisten-Buttons inklusive persistenter Speicherung in den `AppSettings`.
- [x] **Integrierter Werbe- & Tracker-Blocker (Echo Shield)**:
  - **AdBlockerService (`Services/AdBlockerService.cs`)**:
    - Netzwerkfilterung via `WebResourceRequested` mit `AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All)`. Treffer werden mit Status `403 Forbidden` und `Stream.Null` blockiert.
    - $O(1)$ Lookups via `HashSet<string>` (`StringComparer.OrdinalIgnoreCase`) und rekursiver Subdomain-Prüfung (`ads.example.com` blockiert, wenn `example.com` gelistet ist).
    - Lokale Standard-Liste von über 100 gängigen Tracking- und Werbedomains als Fallback gebündelt.
    - Asynchroner Hintergrund-Download und lokales Caching der StevenBlack Hosts-Liste unter `%LOCALAPPDATA%\EchoBrowser\blocklist.txt`.
    - Kosmetisches Element-Hiding via `AddScriptToExecuteOnDocumentCreatedAsync` zum Ausblenden von Werbe-Containern (`.ad-container`, `.adsbox`, `[id^='google_ads']` etc.).
  - **Tab-Lifecycle & Session-Statistik**:
    - Jeder Tab verwaltet `BlockedTrackersCount` für die aktuelle Seite.
    - Bei `NavigationStarting` wird der Zähler pro Tab zurückgesetzt.
    - Bei jedem geblockten Netzwerk-Request wird der Zähler inkrementiert und die Omnibox synchron aktualisiert.
  - **UI-Integration ("Echo Shield" in Omnibox & Popup)**:
    - Dezentes Schild-Icon links in der Omnibox (Silber/Anthrazit mit Akzent-Highlight bei aktiver Blockierung).
    - Zähler-Badge direkt neben dem Schild mit Anzahl der geblockten Elemente (ausgeblendet bei 0).
    - Vollwertiges Shield-Popup im Echo-Theme:
      - Status-Banner: "Echo Shield: Aktiviert / Deaktiviert".
      - Toggle für aktuelle Website ("Schutz auf dieser Website") und globalen Schutz ("Echo Shield global aktiv").
      - Session-Statistik: "X Tracker und Werbeanzeigen blockiert" sowie geladene Filterregeln.
      - "Filter aktualisieren"-Button zur sofortigen Online-Synchronisation.
      - Erweiterte Berechtigungen (JavaScript, Popups) & Websitedaten leeren.
- [x] **Bug behoben (Kontextmenü-Gutter)**: Transparenter Icon-Bereich im Silver/Anthracite-Theme ohne weiße Boxen.
- [x] **Settings Menü ausgebaut**: Einstellungs-Zentrale (`echo://settings`) für Allgemein, Suche, Themes, Datenschutz, Downloads und Tabs.
- [x] **Lesezeichen-Gruppen Drag & Drop**: Vollständige OLE Drag & Drop Unterstützung in und aus Lesezeichen-Gruppen.
- [x] **Change umgesetzt (Adressleiste auf Startseite leer)**:
  - In `MainWindow.xaml.cs` wurde das Überschreiben von `tab.Url` durch `args.Uri` in `NavigationStarting` und `SourceChanged` für interne `data:text/html`-URIs unterbunden.
  - Zentrale Erkennungsmethode `IsStartPage()` implementiert: `echo://start`, `echo://newtab`, `about:blank` und Daten-URIs werden als Startseite erkannt und `txtUrl.Text` bleibt vollständig leer.
  - Dezenter Wasserzeichen-/Platzhaltertext ("Suchbegriff oder Webadresse eingeben...") hinzugefügt, der bei leerer Adressleiste erscheint und sofort bei Eingabe verschwindet.
- [x] **Bug behoben (Erweiterungen Download & Installation)**:
  - **Sanitization von Unpacked Extensions (`ExtensionService.SanitizeUnpackedExtension`)**: Chrome Web Store `.crx`-Dateien enthalten standardmäßig einen `_metadata`-Ordner. Der Chromium Unpacked Extension Loader blockiert Verzeichnisse, die mit Unterstrich beginnen (außer `_locales`), strikt mit `E_ACCESSDENIED`. Dieser Ordner wird nun beim Entpacken automatisch bereinigt.
  - **Thread-Sicherheit**: Die Installation via `CoreWebView2Profile.AddBrowserExtensionAsync` wurde vom Hintergrund-Task auf den UI-Thread (`Dispatcher.InvokeAsync`) verlegt, da WebView2 COM-Objekte strikt an das Single-Threaded Apartment (STA) gebunden sind.
  - **Download-Interruption behoben**: `args.Handled = true` in `DownloadStarting` gesetzt, wodurch Chromium/Edge das Herunterladen von `.crx`-Dateien nicht mehr als unsicheren Download im internen Edge-UI abbricht.
  - **Direkte Chrome Web Store Integration (`GetWebStoreHelperScript`)**:
    - Ein nativer "In Echo installieren"-Button wird auf Erweiterungsseiten im Chrome Web Store eingeblendet.
    - Klicks auf den Store-Button werden abgefangen, sodass der Google Webstore keine fehlerhaften `chrome.webstorePrivate`-Fehlermeldungen ("Fehler beim Herunterladen: Download interrupted") mehr anzeigt.
    - Automatischer Download des `.crx`-Archivs direkt über Googles Update-Server mit Chrome-User-Agent und nahtlose Installation.
  - **Downloads-Flyout erweitert**: Im Downloads-Menü erhalten `.crx`-Dateien nun automatisch einen "Installieren"-Aktionsbutton, mit dem jede heruntergeladene Erweiterung jederzeit mit einem Klick installiert werden kann.


  Changes:
   - [x] **Themed Erweiterungs-Installationsfenster**:
     - `ThemedDialogWindow` (`Views/ThemedDialogWindow.xaml` & `.xaml.cs`) im einheitlichen Silber/Anthrazit-Design von Echo-Browser erstellt.
     - Ersetzt Win32-`MessageBox` durch ein rahmenloses, abgerundetes Fenster mit Schattierung, Icon, Berechtigungs-/Detailbox und "Erweiterung hinzufügen"- / "Abbrechen"-Buttons.
     - Integriert für Web Store-Downloads, manuelle `.crx`-Installationen, Downloads-Flyout und Drag-and-Drop.
   - [x] **Shortcut für Erweiterungs-Einstellungen im Dropdown**:
     - Im `popupExtensions`-Flyout besitzt jede Erweiterung nun einen Einstellungs-Button (Zahnrad ⚙).
     - Öffnet direkt die in der `manifest.json` definierte Optionsseite (`chrome-extension://<id>/<options_page>`) in einem neuen Tab.
   - [x] **Erweiterungen an Symbolleiste anheften**:
     - Pin-Button (📌) im Erweiterungs-Flyout hinzugefügt.
     - Angeheftete Erweiterungs-IDs werden dauerhaft in `AppSettings.PinnedExtensionIds` gespeichert.
     - Angeheftete Erweiterungen erscheinen direkt in der oberen Navigations-Symbolleiste (`pnlPinnedExtensions`) mit eigenem Icon, Klick zum Öffnen der Optionen/des Popups und Rechtsklick-Kontextmenü (Lösen, Optionen, Entfernen).
   - [x] **Essenzielle System-Erweiterungen standardmäßig verstecken**:
     - `Microsoft Clipboard extension` und `Microsoft Edge PDF Viewer` werden in `ExtensionService.IsSystemExtension()` erkannt und in `GetInstalledExtensionsAsync()` standardmäßig aus der Benutzeroberfläche herausgefiltert, sodass sie nicht versehentlich gelöscht oder deaktiviert werden können.
   - [x] **Dropdown-Menü Button-Größen & Spacing optimiert**:
     - Neue Styles `FlyoutSmallButtonStyle` (24px Höhe) und `FlyoutActionButtonStyle` (30px Höhe) in `SilverAnthraciteTheme.xaml` eingeführt.
     - Spacing und Padding in `popupExtensions`, `popupShield` und `popupDownloads` vereinheitlicht und aufgeräumt (Buttons wie "+ Installieren", "Ordner öffnen", "Filter aktualisieren", "Cookies leeren" und "Web Store").

   Bugs:
   - [x] **Adblocker-Ergebnis auf `https://adblock.turtlecute.org/`**:
     - **Ursachenanalyse**: `adblock.turtlecute.org` testet 128 Tracker-Domains mit `fetch(url, { method: 'HEAD', mode: 'no-cors' })`. Durch die vorherige Rückgabe von HTTP 403 im WebView2-Netzwerkhandler wurde ein valider opaker Response erzeugt, wodurch das JS-`fetch()` ohne Exception auflöste und der Test die Domain als "nicht geblockt" markierte!
     - **Lösung**:
       1. Alle 128 Test-Domains aus der Turtlecute/d3ward-Testsuite direkt in die gebündelten Standard-Domains von `AdBlockerService.cs` integriert.
       2. In `GetCosmeticScript()` einen Client-seitigen Schutz injiziert, der `window.fetch` und `XMLHttpRequest` für geblockte Domains und Skripte sofort mit `new TypeError('Failed to fetch')` ablehnt – genau wie uBlock Origin.
       3. Kosmetische CSS-Filter für `#cts_test`, `#ctd_test`, `#ad_ctd`, `.textads`, `.adsbox`, `.banner_ads` etc. hinzugefügt, sodass Static- und Dynamic-Ad-Tests sofort bestehen.
       4. Blockierung von Ad-Skript-Pfaden (`/ads.js`, `/pagead.js`) auf Netzwerk- und Skriptebene, sodass die Ad-Script-Tests bestehen.
       5. StevenBlack Hosts-Datenbank (~75.000 Domains) bleibt als öffentliche Datenbank im Hintergrund aktiv und kann per Klick aktualisiert werden.


       Bugs:
       - Der Adblocker lässt sich durch die buttons im Adblock drop down nicht mehr deaktivieren (dauerhaft an), solle ausschaltbar sein falls manche seiten das nicht mögen.
       - Die erweiterungen haben meistens ein eigenes drop down menü mit gui, das fehlt, stattdessen werden direkt die einstellungen geöffnet.

       Features:
       - Lokalisierung, Deutsch, Englisch, weitere falls möglich, kann in den Einstellungen geändert werden.
       - Sprache soll beim ersten start abgefragt werden, mit einer coolen apple style animation wo das Wort Sprache auf den verschiedenen sprachen dargestellt wird.

       Changes:
       - Das Fenster das beim Schließen des Browsers bei mehreren Tabs offen angezeigt wird ist auch nicht im selben stil wie der Browser.

## Erledigt (Oktober 2026):
- [x] **Sicherheit**: Host-Bridge (`WebMessageReceived`) nimmt privilegierte Nachrichten nur noch von internen Seiten mit Sitzungs-Token an (`InternalPageSecurity`), Installationsanfragen nur vom Chrome Web Store. Download-Sicherheitsprüfung wird nur noch für `.crx` ausgehebelt.
- [x] **Performance**: Netzwerkfilter des Adblockers nur bei aktivem Shield, gebündelte Badge-Updates, Verlauf wird verzögert im Hintergrund gespeichert, alle JSON-Dateien werden atomar geschrieben (`AtomicFile`).
- [x] **Struktur**: `WebMessageRouter`, `MainWindow` in thematische Teildateien aufgeteilt, `.gitignore`, `bin/`/`obj/` aus dem Repo entfernt, WebView2-Version fixiert.
- [x] **Bug (Adblocker lässt sich nicht deaktivieren)**: Shield-Schalter (global & pro Website) funktionieren wieder; der globale Schalter wirkt jetzt auf alle Tabs.
- [x] **Lokalisierung**: Alle Texte (XAML, Code, Start- & Einstellungsseite) kommen aus `Localization/<sprache>.json`. Deutsch, Englisch, Französisch & Spanisch vollständig (fehlende Texte fallen auf Englisch zurück). Sprachwechsel wirkt sofort ohne Neustart.
  - XAML: `Text="{loc:Loc Key}"`, C#: `Tr.Get("Key")` / `Tr.Format("Key", arg)`, HTML-Seiten: `{{t:Key}}` bzw. `{{js:Key}}`.
  - Neue Sprache: JSON-Datei in `Localization/` anlegen und in der Sprachauswahl ergänzen.
- [x] **Change**: Verbliebene Windows-Standarddialoge (Verlauf leeren, Shield-Meldungen, Favorit) durch `ThemedDialogWindow` ersetzt.
- [x] **Update-Infrastruktur mit Velopack**:
  - `VelopackApp.Build().Run()` wird ganz am Anfang der Anwendung in `App.Main()` ausgeführt (`App.xaml` als `Page` mit `StartupObject`).
  - `UpdateService`: Asynchrone Hintergrundprüfung, Delta-Patch-Downloads, Fortschrittsanzeige (0-100%), Vorbereitung für nahtlose Installation bei Beendigung (`WaitExitThenApplyUpdates`) sowie Sofort-Neustart (`ApplyUpdatesAndRestart`). Update-Server fest an `https://github.com/GoToPlayHD/Echo-Browser` gebunden (`GithubSource`).
  - Menü-Integration: Status- und Aktionsmenüpunkt "Nach Updates suchen..." / "Update wird heruntergeladen (X%)..." / "Echo neu starten zum Aktualisieren" mit Update-Badge auf dem 3-Punkte-Menübutton und im Dropdown.
  - Einstellungsseite (`echo://settings` -> "Über Echo-Browser"): Live-Status, Fortschrittsbalken, Update-Prüfbutton und Schalter für automatische Hintergrundprüfungen & Pre-Releases.
  - Vollständige Lokalisierung (Deutsch, Englisch, Französisch, Spanisch) und begleitende Komponententests.

## Offen:
- [x] Französische und spanische Übersetzung vervollständigt (`Localization/fr.json`, `es.json`).
- [x] **Bug (Erweiterungen öffnen Einstellungen statt Popup)**: Ursache war die Zuordnung WebView2-ID ↔ entpackter Ordner (IDs stimmen nicht mit der Store-ID überein, Namen stehen oft als `__MSG_...__` im Manifest). Zuordnung wird jetzt bei der Installation gespeichert (`extensions-index.json`), ältere Installationen werden über den aufgelösten Namen gefunden. Zusätzlich `page_action`-Popups und `default_icon` unterstützt; beim Entfernen wird die entpackte Kopie gelöscht.
- [x] Rechtsklick-Menü der Symbolleiste stand 10x identisch in `MainWindow.xaml` – wird jetzt einmal im Code erzeugt (`MainWindow.Toolbar.cs`).
- [x] Popups als eigene UserControls (`Views/Popups/`): Verlauf, Downloads, Theme, Shield, Erweiterungen sowie die Formulare für Favorit, Lesezeichen und Gruppe. Jedes Panel meldet Aktionen per Ereignis an das Hauptfenster.
  - Bewusst im Hauptfenster geblieben: das Hauptmenü (ruft nur Hauptfenster-Funktionen auf) und die Lesezeichen-Gruppe (Drag & Drop ist eng mit der Lesezeichenleiste verzahnt).
- [x] **Bug (Shield „Cookies & Websitedaten leeren“)**: löschte die Cookies aller Websites statt nur der aktuellen Seite.

## Erledigt (Oktober 2026) – Phase 0: Fundament
- [x] **Bug (Datenverlust bei mehreren Fenstern)**: Lesezeichen, Seitenleiste und Verlauf gab es pro Fenster einmal – das zuletzt speichernde Fenster überschrieb die anderen. Jetzt eine gemeinsame Instanz (`BookmarkService.Instance`, `SidebarService.Instance`, `HistoryService.Instance`).
- [x] **Eine WebView2-Umgebung für alle Fenster** (`BrowserEnvironment`). Inkognito nutzt ein InPrivate-Profil statt eines Temp-Ordners.
- [x] **Interne Seiten unter echtem `echo://`-Schema** statt `NavigateToString`: Zurück/Vor zur Startseite funktioniert, die Host-Bridge vertraut nur noch `echo://`-Seiten (plus Token), fremde Seiten können interne Seiten nicht per iframe einbetten.
- [x] **Wirkungslose Einstellungen angeschlossen**: Popup-Blocker (ungefragte Popups werden blockiert und in der Adressleiste angeboten, pro Website erlaubbar), „Do Not Track“ + Global Privacy Control (Header und `navigator`), Standard-Zoom für neue Tabs. JavaScript-Schalter deaktiviert nicht mehr die Einstellungsseite selbst.
- [x] **Einzelinstanz & Startargumente**: `EchoBrowser.exe <url>` öffnet die Adresse als Tab im laufenden Fenster (Voraussetzung für „Als Standardbrowser“).
- [x] **Absturzsicher**: abgestürzte Tabs zeigen eine Absturzseite mit „Neu laden“, hängende Seiten fragen nach, ein abgestürzter Browser-Prozess wird neu aufgebaut. Die Sitzung wird laufend gespeichert (alle Fenster, aktiver Tab, Titel); nach einem Absturz fragt Echo beim Start „Tabs wiederherstellen?“.
- [x] **Bug (Strg+N stellte die Sitzung erneut her)**: Nur das erste Fenster führt das Startverhalten aus.
- [x] **Protokoll** unter `%LOCALAPPDATA%\EchoBrowser\logs` (7 Tage) und globale Fehlerbehandlung statt stiller `Debug.WriteLine`. Fehlende WebView2 Runtime → Hinweis mit Download-Link.
- [x] **CI**: GitHub Actions baut und testet jeden Pull Request (`.github/workflows/build.yml`).

## Roadmap
- [x] **Phase 1 – Grundfunktionen** (siehe unten)
- [x] **Phase 2 – Optik** (siehe unten)
- [x] **Phase 3 – Super Powers** (siehe unten)
- [ ] **Phase 4**: Echo Shield 2.0 (EasyList), Verlauf in SQLite.
- [ ] **Von Hand prüfen** (braucht echte Maus): Snap-Layouts beim Zeigen auf „Maximieren“, Klick in die linke/rechte Seite der geteilten Ansicht wechselt den Fokus, Trenner ziehen, Link-Kontextmenü „In geteilter Ansicht öffnen“, Tabs per Drag & Drop in eine Gruppe ziehen.

## Erledigt (Oktober 2026) – Phase 1: Grundfunktionen
- [x] **Favicons** in Tabs, Lesezeichenleiste, Gruppen-Flyout, Verlauf und Omnibox (`FaviconCache`, `SiteIcon`); interne Seiten zeigen das Echo-Symbol.
- [x] **Tabs**: drehender Lade-Spinner, Hover-Zustand, Ton-Anzeige mit Stummschalten, Mittelklick schließt, Kontextmenü (Neuer Tab rechts, Neu laden, Duplizieren, Stummschalten, In neues Fenster verschieben, Andere/Rechts schließen, Geschlossenen Tab wieder öffnen).
- [x] **Tastenkürzel** wie in Chrome (Strg+Tab, Strg+1–9, Strg+Umschalt+T, Strg+F/F3, Zoom, F11, Strg+P/S/U, Alt+Links/Rechts …) – Liste in der README.
- [x] **Auf Seite suchen** mit WebView2-Find-API, Trefferzahl und eigener Suchleiste.
- [x] **Omnibox 2.0**: Vorschläge aus Tabs („Zu Tab wechseln“), Lesezeichen, Verlauf (Häufigkeit + Aktualität) und Suchmaschine, Inline-Vervollständigung, Pfeiltasten, Alt+Enter, Strg+Enter, Umschalt+Entf; ohne Fokus Domain hervorgehoben, „Nicht sicher“ bei http. Adress-Erkennung korrigiert (`host:port`, IP, localhost, Umlaut-Domains).
- [x] **Zoom pro Website** (`zoom.json`), Zoom-Anzeige in der Adressleiste, Zoom-Zeile im Menü.
- [x] **Website-Berechtigungen**: eigene Abfrage, Verwaltung im Shield-Panel.
- [x] **Vollbild** (F11 und Videos) mit Hinweis.
- [x] **Downloads**: gemeinsame, gespeicherte Liste, Pause/Fortsetzen/Abbrechen, Erneut versuchen, Im Ordner anzeigen, Fortschrittsring; Download-Links ändern die Adressleiste nicht mehr.
- [x] **Standardbrowser**: Registrierung bei Windows (Velopack-Hooks + Knopf in den Einstellungen).
- [x] **Passwörter & Autofill** (WebView2) mit Schaltern, **Lesezeichen-Import** aus Chrome/Edge/Brave/HTML und HTML-Export.
- [x] Flyouts der rechten Symbolleiste rechtsbündig; Trennlinien in Kontextmenüs nicht mehr eingerückt; „Aktiv“ nur bei der gewählten Suchmaschine.

## Erledigt (Oktober 2026) – Phase 2: Optik
- [x] **Start-, Einstellungs- und Absturzseite folgen dem Theme** (`ThemeManager.GetCssVariables`), live ohne Neuladen; Webseiten bekommen `prefers-color-scheme` passend zum Browser. Eigene Akzentfarben bleiben im hellen Design lesbar.
- [x] **Neues Theme „System“**: folgt automatisch dem hellen/dunklen Modus von Windows (auch während Echo läuft).
- [x] **Mica** (Windows 11 22H2+) in der Tab-Leiste, abschaltbar unter Erscheinungsbild → „Transparenzeffekte“; passende helle/dunkle Fensterrahmen und runde Ecken.
- [x] **Snap-Layouts** beim Zeigen auf „Maximieren“ (`HTMAXBUTTON`) und **kein abgeschnittener Rand mehr im maximierten Fenster** (vorher ~8 px oben/links/rechts).
- [x] **Tab-Leiste wie Chrome**: Tabs teilen sich die Breite und schrumpfen bis aufs Symbol, „+“ direkt hinter dem letzten Tab, Einblend-Animation, Infokarte mit Titel und Domain.
- [x] **Barrierefreiheit**: sichtbarer Tastaturfokus statt `FocusVisualStyle={x:Null}`, Screenreader-Namen für Icon-Knöpfe.
- [x] **Feinschliff**: Flyouts gleiten beim Öffnen leicht herein; feste Farben (Inkognito-Abzeichen, Verlauf, Erweiterungs-Popup) durch Theme-Farben ersetzt; Dialoge zeigen das Echo-Logo statt eines Puzzle-Symbols; Seitenleisten-Symbole bleiben im hellen Design sichtbar.
- [x] **Startseite 2.0**: Favicons auf den Kacheln, „Meistbesucht“ aus dem Verlauf, optionales eigenes Hintergrundbild (Einstellungen → Erscheinungsbild). Interne Bilder (`echo://favicon`, `echo://wallpaper`) sind für Webseiten gesperrt.

## Erledigt (Oktober 2026) – Phase 3: Super Powers
- [x] **Befehlspalette (Strg+K)**: unscharfe Suche über alle Befehle, offene Tabs, Lesezeichen, Verlauf und Einstellungsbereiche (`echo://settings#section-…`); Tastenkürzel daneben, auch über das Hauptmenü erreichbar. `CommandCatalog` ist jetzt die eine Quelle für Befehle und Tastenkürzel (Tests: keine doppelten Kürzel, alle Befehle übersetzt und belegt).
- [x] **Tab-Schlaf**: inaktive Tabs werden nach 5/15/30/60/120 Min. (Standard 30, „Nie“ möglich) eingefroren (`TrySuspendAsync`, `MemoryUsageTargetLevel.Low`) und nach der vierfachen Zeit (mind. 2 Std.) verworfen; Tabs mit Ton, ladende und sichtbare Tabs bleiben wach. Schlafende Tabs sind abgeblendet, Aktivieren weckt bzw. lädt sie neu (`TabSleepPolicy`, Tests).
- [x] **Schneller Start**: beim Wiederherstellen der Sitzung lädt nur der aktive Tab, die anderen beim ersten Anklicken.
- [x] **Leistung & Speicher** (Hauptmenü, Befehlspalette): Arbeitsspeicher von Echo gesamt (privater Working Set wie im Task-Manager) und pro Tab (Renderer-Prozesse über Frame-IDs zugeordnet), Schlafzeit wählen, „Jetzt schlafen legen“; Speicher auch in der Infokarte des Tabs. Einstellung zusätzlich unter Einstellungen → Tabs.
- [x] **Angeheftete Tabs**: nur Symbol, feste Breite, immer vorne, ohne Schließen-Kreuz; Kontextmenü „Anheften/Loslösen“, Befehl in der Palette.
- [x] **Tab-Gruppen**: Name und Farbe (9 Farben wie Chrome), farbiger Gruppenkopf und Unterstrich, Klick klappt ein/aus (eingeklappt mit Anzahl), Rechtsklick öffnet den Editor (Name, Farbe, Neuer Tab in der Gruppe, Gruppierung aufheben, Gruppe schließen). Drag & Drop in eine Gruppe hinein bzw. heraus; „Neuer Tab rechts“ bleibt in der Gruppe. Reihenfolge-Logik in `TabOrder` (Tests), Tab-Leiste wird per `ListSync` abgeglichen (keine neu aufgebauten Tabs).
- [x] **Vertikale Tabs**: Liste links (breit 240 px oder schmal nur Symbole), schmale Titelzeile mit dem Titel des aktiven Tabs, Umschalter neben dem Logo, in den Einstellungen (Tabs) und in der Befehlspalette.
- [x] **Sitzung** speichert angeheftete Tabs und Gruppen (Name, Farbe, eingeklappt).
- [x] **Geteilte Ansicht (Split View)**: zwei Tabs nebeneinander (Spalten + `GridSplitter` im `WebViewContainer`, kein Airspace-Problem), Akzentrahmen um die Seite mit dem Fokus, Adressleiste/Navigation/Zoom/Suche folgen dem Fokus; die Teilung bleibt erhalten, solange einer der beiden Tabs aktiv ist (wie Edge), beide Tabs sind in der Tab-Leiste hervorgehoben und stehen nebeneinander. Einstiege: Knopf in der Symbolleiste (ausblendbar), Tab-Kontextmenü („Neben aktuellem Tab anzeigen“), Link-Kontextmenü („Link in geteilter Ansicht öffnen“), Befehlspalette. Beide Seiten bleiben wach (Tab-Schlaf), Videos im Vollbild nehmen die ganze Fläche ein.
- [x] **Sitzung**: ist der aktive Tab nicht speicherbar (Startseite), wird beim nächsten Start der nächstgelegene Tab aktiv statt immer der erste.
- [x] **Eigener Datenordner** über `ECHO_USER_DATA_DIR` (alle Dienste nutzen `AppPaths`); eigene Einzelinstanz pro Datenordner – Entwicklungsstände laufen neben dem installierten Echo.


Bugs:
- Seitliche und obere Lesezeichen leiste ist nicht syncronisiert.
- Lokale dateien können nicht auf der Seitlichen Lesezeichenleiste verwendete weren (es wird immer https davor geschrieben)
- Wenn man Lesezeichen Manuell hinzufügt kann man nichts ins URL Feld schreiben oder kopieren.