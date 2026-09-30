# Echo-Browser TODO

## Erledigt:
- [x] **Bug behoben**: Beim Rechtsklick wurde links neben dem Text eine weiße Box angezeigt (Default Windows Aero Icon-Gutter). Es wurde ein modernes, dunkles, konsistentes ControlTemplate für `ContextMenu`, `MenuItem` und `Separator` im Silver/Anthracite-Theme implementiert (Transparenter Icon-Bereich ohne weiße Boxen, weiche Hover-Effekte, abgerundete Ecken, Drop-Shadow).
- [x] **Settings Menü ausgebaut**: Vollwertige, moderne Einstellungs-Zentrale (`echo://settings` und Menü "Einstellungen & Über Echo" / `Ctrl+,`) wie in Chrome und Firefox:
  - **Allgemein & Startverhalten**: Neue Tab-Seite öffnen, Vorherige Sitzung wiederherstellen, Bestimmte Start-URL, Home-Button ein/aus, Startseiten-Verknüpfungen ein/aus.
  - **Suchmaschine**: Standardsuchmaschine wählen (DuckDuckGo, Google, Bing, Ecosia, Brave, Startpage) & Suchvorschläge-Toggle.
  - **Erscheinungsbild**: Themes (Silber & Anthrazit, Midnight OLED, Titanium Light, Cobalt Slate), Akzentfarben (Silber, Eisblau, Smaragd, Bernstein, Rubin, Amethyst, Custom Hex), Lesezeichenleiste, Seitenleiste, Standard-Zoomstufe.
  - **Datenschutz & Sicherheit**: Echo Shield Stufen (Ausgewogen, Strikt, Deaktiviert), Pop-up-Blocker, JavaScript ein/aus, Do Not Track (DNT), modales Tool zur vollständigen Bereinigung von Browserdaten (Verlauf, Cookies, Cache).
  - **Downloads**: Download-Verzeichnis wählen & anpassen (inkl. Windows Ordnerauswahl-Dialog und Explorer-Verknüpfung), Option zur Bestätigung des Speicherorts vor jedem Download.
  - **Tabs & Verhalten**: Neue Tabs im Hintergrund öffnen, Warnung beim Schließen mehrerer Tabs, Einstellungen auf Werkseinstellungen zurücksetzen.
  - **Über Echo**: Versionsdetails (v1.2), Chromium/WebView2-Versionsanzeige, Plattform-Info, Update-Status-Badge.
- [x] **Lesezeichen-Gruppen Drag & Drop**: Lesezeichen können jetzt per Drag & Drop aus Gruppen herausgezogen und wieder auf der Lesezeichenleiste (oder in anderen Gruppen) platziert werden:
  - Interaktives Gruppen-Flyout (`popupBookmarkGroup`) im modernen Echo-Design mit Drag-Handles, Zähler-Badge und Hover-Effekten.
  - Direkte OLE Drag & Drop Unterstützung: Lesezeichen im Gruppen-Flyout anklicken und auf die Hauptleiste ziehen, um sie sofort aus der Gruppe herauszunehmen und an gewünschter Stelle abzulegen.
  - Ebenso Unterstützung zum Umordnen innerhalb der Gruppe sowie Verschieben zwischen verschiedenen Gruppen.
  - Kontextmenü-Option "Aus Gruppe auf Leiste verschieben" als Rechtsklick-Alternative.