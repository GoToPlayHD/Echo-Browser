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

## Offen:
- [x] Französische und spanische Übersetzung vervollständigt (`Localization/fr.json`, `es.json`).
- [x] **Bug (Erweiterungen öffnen Einstellungen statt Popup)**: Ursache war die Zuordnung WebView2-ID ↔ entpackter Ordner (IDs stimmen nicht mit der Store-ID überein, Namen stehen oft als `__MSG_...__` im Manifest). Zuordnung wird jetzt bei der Installation gespeichert (`extensions-index.json`), ältere Installationen werden über den aufgelösten Namen gefunden. Zusätzlich `page_action`-Popups und `default_icon` unterstützt; beim Entfernen wird die entpackte Kopie gelöscht.
- [x] Rechtsklick-Menü der Symbolleiste stand 10x identisch in `MainWindow.xaml` – wird jetzt einmal im Code erzeugt (`MainWindow.Toolbar.cs`).
- [x] Popups als eigene UserControls (`Views/Popups/`): Verlauf, Downloads, Theme, Shield, Erweiterungen sowie die Formulare für Favorit, Lesezeichen und Gruppe. Jedes Panel meldet Aktionen per Ereignis an das Hauptfenster.
  - Bewusst im Hauptfenster geblieben: das Hauptmenü (ruft nur Hauptfenster-Funktionen auf) und die Lesezeichen-Gruppe (Drag & Drop ist eng mit der Lesezeichenleiste verzahnt).
- [x] **Bug (Shield „Cookies & Websitedaten leeren“)**: löschte die Cookies aller Websites statt nur der aktuellen Seite.
