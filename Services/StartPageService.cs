using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using EchoBrowser.Models;

namespace EchoBrowser.Services
{
    public static class StartPageService
    {
        public const string StartPageUrl = "echo://start";

        public static string GetStartPageHtml(bool isIncognito = false)
        {
            var settings = AppSettingsService.Instance.Settings;
            string currentEngine = settings.SearchEngine ?? "duckduckgo";
            bool showFavorites = !isIncognito && settings.IsStartpageFavoritesVisible;
            string shortcutsJson = JsonSerializer.Serialize(settings.StartpageShortcuts ?? new());

            string incognitoExtra = isIncognito 
                ? @"<div class=""incognito-tag"">
                        <svg width=""16"" height=""16"" viewBox=""0 0 24 24"" fill=""none"" stroke=""#9CA3AF"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                            <path d=""M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z""/>
                            <circle cx=""12"" cy=""12"" r=""3""/>
                        </svg>
                        <span>{{t:Start_Incognito}}</span>
                    </div>
                    <p class=""incognito-notice"">{{t:Start_IncognitoNotice}}</p>"
                : "";

            // Erst übersetzen, dann Nutzerdaten (Verknüpfungen) einsetzen – so werden darin keine Platzhalter ersetzt
            string html = RawHtmlTemplate.Replace("##INCOGNITO_EXTRA##", incognitoExtra);
            html = PageLocalizer.Apply(html);
            html = html.Replace("##TITLE##", System.Net.WebUtility.HtmlEncode(Tr.Get(isIncognito ? "Tab_NewTabIncognito" : "Tab_NewTab")));
            html = html.Replace("##CURRENT_ENGINE##", currentEngine);
            html = html.Replace("##SHOW_FAVORITES##", showFavorites ? "true" : "false");
            html = html.Replace("##SHORTCUTS_SECTION_CLASS##", showFavorites ? "" : "hidden");
            html = html.Replace("##TOGGLE_TEXT##", System.Net.WebUtility.HtmlEncode(Tr.Get(showFavorites ? "Start_HideShortcuts" : "Start_ShowShortcuts")));
            html = html.Replace("##SHORTCUTS_JSON##", shortcutsJson);

            // "Meistbesucht" aus dem Verlauf (nicht im Inkognito-Modus)
            var mostVisited = isIncognito
                ? new List<MostVisitedSite>()
                : MostVisited.Calculate(
                    HistoryService.Instance.Entries.Select(h => new SuggestionSource(h.Title, h.Url, h.VisitedAt)),
                    (settings.StartpageShortcuts ?? new()).Select(s => s.Url),
                    DateTime.Now);
            html = html.Replace("##MOST_VISITED_JSON##", JsonSerializer.Serialize(mostVisited));

            // Eigenes Hintergrundbild
            string wallpaper = settings.StartpageBackgroundPath ?? "";
            bool hasWallpaper = wallpaper.Length > 0 && System.IO.File.Exists(wallpaper);
            html = html.Replace("##WALLPAPER_CLASS##", hasWallpaper ? "has-wallpaper" : "");
            html = html.Replace("##WALLPAPER_VERSION##", hasWallpaper ? System.IO.File.GetLastWriteTimeUtc(wallpaper).Ticks.ToString() : "0");
            html = html.Replace("##THEME_CSS##", ThemeManager.Instance.GetCssVariables());
            html = InternalPageSecurity.InjectToken(html);

            return html;
        }

        private const string RawHtmlTemplate = @"<!DOCTYPE html>
<html lang=""{{lang}}"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>##TITLE##</title>
    <!-- Farben des aktuellen Echo-Themes (ThemeManager.GetCssVariables), werden bei Theme-Wechsel live ersetzt -->
    <style id=""echo-theme"">##THEME_CSS##</style>
    <style>
        :root {
            --bg-color: var(--echo-window);
            --surface-color: var(--echo-surface);
            --surface-hover: var(--echo-surface-hover);
            --surface-raised: var(--echo-surface-active);
            --input-bg: var(--echo-omnibox);
            --border-color: var(--echo-border);
            --border-focus: var(--echo-focus);
            --text-primary: var(--echo-text);
            --text-secondary: var(--echo-text-secondary);
            --text-muted: var(--echo-text-muted);
            --accent-silver: var(--echo-accent);
            --accent-bright: var(--echo-accent-bright);
            --accent-glow: color-mix(in srgb, var(--echo-accent) 20%, transparent);
            --danger-color: var(--echo-danger);
        }

        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            user-select: none;
        }

        /* Eigenes Hintergrundbild, leicht abgedunkelt bzw. aufgehellt für lesbare Inhalte */
        body.has-wallpaper {
            background-image: linear-gradient(color-mix(in srgb, var(--echo-window) 45%, transparent), color-mix(in srgb, var(--echo-window) 45%, transparent)),
                              url('echo://wallpaper/?v=##WALLPAPER_VERSION##');
            background-size: cover;
            background-position: center;
            background-attachment: fixed;
        }

        .section-label {
            font-size: 12px;
            font-weight: 600;
            letter-spacing: 0.3px;
            color: var(--text-muted);
            margin: 0 4px 10px;
        }

        .most-visited-section {
            margin-top: 22px;
        }

        .tile-favicon {
            width: 22px;
            height: 22px;
            display: none;
        }

        body {
            background-color: var(--bg-color);
            background-image: radial-gradient(circle at 50% 32%, var(--echo-surface) 0%, var(--echo-window) 72%);
            color: var(--text-primary);
            min-height: 100vh;
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            overflow-x: hidden;
        }

        .container {
            width: 100%;
            max-width: 820px;
            padding: 20px;
            display: flex;
            flex-direction: column;
            align-items: center;
            margin-top: -30px;
        }

        /* Top Half: Logo */
        .logo-container {
            display: flex;
            flex-direction: column;
            align-items: center;
            margin-bottom: 28px;
            cursor: default;
        }

        .echo-logo-svg {
            width: 84px;
            height: 84px;
            margin-bottom: 12px;
            filter: drop-shadow(0 6px 16px rgba(0, 0, 0, calc(0.5 * var(--echo-shadow-k))));
            transition: transform 0.3s cubic-bezier(0.16, 1, 0.3, 1), filter 0.3s ease;
        }

        .logo-container:hover .echo-logo-svg {
            transform: scale(1.06) rotate(3deg);
            filter: drop-shadow(0 10px 24px rgba(196, 199, 204, 0.3));
        }

        .logo-text {
            font-size: 74px;
            font-weight: 800;
            letter-spacing: 8px;
            text-transform: uppercase;
            background: linear-gradient(135deg, var(--echo-logo-1) 0%, var(--echo-logo-2) 30%, var(--echo-logo-3) 70%, var(--echo-logo-4) 100%);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            filter: drop-shadow(0 6px 16px rgba(0, 0, 0, calc(0.5 * var(--echo-shadow-k))));
            transition: transform 0.3s cubic-bezier(0.16, 1, 0.3, 1), filter 0.3s ease;
        }

        .logo-text:hover {
            transform: scale(1.02);
            filter: drop-shadow(0 8px 24px rgba(196, 199, 204, 0.25));
        }

        .incognito-tag {
            margin-top: 12px;
            padding: 5px 14px;
            background: var(--surface-color);
            border: 1px solid var(--border-color);
            border-radius: 14px;
            color: var(--accent-silver);
            font-size: 13px;
            font-weight: 600;
            display: inline-flex;
            align-items: center;
            gap: 7px;
            box-shadow: 0 2px 8px rgba(0, 0, 0, calc(0.35 * var(--echo-shadow-k)));
        }

        .incognito-notice {
            margin-top: 8px;
            color: var(--text-secondary);
            font-size: 13px;
            max-width: 480px;
            text-align: center;
            line-height: 1.45;
        }

        /* Search Section */
        .search-wrapper {
            width: 100%;
            max-width: 650px;
            position: relative;
        }

        .search-box {
            width: 100%;
            height: 52px;
            background: var(--surface-color);
            border: 1px solid var(--border-color);
            border-radius: 26px;
            display: flex;
            align-items: center;
            padding: 0 16px;
            box-shadow: 0 4px 16px rgba(0, 0, 0, calc(0.28 * var(--echo-shadow-k)));
            transition: all 0.25s cubic-bezier(0.16, 1, 0.3, 1);
        }

        .search-box:hover {
            border-color: var(--echo-accent-dim);
            background: var(--surface-hover);
            box-shadow: 0 6px 20px rgba(0, 0, 0, calc(0.35 * var(--echo-shadow-k)));
        }

        .search-box:focus-within {
            border-color: var(--border-focus);
            background: var(--surface-hover);
            box-shadow: 0 0 0 3px var(--accent-glow), 0 8px 28px rgba(0, 0, 0, calc(0.45 * var(--echo-shadow-k)));
        }

        .search-icon {
            width: 20px;
            height: 20px;
            fill: var(--accent-silver);
            margin-right: 12px;
            flex-shrink: 0;
            opacity: 0.8;
            transition: opacity 0.2s ease;
        }

        .search-box:focus-within .search-icon {
            opacity: 1;
            fill: var(--accent-bright);
        }

        .search-input {
            flex: 1;
            background: transparent;
            border: none;
            outline: none;
            color: var(--text-primary);
            font-size: 16px;
            user-select: text;
        }

        .search-input::placeholder {
            color: var(--text-muted);
            font-size: 15px;
        }

        .clear-btn {
            display: none;
            cursor: pointer;
            background: transparent;
            border: none;
            outline: none;
            color: var(--text-muted);
            font-size: 18px;
            padding: 4px;
            margin-right: 6px;
            border-radius: 50%;
            transition: color 0.15s;
        }

        .clear-btn:hover {
            color: var(--text-primary);
        }

        /* Engine Dropdown inside search bar */
        .engine-dropdown-wrap {
            position: relative;
            display: flex;
            align-items: center;
        }

        .engine-select {
            background: var(--input-bg);
            color: var(--accent-silver);
            border: 1px solid var(--border-color);
            border-radius: 14px;
            padding: 5px 12px;
            font-size: 12.5px;
            font-weight: 600;
            cursor: pointer;
            outline: none;
            transition: all 0.2s;
            appearance: none;
            -webkit-appearance: none;
            padding-right: 24px;
            background-image: url('data:image/svg+xml;utf8,<svg fill=""%23C4C7CC"" height=""12"" viewBox=""0 0 24 24"" width=""12"" xmlns=""http://www.w3.org/2000/svg""><path d=""M7 10l5 5 5-5z""/></svg>');
            background-repeat: no-repeat;
            background-position: right 8px center;
        }

        .engine-select:hover {
            border-color: var(--accent-silver);
            color: var(--accent-bright);
            background-color: var(--surface-hover);
        }

        .engine-select option {
            background: var(--surface-color);
            color: var(--text-primary);
        }

        /* Buttons Row & Shortcuts Header */
        .controls-row {
            display: flex;
            align-items: center;
            justify-content: space-between;
            width: 100%;
            max-width: 650px;
            margin-top: 18px;
            padding: 0 4px;
        }

        .search-submit-btn {
            background: var(--surface-color);
            color: var(--text-primary);
            border: 1px solid var(--border-color);
            border-radius: 18px;
            padding: 8px 22px;
            font-size: 13.5px;
            font-weight: 500;
            cursor: pointer;
            transition: all 0.2s cubic-bezier(0.16, 1, 0.3, 1);
            display: flex;
            align-items: center;
            gap: 8px;
            box-shadow: 0 2px 6px rgba(0, 0, 0, calc(0.2 * var(--echo-shadow-k)));
        }

        .search-submit-btn:hover {
            background: var(--surface-hover);
            border-color: var(--accent-silver);
            color: var(--accent-bright);
            transform: translateY(-1px);
        }

        .search-submit-btn:active {
            transform: translateY(1px);
        }

        .toggle-shortcuts-btn {
            background: transparent;
            border: none;
            outline: none;
            color: var(--text-muted);
            font-size: 12.5px;
            font-weight: 500;
            cursor: pointer;
            transition: color 0.15s;
            display: flex;
            align-items: center;
            gap: 6px;
        }

        .toggle-shortcuts-btn:hover {
            color: var(--accent-silver);
        }

        /* Shortcuts Container */
        .shortcuts-section {
            width: 100%;
            max-width: 650px;
            margin-top: 28px;
            transition: opacity 0.25s ease;
        }

        .shortcuts-section.hidden {
            display: none;
        }

        .shortcuts-grid {
            display: grid;
            grid-template-columns: repeat(6, 1fr);
            gap: 12px;
            width: 100%;
        }

        @media (max-width: 680px) {
            .shortcuts-grid {
                grid-template-columns: repeat(3, 1fr);
            }
        }

        .shortcut-item {
            position: relative;
            display: flex;
            flex-direction: column;
            align-items: center;
            padding: 10px 4px;
            border-radius: 12px;
            transition: all 0.2s cubic-bezier(0.16, 1, 0.3, 1);
            cursor: pointer;
        }

        .shortcut-item:hover {
            background: var(--surface-hover);
            transform: translateY(-3px);
        }

        .shortcut-icon-circle {
            width: 46px;
            height: 46px;
            border-radius: 50%;
            background: var(--surface-color);
            border: 1px solid var(--border-color);
            display: flex;
            align-items: center;
            justify-content: center;
            margin-bottom: 8px;
            box-shadow: 0 4px 10px rgba(0, 0, 0, calc(0.25 * var(--echo-shadow-k)));
            transition: all 0.2s ease;
        }

        .shortcut-item:hover .shortcut-icon-circle {
            border-color: var(--accent-silver);
            background: var(--surface-raised);
            box-shadow: 0 6px 16px rgba(0, 0, 0, calc(0.35 * var(--echo-shadow-k)));
        }

        .shortcut-icon {
            width: 20px;
            height: 20px;
            fill: var(--accent-silver);
        }

        .shortcut-title {
            font-size: 11.5px;
            font-weight: 500;
            color: var(--text-secondary);
            text-align: center;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
            max-width: 84px;
        }

        .shortcut-item:hover .shortcut-title {
            color: var(--text-primary);
        }

        /* Edit button overlay on shortcut */
        .shortcut-edit-btn {
            position: absolute;
            top: 4px;
            right: 4px;
            width: 22px;
            height: 22px;
            border-radius: 50%;
            background: var(--surface-raised);
            border: 1px solid var(--border-color);
            color: var(--text-secondary);
            font-size: 11px;
            display: none;
            align-items: center;
            justify-content: center;
            cursor: pointer;
            box-shadow: 0 2px 5px rgba(0, 0, 0, calc(0.4 * var(--echo-shadow-k)));
        }

        .shortcut-item:hover .shortcut-edit-btn {
            display: flex;
        }

        .shortcut-edit-btn:hover {
            background: var(--surface-hover);
            color: var(--accent-bright);
            border-color: var(--accent-silver);
        }

        /* Add Shortcut Tile */
        .shortcut-add-circle {
            border: 1px dashed var(--border-color);
            background: transparent;
        }

        .shortcut-item:hover .shortcut-add-circle {
            border-color: var(--accent-silver);
            border-style: solid;
        }

        /* Modal Dialog for Add/Edit Shortcut */
        .modal-overlay {
            position: fixed;
            top: 0; left: 0; right: 0; bottom: 0;
            background: rgba(0, 0, 0, calc(0.65 * var(--echo-shadow-k)));
            backdrop-filter: blur(4px);
            display: none;
            align-items: center;
            justify-content: center;
            z-index: 1000;
        }

        .modal-card {
            width: 360px;
            background: var(--surface-color);
            border: 1px solid var(--border-color);
            border-radius: 14px;
            padding: 22px;
            box-shadow: 0 12px 36px rgba(0, 0, 0, calc(0.6 * var(--echo-shadow-k)));
        }

        .modal-title {
            font-size: 16px;
            font-weight: 700;
            margin-bottom: 14px;
            color: var(--text-primary);
        }

        .modal-field {
            margin-bottom: 12px;
        }

        .modal-label {
            font-size: 11.5px;
            font-weight: 600;
            color: var(--text-secondary);
            margin-bottom: 4px;
            display: block;
        }

        .modal-input {
            width: 100%;
            height: 36px;
            background: var(--input-bg);
            border: 1px solid var(--border-color);
            border-radius: 8px;
            padding: 0 10px;
            color: var(--text-primary);
            font-size: 13.5px;
            outline: none;
        }

        .modal-input:focus {
            border-color: var(--accent-silver);
        }

        .modal-actions {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-top: 18px;
        }

        .modal-btn {
            padding: 7px 16px;
            border-radius: 8px;
            font-size: 13px;
            font-weight: 500;
            cursor: pointer;
            border: none;
            outline: none;
        }

        .modal-btn-cancel {
            background: transparent;
            color: var(--text-muted);
        }

        .modal-btn-cancel:hover {
            color: var(--text-primary);
        }

        .modal-btn-delete {
            background: transparent;
            color: var(--danger-color);
            margin-right: auto;
        }

        .modal-btn-delete:hover {
            text-decoration: underline;
        }

        .modal-btn-save {
            background: var(--surface-raised);
            color: var(--text-primary);
            border: 1px solid var(--accent-silver);
        }

        .modal-btn-save:hover {
            background: var(--surface-hover);
        }
    </style>
</head>
<body class=""##WALLPAPER_CLASS##"">
    <div class=""container"">
        <!-- Top Half: Logo (SVG Emblem + Typography) -->
        <div class=""logo-container"">
            <svg class=""echo-logo-svg"" viewBox=""0 0 512 512"" xmlns=""http://www.w3.org/2000/svg"">
                <defs>
                    <linearGradient id=""bgGrad"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""100%"">
                        <stop offset=""0%"" stop-color=""#24272D""/>
                        <stop offset=""100%"" stop-color=""#141518""/>
                    </linearGradient>
                    <linearGradient id=""silverGrad"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""100%"">
                        <stop offset=""0%"" stop-color=""#FFFFFF""/>
                        <stop offset=""25%"" stop-color=""#E2E8F0""/>
                        <stop offset=""60%"" stop-color=""#94A3B8""/>
                        <stop offset=""100%"" stop-color=""#475569""/>
                    </linearGradient>
                    <linearGradient id=""accentGrad"" x1=""0%"" y1=""100%"" x2=""100%"" y2=""0%"">
                        <stop offset=""0%"" stop-color=""#64748B""/>
                        <stop offset=""100%"" stop-color=""#CBD5E1""/>
                    </linearGradient>
                    <filter id=""shadow"" x=""-10%"" y=""-10%"" width=""130%"" height=""130%"">
                        <feDropShadow dx=""0"" dy=""8"" stdDeviation=""12"" flood-color=""#000000"" flood-opacity=""0.6""/>
                    </filter>
                </defs>
                <rect width=""512"" height=""512"" rx=""112"" fill=""url(#bgGrad)""/>
                <rect width=""504"" height=""504"" x=""4"" y=""4"" rx=""108"" fill=""none"" stroke=""#334155"" stroke-width=""2"" opacity=""0.6""/>
                <g filter=""url(#shadow)"">
                    <path d=""M 256 96 C 344.36 96 416 167.64 416 256 C 416 344.36 344.36 416 256 416 C 182.26 416 120.35 365.98 102.32 298 L 164.21 298 C 179.6 331.63 214.95 354 256 354 C 310.12 354 354 310.12 354 256 C 354 201.88 310.12 158 256 158 C 214.95 158 179.6 180.37 164.21 214 L 102.32 214 C 120.35 146.02 182.26 96 256 96 Z"" fill=""url(#silverGrad)""/>
                    <rect x=""180"" y=""232"" width=""130"" height=""48"" rx=""24"" fill=""url(#accentGrad)""/>
                    <circle cx=""120"" cy=""256"" r=""24"" fill=""url(#silverGrad)""/>
                </g>
            </svg>
            <h1 class=""logo-text"">ECHO</h1>
            ##INCOGNITO_EXTRA##
        </div>

        <!-- Lower Half: Search Box -->
        <div class=""search-wrapper"">
            <div class=""search-box"">
                <svg class=""search-icon"" viewBox=""0 0 24 24"">
                    <path d=""M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z""/>
                </svg>
                <input type=""text"" 
                       id=""searchInput"" 
                       class=""search-input"" 
                       placeholder=""{{t:Start_SearchPlaceholder}}"" 
                       autocomplete=""off"" 
                       autofocus>
                <button id=""clearBtn"" class=""clear-btn"" title=""{{t:Start_Delete}}"">&#x2715;</button>

                <!-- Search Engine Selector Dropdown -->
                <div class=""engine-dropdown-wrap"">
                    <select id=""engineSelect"" class=""engine-select"" title=""{{t:Start_ChooseEngine}}"">
                        <option value=""duckduckgo"">DuckDuckGo</option>
                        <option value=""google"">Google</option>
                        <option value=""bing"">Bing</option>
                        <option value=""ecosia"">Ecosia</option>
                        <option value=""brave"">Brave Search</option>
                        <option value=""startpage"">Startpage</option>
                    </select>
                </div>
            </div>
        </div>

        <!-- Controls Row: Search Submit & Toggle Shortcuts -->
        <div class=""controls-row"">
            <button id=""btnSearch"" class=""search-submit-btn"">
                <span>{{t:Start_Search}}</span>
            </button>
            <button id=""btnToggleShortcuts"" class=""toggle-shortcuts-btn"">
                <span id=""toggleShortcutsText"">##TOGGLE_TEXT##</span>
            </button>
        </div>

        <!-- Quick Access Shortcuts Section -->
        <div id=""shortcutsSection"" class=""shortcuts-section ##SHORTCUTS_SECTION_CLASS##"">
            <div id=""shortcutsGrid"" class=""shortcuts-grid"">
                <!-- Injected via JavaScript -->
            </div>
        </div>

        <!-- Meistbesucht (aus dem Verlauf) -->
        <div id=""mostVisitedSection"" class=""shortcuts-section most-visited-section ##SHORTCUTS_SECTION_CLASS##"">
            <div class=""section-label"">{{t:Start_MostVisited}}</div>
            <div id=""mostVisitedGrid"" class=""shortcuts-grid""></div>
        </div>
    </div>

    <!-- Modal Dialog for Add / Edit Shortcut -->
    <div id=""shortcutModal"" class=""modal-overlay"">
        <div class=""modal-card"">
            <h3 id=""modalHeading"" class=""modal-title"">{{t:Start_AddShortcut}}</h3>
            <div class=""modal-field"">
                <label class=""modal-label"">{{t:Bookmark_Name}}</label>
                <input type=""text"" id=""modalTitleInput"" class=""modal-input"" placeholder=""{{t:Start_NameExample}}"">
            </div>
            <div class=""modal-field"">
                <label class=""modal-label"">{{t:Bookmark_Url}}</label>
                <input type=""text"" id=""modalUrlInput"" class=""modal-input"" placeholder=""https://"">
            </div>
            <div class=""modal-actions"">
                <button id=""modalDeleteBtn"" class=""modal-btn modal-btn-delete"" style=""display:none;"">{{t:Start_Delete}}</button>
                <button id=""modalCancelBtn"" class=""modal-btn modal-btn-cancel"">{{t:Dialog_Cancel}}</button>
                <button id=""modalSaveBtn"" class=""modal-btn modal-btn-save"">{{t:Common_Save}}</button>
            </div>
        </div>
    </div>

    <script>
        const input = document.getElementById('searchInput');
        const clearBtn = document.getElementById('clearBtn');
        const btnSearch = document.getElementById('btnSearch');
        const engineSelect = document.getElementById('engineSelect');
        const btnToggleShortcuts = document.getElementById('btnToggleShortcuts');
        const toggleShortcutsText = document.getElementById('toggleShortcutsText');
        const shortcutsSection = document.getElementById('shortcutsSection');
        const shortcutsGrid = document.getElementById('shortcutsGrid');

        // Modal elements
        const shortcutModal = document.getElementById('shortcutModal');
        const modalHeading = document.getElementById('modalHeading');
        const modalTitleInput = document.getElementById('modalTitleInput');
        const modalUrlInput = document.getElementById('modalUrlInput');
        const modalDeleteBtn = document.getElementById('modalDeleteBtn');
        const modalCancelBtn = document.getElementById('modalCancelBtn');
        const modalSaveBtn = document.getElementById('modalSaveBtn');

        let currentEditingIndex = -1;
        let isShortcutsVisible = ##SHOW_FAVORITES##;
        let shortcuts = ##SHORTCUTS_JSON##;
        const mostVisited = ##MOST_VISITED_JSON##;
        const mostVisitedSection = document.getElementById('mostVisitedSection');
        const mostVisitedGrid = document.getElementById('mostVisitedGrid');

        // Kachel-Symbol: Anfangsbuchstabe, ersetzt durch das Website-Symbol, sobald es geladen ist
        function tileIcon(url, title) {
            const initial = title ? escapeHtml(title.charAt(0).toUpperCase()) : '?';
            let host = '';
            try { host = new URL(url).hostname.replace(/^www\./, ''); } catch (e) { }
            const favicon = host
                ? `<img class=""tile-favicon"" aria-hidden=""true"" src=""echo://favicon/${encodeURIComponent(host)}"" onload=""this.previousElementSibling.style.display='none'; this.style.display='block';"" onerror=""this.remove()"">`
                : '';
            return `<span style=""font-weight:700; font-size:15px; color:var(--accent-silver);"">${initial}</span>${favicon}`;
        }

        function renderMostVisited() {
            mostVisitedGrid.innerHTML = '';
            if (mostVisited.length === 0) {
                mostVisitedSection.style.display = 'none';
                return;
            }
            mostVisited.forEach(site => {
                const el = document.createElement('div');
                el.className = 'shortcut-item';
                el.title = site.Url;
                el.innerHTML = `
                    <div class=""shortcut-icon-circle"">${tileIcon(site.Url, site.Title)}</div>
                    <span class=""shortcut-title"">${escapeHtml(site.Title)}</span>`;
                el.addEventListener('click', () => navigateTo(site.Url));
                mostVisitedGrid.appendChild(el);
            });
        }

        // Initialize Engine
        engineSelect.value = ""##CURRENT_ENGINE##"";

        input.addEventListener('input', () => {
            clearBtn.style.display = input.value.length > 0 ? 'block' : 'none';
        });

        clearBtn.addEventListener('click', () => {
            input.value = '';
            clearBtn.style.display = 'none';
            input.focus();
        });

        // Engine Dropdown Change
        engineSelect.addEventListener('change', () => {
            const selected = engineSelect.value;
            postHostMessage({ type: 'setSearchEngine', engine: selected });
        });

        // Ob Adresse oder Suchbegriff, entscheidet der Browser (UrlHelper.ToNavigableUrl) – wie in der Adressleiste
        function performSearch() {
            const query = input.value.trim();
            if (query) navigateTo(query);
        }

        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') performSearch();
        });

        btnSearch.addEventListener('click', () => {
            performSearch();
        });

        // Toggle Shortcuts Visibility
        btnToggleShortcuts.addEventListener('click', () => {
            isShortcutsVisible = !isShortcutsVisible;
            if (isShortcutsVisible) {
                shortcutsSection.classList.remove('hidden');
                mostVisitedSection.classList.remove('hidden');
                toggleShortcutsText.textContent = {{js:Start_HideShortcuts}};
            } else {
                shortcutsSection.classList.add('hidden');
                mostVisitedSection.classList.add('hidden');
                toggleShortcutsText.textContent = {{js:Start_ShowShortcuts}};
            }
            postHostMessage({ type: 'toggleStartpageFavorites', visible: isShortcutsVisible });
        });

        // Render Shortcuts
        function renderShortcuts() {
            shortcutsGrid.innerHTML = '';

            shortcuts.forEach((item, index) => {
                const el = document.createElement('div');
                el.className = 'shortcut-item';
                el.title = item.Url;


                el.innerHTML = `
                    <div class=""shortcut-icon-circle"">${tileIcon(item.Url, item.Title)}</div>
                    <span class=""shortcut-title"">${escapeHtml(item.Title)}</span>
                    <button class=""shortcut-edit-btn"" title=""{{t:Start_Edit}}"">&#x270E;</button>
                `;

                // Left click on item navigates
                el.addEventListener('click', (e) => {
                    if (e.target.classList.contains('shortcut-edit-btn')) return;
                    navigateTo(item.Url);
                });

                // Edit button
                const editBtn = el.querySelector('.shortcut-edit-btn');
                editBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    openEditModal(index);
                });

                shortcutsGrid.appendChild(el);
            });

            // Add button tile
            const addTile = document.createElement('div');
            addTile.className = 'shortcut-item';
            addTile.title = {{js:Start_AddShortcut}};
            addTile.innerHTML = `
                <div class=""shortcut-icon-circle shortcut-add-circle"">
                    <span style=""font-size:20px; color:var(--text-secondary);"">+</span>
                </div>
                <span class=""shortcut-title"" style=""color:var(--text-secondary);"">{{t:Start_Add}}</span>
            `;
            addTile.addEventListener('click', () => {
                openAddModal();
            });
            shortcutsGrid.appendChild(addTile);
        }

        function openAddModal() {
            currentEditingIndex = -1;
            modalHeading.textContent = {{js:Start_AddShortcut}};
            modalTitleInput.value = '';
            modalUrlInput.value = 'https://';
            modalDeleteBtn.style.display = 'none';
            shortcutModal.style.display = 'flex';
            modalTitleInput.focus();
        }

        function openEditModal(index) {
            currentEditingIndex = index;
            const item = shortcuts[index];
            modalHeading.textContent = {{js:Start_EditShortcut}};
            modalTitleInput.value = item.Title;
            modalUrlInput.value = item.Url;
            modalDeleteBtn.style.display = 'block';
            shortcutModal.style.display = 'flex';
            modalTitleInput.focus();
        }

        modalCancelBtn.addEventListener('click', () => {
            shortcutModal.style.display = 'none';
        });

        modalDeleteBtn.addEventListener('click', () => {
            if (currentEditingIndex >= 0 && currentEditingIndex < shortcuts.length) {
                shortcuts.splice(currentEditingIndex, 1);
                saveShortcuts();
                renderShortcuts();
            }
            shortcutModal.style.display = 'none';
        });

        modalSaveBtn.addEventListener('click', () => {
            let title = modalTitleInput.value.trim();
            let url = modalUrlInput.value.trim();

            if (!url || url === 'https://' || url === 'http://') return;
            if (!url.startsWith('http://') && !url.startsWith('https://')) {
                url = 'https://' + url;
            }
            if (!title) {
                title = url.replace('https://', '').replace('http://', '').split('/')[0];
            }

            if (currentEditingIndex === -1) {
                shortcuts.push({ Title: title, Url: url, IconKey: 'globe' });
            } else {
                shortcuts[currentEditingIndex].Title = title;
                shortcuts[currentEditingIndex].Url = url;
            }

            saveShortcuts();
            renderShortcuts();
            shortcutModal.style.display = 'none';
        });

        function saveShortcuts() {
            postHostMessage({ type: 'saveStartpageShortcuts', shortcuts: shortcuts });
        }

        function navigateTo(url) {
            postHostMessage({ type: 'navigate', url: url });
        }

        function postHostMessage(msgObj) {
            if (window.chrome && window.chrome.webview) {
                msgObj.__echoToken = '##ECHO_BRIDGE_TOKEN##';
                window.chrome.webview.postMessage(msgObj);
            }
        }

        function escapeHtml(text) {
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }

        renderShortcuts();
        renderMostVisited();
    </script>
</body>
</html>";
    }
}
