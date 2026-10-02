using System;
using System.Text.Json;
using EchoBrowser.Models;

namespace EchoBrowser.Services
{
    public static class SettingsPageService
    {
        public const string SettingsPageUrl = "echo://settings";

        public static string GetSettingsPageHtml(AppSettings settings, string webViewVersion = "120.0", string appVersion = "1.2")
        {
            string settingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = false });

            // Erst übersetzen, dann Nutzerdaten (Einstellungen) einsetzen – so werden darin keine Platzhalter ersetzt
            string html = PageLocalizer.Apply(RawHtmlTemplate);
            html = html.Replace("##SETTINGS_JSON##", settingsJson);
            html = html.Replace("##WEBVIEW_VERSION##", webViewVersion);
            html = html.Replace("##APP_VERSION##", appVersion);
            html = InternalPageSecurity.InjectToken(html);

            return html;
        }

        private const string RawHtmlTemplate = @"<!DOCTYPE html>
<html lang=""{{lang}}"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{{t:Tab_Settings}} – Echo-Browser</title>
    <style>
        :root {
            --bg-color: #16171B;
            --sidebar-bg: #1C1D22;
            --surface-color: #22242B;
            --surface-hover: #2B2E37;
            --surface-active: #343742;
            --border-color: #2F323B;
            --border-subtle: #24262E;
            --border-focus: #C4C7CC;
            --text-primary: #F0F2F5;
            --text-secondary: #9DA3AF;
            --text-muted: #6B7280;
            --accent-silver: #C4C7CC;
            --accent-silver-bright: #FFFFFF;
            --accent-glow: rgba(196, 199, 204, 0.2);
            --accent-blue: #38BDF8;
            --accent-green: #34D399;
            --danger-color: #F87171;
            --danger-hover: #EF4444;
        }

        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            user-select: none;
        }

        body {
            background-color: var(--bg-color);
            color: var(--text-primary);
            height: 100vh;
            display: flex;
            flex-direction: column;
            overflow: hidden;
        }

        /* Top Header */
        header {
            height: 60px;
            background: var(--sidebar-bg);
            border-bottom: 1px solid var(--border-color);
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 0 32px;
            flex-shrink: 0;
            z-index: 10;
        }

        .header-brand {
            display: flex;
            align-items: center;
            gap: 12px;
        }

        .header-brand svg {
            width: 28px;
            height: 28px;
            filter: drop-shadow(0 2px 8px rgba(0,0,0,0.4));
        }

        .brand-title {
            font-size: 16px;
            font-weight: 700;
            letter-spacing: 1.5px;
            background: linear-gradient(135deg, #FFFFFF 0%, #C4C7CC 100%);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
        }

        .brand-badge {
            font-size: 11px;
            padding: 2px 8px;
            border-radius: 10px;
            background: rgba(196, 199, 204, 0.12);
            color: var(--accent-silver);
            border: 1px solid var(--border-color);
            font-weight: 600;
        }

        /* Search Bar */
        .search-container {
            width: 380px;
            position: relative;
        }

        .search-input {
            width: 100%;
            height: 36px;
            background: var(--bg-color);
            border: 1px solid var(--border-color);
            border-radius: 18px;
            padding: 0 16px 0 38px;
            color: var(--text-primary);
            font-size: 13px;
            outline: none;
            transition: all 0.2s ease;
        }

        .search-input:focus {
            border-color: var(--border-focus);
            box-shadow: 0 0 0 3px var(--accent-glow);
            background: var(--surface-color);
        }

        .search-icon {
            position: absolute;
            left: 12px;
            top: 50%;
            transform: translateY(-50%);
            width: 16px;
            height: 16px;
            fill: var(--text-muted);
            pointer-events: none;
        }

        /* Main Workspace: Sidebar + Content */
        .workspace {
            display: flex;
            flex: 1;
            height: calc(100vh - 60px);
            overflow: hidden;
        }

        /* Navigation Sidebar */
        aside {
            width: 250px;
            background: var(--sidebar-bg);
            border-right: 1px solid var(--border-color);
            padding: 20px 12px;
            display: flex;
            flex-direction: column;
            gap: 4px;
            overflow-y: auto;
            flex-shrink: 0;
        }

        .nav-item {
            display: flex;
            align-items: center;
            gap: 12px;
            padding: 10px 14px;
            border-radius: 8px;
            color: var(--text-secondary);
            font-size: 13.5px;
            font-weight: 500;
            cursor: pointer;
            transition: all 0.18s ease;
        }

        .nav-item:hover {
            background: var(--surface-color);
            color: var(--text-primary);
        }

        .nav-item.active {
            background: var(--surface-hover);
            color: var(--accent-silver-bright);
            font-weight: 600;
        }

        .nav-item svg {
            width: 18px;
            height: 18px;
            fill: currentColor;
            flex-shrink: 0;
        }

        /* Content Container */
        main {
            flex: 1;
            overflow-y: auto;
            padding: 32px 48px 80px 48px;
            scroll-behavior: smooth;
        }

        .content-wrapper {
            max-width: 820px;
            margin: 0 auto;
        }

        .section-header {
            margin-bottom: 24px;
            padding-bottom: 12px;
            border-bottom: 1px solid var(--border-color);
            display: flex;
            align-items: center;
            gap: 12px;
        }

        .section-title {
            font-size: 20px;
            font-weight: 700;
            color: var(--text-primary);
        }

        .settings-card {
            background: var(--surface-color);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            padding: 20px;
            margin-bottom: 18px;
            box-shadow: 0 4px 14px rgba(0,0,0,0.22);
            transition: border-color 0.2s ease, box-shadow 0.2s ease;
        }

        .settings-card:hover {
            border-color: #3B3F4A;
        }

        .card-row {
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 12px 0;
        }

        .card-row:not(:last-child) {
            border-bottom: 1px solid var(--border-subtle);
        }

        .row-info {
            display: flex;
            flex-direction: column;
            gap: 4px;
            padding-right: 20px;
        }

        .row-title {
            font-size: 14px;
            font-weight: 600;
            color: var(--text-primary);
        }

        .row-desc {
            font-size: 12.5px;
            color: var(--text-secondary);
            line-height: 1.4;
        }

        /* Modern Toggle Switch */
        .toggle-switch {
            position: relative;
            display: inline-block;
            width: 44px;
            height: 24px;
            flex-shrink: 0;
            cursor: pointer;
        }

        .toggle-switch input {
            opacity: 0;
            width: 0;
            height: 0;
        }

        .toggle-slider {
            position: absolute;
            top: 0; left: 0; right: 0; bottom: 0;
            background-color: var(--border-color);
            border-radius: 24px;
            transition: .25s ease;
        }

        .toggle-slider:before {
            position: absolute;
            content: """";
            height: 18px;
            width: 18px;
            left: 3px;
            bottom: 3px;
            background-color: var(--accent-silver);
            border-radius: 50%;
            transition: .25s ease;
            box-shadow: 0 2px 4px rgba(0,0,0,0.3);
        }

        input:checked + .toggle-slider {
            background-color: var(--accent-blue);
        }

        input:checked + .toggle-slider:before {
            transform: translateX(20px);
            background-color: #FFFFFF;
        }

        /* Radio Options Group */
        .radio-group {
            display: flex;
            flex-direction: column;
            gap: 10px;
            width: 100%;
            margin-top: 10px;
        }

        .radio-option {
            display: flex;
            align-items: center;
            gap: 14px;
            padding: 12px 14px;
            border-radius: 8px;
            background: var(--bg-color);
            border: 1px solid var(--border-color);
            cursor: pointer;
            transition: all 0.2s ease;
        }

        .radio-option:hover {
            border-color: var(--border-focus);
            background: var(--surface-hover);
        }

        .radio-option.selected {
            border-color: var(--accent-silver);
            background: var(--surface-active);
        }

        .custom-radio {
            width: 18px;
            height: 18px;
            border-radius: 50%;
            border: 2px solid var(--text-muted);
            display: flex;
            align-items: center;
            justify-content: center;
            flex-shrink: 0;
            transition: all 0.2s ease;
        }

        .radio-option.selected .custom-radio {
            border-color: var(--accent-blue);
        }

        .custom-radio:after {
            content: """";
            width: 8px;
            height: 8px;
            border-radius: 50%;
            background: var(--accent-blue);
            display: none;
        }

        .radio-option.selected .custom-radio:after {
            display: block;
        }

        /* Buttons & Inputs */
        .btn {
            padding: 8px 16px;
            border-radius: 6px;
            font-size: 13px;
            font-weight: 600;
            cursor: pointer;
            border: 1px solid var(--border-color);
            background: var(--surface-hover);
            color: var(--text-primary);
            transition: all 0.2s ease;
            display: inline-flex;
            align-items: center;
            gap: 8px;
        }

        .btn:hover {
            background: var(--surface-active);
            border-color: var(--border-focus);
            color: var(--accent-silver-bright);
        }

        .btn-primary {
            background: #2D3748;
            border-color: #4A5568;
            color: #FFFFFF;
        }

        .btn-primary:hover {
            background: #3B4758;
            border-color: var(--accent-silver);
        }

        .btn-danger {
            background: rgba(248, 113, 113, 0.15);
            border-color: rgba(248, 113, 113, 0.35);
            color: var(--danger-color);
        }

        .btn-danger:hover {
            background: var(--danger-color);
            border-color: var(--danger-hover);
            color: #FFFFFF;
        }

        .input-text {
            height: 34px;
            background: var(--bg-color);
            border: 1px solid var(--border-color);
            border-radius: 6px;
            padding: 0 12px;
            color: var(--text-primary);
            font-size: 13px;
            outline: none;
            transition: all 0.2s ease;
        }

        .input-text:focus {
            border-color: var(--border-focus);
            box-shadow: 0 0 0 2px var(--accent-glow);
        }

        .select-input {
            height: 34px;
            background: var(--bg-color);
            border: 1px solid var(--border-color);
            border-radius: 6px;
            padding: 0 12px;
            color: var(--text-primary);
            font-size: 13px;
            outline: none;
            cursor: pointer;
        }

        /* Search Engine Grid */
        .engine-grid {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
            gap: 12px;
            margin-top: 12px;
        }

        .engine-card {
            background: var(--bg-color);
            border: 1px solid var(--border-color);
            border-radius: 10px;
            padding: 14px;
            cursor: pointer;
            transition: all 0.2s ease;
            display: flex;
            flex-direction: column;
            gap: 8px;
        }

        .engine-card:hover {
            border-color: #4A505E;
            background: var(--surface-hover);
        }

        .engine-card.active {
            border-color: var(--accent-blue);
            background: var(--surface-active);
        }

        .engine-card-header {
            display: flex;
            align-items: center;
            justify-content: space-between;
        }

        .engine-name {
            font-weight: 700;
            font-size: 14px;
            color: var(--text-primary);
        }

        .engine-desc {
            font-size: 12px;
            color: var(--text-secondary);
            line-height: 1.35;
        }

        .badge-active {
            font-size: 10px;
            background: rgba(56, 189, 248, 0.2);
            color: var(--accent-blue);
            padding: 2px 6px;
            border-radius: 8px;
            font-weight: 700;
        }

        /* Theme Presets Grid */
        .theme-grid {
            display: grid;
            grid-template-columns: repeat(2, 1fr);
            gap: 14px;
            margin-top: 14px;
        }

        .theme-card {
            background: var(--bg-color);
            border: 1px solid var(--border-color);
            border-radius: 10px;
            padding: 14px;
            cursor: pointer;
            transition: all 0.2s ease;
            display: flex;
            align-items: center;
            gap: 14px;
        }

        .theme-card:hover {
            border-color: var(--border-focus);
            background: var(--surface-hover);
        }

        .theme-card.active {
            border-color: var(--accent-silver);
            background: var(--surface-active);
            box-shadow: 0 0 0 1px var(--accent-silver);
        }

        .theme-preview-dot {
            width: 24px;
            height: 24px;
            border-radius: 50%;
            border: 2px solid rgba(255,255,255,0.2);
            flex-shrink: 0;
        }

        /* Accent Colors Swatches */
        .accent-palette {
            display: flex;
            gap: 10px;
            align-items: center;
            flex-wrap: wrap;
            margin-top: 8px;
        }

        .color-swatch {
            width: 32px;
            height: 32px;
            border-radius: 50%;
            cursor: pointer;
            border: 2px solid transparent;
            transition: transform 0.2s ease, border-color 0.2s ease;
            box-shadow: 0 2px 6px rgba(0,0,0,0.3);
        }

        .color-swatch:hover {
            transform: scale(1.15);
        }

        .color-swatch.active {
            border-color: #FFFFFF;
            transform: scale(1.15);
            box-shadow: 0 0 0 3px rgba(255,255,255,0.3);
        }

        /* Shield Protection Cards */
        .shield-level-grid {
            display: grid;
            grid-template-columns: repeat(3, 1fr);
            gap: 12px;
            margin-top: 12px;
        }

        .shield-card {
            background: var(--bg-color);
            border: 1px solid var(--border-color);
            border-radius: 10px;
            padding: 16px;
            cursor: pointer;
            transition: all 0.2s ease;
            display: flex;
            flex-direction: column;
            gap: 8px;
        }

        .shield-card:hover {
            border-color: #4A505E;
            background: var(--surface-hover);
        }

        .shield-card.active {
            border-color: var(--accent-green);
            background: var(--surface-active);
        }

        .shield-card-title {
            font-size: 13.5px;
            font-weight: 700;
            color: var(--text-primary);
            display: flex;
            align-items: center;
            justify-content: space-between;
        }

        .shield-card-desc {
            font-size: 11.5px;
            color: var(--text-secondary);
            line-height: 1.4;
        }

        /* Modal Overlay */
        .modal-overlay {
            position: fixed;
            top: 0; left: 0; right: 0; bottom: 0;
            background: rgba(0,0,0,0.65);
            backdrop-filter: blur(4px);
            display: none;
            align-items: center;
            justify-content: center;
            z-index: 100;
        }

        .modal-overlay.open {
            display: flex;
        }

        .modal-card {
            background: var(--surface-color);
            border: 1px solid var(--border-color);
            border-radius: 14px;
            padding: 24px;
            width: 440px;
            box-shadow: 0 16px 36px rgba(0,0,0,0.6);
        }

        .modal-title {
            font-size: 17px;
            font-weight: 700;
            margin-bottom: 8px;
        }

        .modal-desc {
            font-size: 13px;
            color: var(--text-secondary);
            margin-bottom: 18px;
            line-height: 1.4;
        }

        .modal-checkboxes {
            display: flex;
            flex-direction: column;
            gap: 12px;
            margin-bottom: 24px;
        }

        .modal-cb-row {
            display: flex;
            align-items: center;
            gap: 12px;
            font-size: 13px;
            cursor: pointer;
        }

        .modal-cb-row input {
            width: 16px;
            height: 16px;
            accent-color: var(--accent-blue);
            cursor: pointer;
        }

        .modal-actions {
            display: flex;
            justify-content: flex-end;
            gap: 10px;
        }

        /* Toast Alert */
        .toast {
            position: fixed;
            bottom: 24px;
            right: 24px;
            background: var(--surface-active);
            border: 1px solid var(--accent-silver);
            color: var(--text-primary);
            padding: 12px 20px;
            border-radius: 8px;
            font-size: 13px;
            font-weight: 600;
            box-shadow: 0 8px 24px rgba(0,0,0,0.4);
            transform: translateY(100px);
            opacity: 0;
            transition: all 0.3s cubic-bezier(0.16, 1, 0.3, 1);
            z-index: 200;
            display: flex;
            align-items: center;
            gap: 10px;
        }

        .toast.show {
            transform: translateY(0);
            opacity: 1;
        }

        /* About Section Spec */
        .about-card {
            display: flex;
            align-items: center;
            gap: 24px;
            padding: 24px;
            background: var(--surface-color);
            border: 1px solid var(--border-color);
            border-radius: 12px;
        }

        .about-logo svg {
            width: 72px;
            height: 72px;
            filter: drop-shadow(0 4px 12px rgba(0,0,0,0.5));
        }

        .about-details {
            display: flex;
            flex-direction: column;
            gap: 6px;
        }

        .about-title {
            font-size: 22px;
            font-weight: 800;
            letter-spacing: 1px;
            background: linear-gradient(135deg, #FFFFFF 0%, #C4C7CC 100%);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
        }

        .about-meta {
            font-size: 13px;
            color: var(--text-secondary);
        }

        .about-status {
            display: inline-flex;
            align-items: center;
            gap: 6px;
            background: rgba(52, 211, 153, 0.15);
            color: var(--accent-green);
            padding: 3px 10px;
            border-radius: 12px;
            font-size: 12px;
            font-weight: 700;
            width: fit-content;
            margin-top: 4px;
        }

        .about-status.checking {
            background: rgba(56, 189, 248, 0.15);
            color: var(--accent-blue);
        }

        .about-status.warning {
            background: rgba(251, 191, 36, 0.15);
            color: #FBBF24;
        }

        .about-status.ready {
            background: rgba(52, 211, 153, 0.25);
            color: #34D399;
        }

        .update-progress-bar {
            height: 5px;
            background: var(--border-color);
            border-radius: 3px;
            overflow: hidden;
            margin-top: 8px;
            width: 240px;
        }

        .update-progress-fill {
            height: 100%;
            background: var(--accent-blue);
            width: 0%;
            transition: width 0.2s ease;
        }
    </style>
</head>
<body>

    <!-- Header -->
    <header>
        <div class=""header-brand"">
            <!-- Echo Waves Icon -->
            <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""#C4C7CC"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                <path d=""M2 12a10 10 0 0 1 20 0""/>
                <path d=""M5 12a7 7 0 0 1 14 0""/>
                <path d=""M8 12a4 4 0 0 1 8 0""/>
                <circle cx=""12"" cy=""12"" r=""1"" fill=""#C4C7CC""/>
            </svg>
            <div class=""brand-title"">ECHO BROWSER</div>
            <div class=""brand-badge"">{{t:Settings_Badge}}</div>
        </div>

        <div class=""search-container"">
            <svg class=""search-icon"" viewBox=""0 0 24 24"">
                <path d=""M15.5 14h-.79l-.28-.27A6.471 6.471 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z""/>
            </svg>
            <input type=""text"" id=""searchSettings"" class=""search-input"" placeholder=""{{t:Settings_SearchPlaceholder}}"" autocomplete=""off"">
        </div>
    </header>

    <!-- Workspace -->
    <div class=""workspace"">
        <!-- Sidebar Navigation -->
        <aside>
            <div class=""nav-item active"" data-target=""section-general"">
                <svg viewBox=""0 0 24 24""><path d=""M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z""/></svg>
                <span>{{t:Settings_NavGeneral}}</span>
            </div>
            <div class=""nav-item"" data-target=""section-search"">
                <svg viewBox=""0 0 24 24""><path d=""M15.5 14h-.79l-.28-.27A6.471 6.471 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z""/></svg>
                <span>{{t:Settings_NavSearch}}</span>
            </div>
            <div class=""nav-item"" data-target=""section-appearance"">
                <svg viewBox=""0 0 24 24""><path d=""M12 3c-4.97 0-9 4.03-9 9 0 2.12.74 4.07 1.97 5.61L4.35 18.5c-.39.39-.39 1.02 0 1.41.39.39 1.02.39 1.41 0l.9-.9C8.19 19.9 10.03 20.5 12 20.5c4.97 0 9-4.03 9-9s-4.03-9-9-9zm0 15.5c-3.58 0-6.5-2.92-6.5-6.5S8.42 5.5 12 5.5s6.5 2.92 6.5 6.5-2.92 6.5-6.5 6.5z""/><circle cx=""8.5"" cy=""9.5"" r=""1.5""/><circle cx=""15.5"" cy=""9.5"" r=""1.5""/><circle cx=""12"" cy=""14"" r=""1.5""/></svg>
                <span>{{t:Settings_NavAppearance}}</span>
            </div>
            <div class=""nav-item"" data-target=""section-privacy"">
                <svg viewBox=""0 0 24 24""><path d=""M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm-2 16l-4-4 1.41-1.41L10 14.17l6.59-6.59L18 9l-8 8z""/></svg>
                <span>{{t:Settings_NavPrivacy}}</span>
            </div>
            <div class=""nav-item"" data-target=""section-downloads"">
                <svg viewBox=""0 0 24 24""><path d=""M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z""/></svg>
                <span>{{t:Settings_NavDownloads}}</span>
            </div>
            <div class=""nav-item"" data-target=""section-tabs"">
                <svg viewBox=""0 0 24 24""><path d=""M3 3h18a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zm0 4v12h18V7H3zm2 2h4v2H5V9zm6 0h4v2h-4V9z""/></svg>
                <span>{{t:Settings_NavTabs}}</span>
            </div>
            <div class=""nav-item"" data-target=""section-about"">
                <svg viewBox=""0 0 24 24""><path d=""M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z""/></svg>
                <span>{{t:Settings_NavAbout}}</span>
            </div>
        </aside>

        <!-- Main Content -->
        <main id=""mainContent"">
            <div class=""content-wrapper"">

                <!-- 1. ALLGEMEIN -->
                <section id=""section-general"">
                    <div class=""section-header"">
                        <h2 class=""section-title"">{{t:Settings_GeneralTitle}}</h2>
                    </div>

                    <div class=""settings-card"">
                        <div class=""row-title"">{{t:Settings_OnStartup}}</div>
                        <div class=""row-desc"">{{t:Settings_OnStartupDesc}}</div>

                        <div class=""radio-group"" id=""startupGroup"">
                            <div class=""radio-option"" data-val=""startpage"">
                                <div class=""custom-radio""></div>
                                <div>
                                    <div class=""row-title"">{{t:Settings_StartupStartpage}}</div>
                                    <div class=""row-desc"">{{t:Settings_StartupStartpageDesc}}</div>
                                </div>
                            </div>
                            <div class=""radio-option"" data-val=""restore_session"">
                                <div class=""custom-radio""></div>
                                <div>
                                    <div class=""row-title"">{{t:Settings_StartupRestore}}</div>
                                    <div class=""row-desc"">{{t:Settings_StartupRestoreDesc}}</div>
                                </div>
                            </div>
                            <div class=""radio-option"" data-val=""custom_url"">
                                <div class=""custom-radio""></div>
                                <div style=""flex: 1;"">
                                    <div class=""row-title"">{{t:Settings_StartupCustom}}</div>
                                    <div class=""row-desc"">{{t:Settings_StartupCustomDesc}}</div>
                                    <input type=""text"" id=""txtCustomUrl"" class=""input-text"" style=""width: 100%; margin-top: 8px; display: none;"" placeholder=""https://example.com"">
                                </div>
                            </div>
                        </div>
                    </div>

                    <div class=""settings-card"">
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_ShowHomeButton}}</div>
                                <div class=""row-desc"">{{t:Settings_ShowHomeButtonDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkShowHomeButton"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_StartpageTiles}}</div>
                                <div class=""row-desc"">{{t:Settings_StartpageTilesDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkStartpageFavorites"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                    </div>

                    <div class=""settings-card"">
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_Language}}</div>
                                <div class=""row-desc"">{{t:Settings_LanguageDesc}}</div>
                            </div>
                            <select id=""selLanguage"" class=""select-dropdown"" style=""background: #1e2025; color: #e2e8f0; border: 1px solid #333842; padding: 6px 12px; border-radius: 6px; font-size: 13px; outline: none; cursor: pointer;"">
                                <option value=""de"">🇩🇪 Deutsch</option>
                                <option value=""en"">🇬🇧 English</option>
                                <option value=""fr"">🇫🇷 Français</option>
                                <option value=""es"">🇪🇸 Español</option>
                            </select>
                        </div>
                    </div>
                </section>

                <!-- 2. SUCHMASCHINE -->
                <section id=""section-search"">
                    <div class=""section-header"">
                        <h2 class=""section-title"">{{t:Settings_NavSearch}}</h2>
                    </div>

                    <div class=""settings-card"">
                        <div class=""row-title"">{{t:Settings_DefaultSearch}}</div>
                        <div class=""row-desc"">{{t:Settings_DefaultSearchDesc}}</div>

                        <div class=""engine-grid"" id=""engineGrid"">
                            <div class=""engine-card"" data-engine=""duckduckgo"">
                                <div class=""engine-card-header"">
                                    <div class=""engine-name"">DuckDuckGo</div>
                                    <span class=""badge-active"">{{t:Settings_Active}}</span>
                                </div>
                                <div class=""engine-desc"">{{t:Settings_EngineDuckDuckGo}}</div>
                            </div>
                            <div class=""engine-card"" data-engine=""google"">
                                <div class=""engine-card-header"">
                                    <div class=""engine-name"">Google</div>
                                    <span class=""badge-active"">{{t:Settings_Active}}</span>
                                </div>
                                <div class=""engine-desc"">{{t:Settings_EngineGoogle}}</div>
                            </div>
                            <div class=""engine-card"" data-engine=""bing"">
                                <div class=""engine-card-header"">
                                    <div class=""engine-name"">Bing</div>
                                    <span class=""badge-active"">{{t:Settings_Active}}</span>
                                </div>
                                <div class=""engine-desc"">{{t:Settings_EngineBing}}</div>
                            </div>
                            <div class=""engine-card"" data-engine=""ecosia"">
                                <div class=""engine-card-header"">
                                    <div class=""engine-name"">Ecosia</div>
                                    <span class=""badge-active"">{{t:Settings_Active}}</span>
                                </div>
                                <div class=""engine-desc"">{{t:Settings_EngineEcosia}}</div>
                            </div>
                            <div class=""engine-card"" data-engine=""brave"">
                                <div class=""engine-card-header"">
                                    <div class=""engine-name"">Brave Search</div>
                                    <span class=""badge-active"">{{t:Settings_Active}}</span>
                                </div>
                                <div class=""engine-desc"">{{t:Settings_EngineBrave}}</div>
                            </div>
                            <div class=""engine-card"" data-engine=""startpage"">
                                <div class=""engine-card-header"">
                                    <div class=""engine-name"">Startpage</div>
                                    <span class=""badge-active"">{{t:Settings_Active}}</span>
                                </div>
                                <div class=""engine-desc"">{{t:Settings_EngineStartpage}}</div>
                            </div>
                        </div>
                    </div>

                    <div class=""settings-card"">
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_SearchSuggestions}}</div>
                                <div class=""row-desc"">{{t:Settings_SearchSuggestionsDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkSearchSuggestions"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                    </div>
                </section>

                <!-- 3. ERSCHEINUNGSBILD -->
                <section id=""section-appearance"">
                    <div class=""section-header"">
                        <h2 class=""section-title"">{{t:Settings_AppearanceTitle}}</h2>
                    </div>

                    <div class=""settings-card"">
                        <div class=""row-title"">{{t:Settings_ThemePreset}}</div>
                        <div class=""row-desc"">{{t:Settings_ThemePresetDesc}}</div>

                        <div class=""theme-grid"" id=""themeGrid"">
                            <div class=""theme-card"" data-preset=""SilverAnthracite"">
                                <div class=""theme-preview-dot"" style=""background: #1C1D21; border-color: #C4C7CC;""></div>
                                <div>
                                    <div class=""row-title"">{{t:Settings_ThemeSilver}}</div>
                                    <div class=""row-desc"">{{t:Settings_ThemeSilverDesc}}</div>
                                </div>
                            </div>
                            <div class=""theme-card"" data-preset=""MidnightOled"">
                                <div class=""theme-preview-dot"" style=""background: #07080A; border-color: #FFFFFF;""></div>
                                <div>
                                    <div class=""row-title"">Midnight OLED</div>
                                    <div class=""row-desc"">{{t:Settings_ThemeMidnightDesc}}</div>
                                </div>
                            </div>
                            <div class=""theme-card"" data-preset=""TitaniumLight"">
                                <div class=""theme-preview-dot"" style=""background: #F4F6F9; border-color: #8A8F99;""></div>
                                <div>
                                    <div class=""row-title"">Titanium Light</div>
                                    <div class=""row-desc"">{{t:Settings_ThemeTitaniumDesc}}</div>
                                </div>
                            </div>
                            <div class=""theme-card"" data-preset=""CobaltSlate"">
                                <div class=""theme-preview-dot"" style=""background: #111827; border-color: #60A5FA;""></div>
                                <div>
                                    <div class=""row-title"">Cobalt Slate</div>
                                    <div class=""row-desc"">{{t:Settings_ThemeCobaltDesc}}</div>
                                </div>
                            </div>
                        </div>
                    </div>

                    <div class=""settings-card"">
                        <div class=""row-title"">{{t:Settings_AccentColor}}</div>
                        <div class=""row-desc"">{{t:Settings_AccentColorDesc}}</div>

                        <div class=""accent-palette"" id=""accentPalette"">
                            <div class=""color-swatch"" style=""background: #C4C7CC;"" data-color=""#C4C7CC"" title=""{{t:Color_Silver}}""></div>
                            <div class=""color-swatch"" style=""background: #38BDF8;"" data-color=""#38BDF8"" title=""{{t:Color_IceBlue}}""></div>
                            <div class=""color-swatch"" style=""background: #34D399;"" data-color=""#34D399"" title=""{{t:Color_Emerald}}""></div>
                            <div class=""color-swatch"" style=""background: #F59E0B;"" data-color=""#F59E0B"" title=""{{t:Color_Amber}}""></div>
                            <div class=""color-swatch"" style=""background: #F43F5E;"" data-color=""#F43F5E"" title=""{{t:Color_Ruby}}""></div>
                            <div class=""color-swatch"" style=""background: #A855F7;"" data-color=""#A855F7"" title=""{{t:Color_Amethyst}}""></div>
                            <div style=""display: flex; align-items: center; gap: 8px; margin-left: 12px;"">
                                <input type=""text"" id=""txtCustomAccent"" class=""input-text"" style=""width: 90px; text-transform: uppercase;"" maxlength=""7"" placeholder=""#HEX"">
                                <button id=""btnApplyAccent"" class=""btn"">{{t:Settings_Apply}}</button>
                            </div>
                        </div>
                    </div>

                    <div class=""settings-card"">
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_ShowBookmarksBar}}</div>
                                <div class=""row-desc"">{{t:Settings_ShowBookmarksBarDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkBookmarksBar"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_ShowSidebar}}</div>
                                <div class=""row-desc"">{{t:Settings_ShowSidebarDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkSidebar"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_DefaultZoom}}</div>
                                <div class=""row-desc"">{{t:Settings_DefaultZoomDesc}}</div>
                            </div>
                            <select id=""selZoom"" class=""select-input"">
                                <option value=""75"">75%</option>
                                <option value=""90"">90%</option>
                                <option value=""100"" selected>{{t:Settings_Zoom100}}</option>
                                <option value=""110"">110%</option>
                                <option value=""125"">125%</option>
                                <option value=""150"">150%</option>
                            </select>
                        </div>
                    </div>

                    <div class=""settings-card"">
                        <div class=""row-title"">{{t:Settings_ToolbarTitle}}</div>
                        <div class=""row-desc"">{{t:Settings_ToolbarDesc}}</div>

                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_BtnSidebar}}</div>
                                <div class=""row-desc"">{{t:Settings_BtnSidebarDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkShowSidebarButton"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_BtnBack}}</div>
                                <div class=""row-desc"">{{t:Settings_BtnBackDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkShowBackButton"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_BtnForward}}</div>
                                <div class=""row-desc"">{{t:Settings_BtnForwardDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkShowForwardButton"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_BtnReload}}</div>
                                <div class=""row-desc"">{{t:Settings_BtnReloadDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkShowReloadButton"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_BtnSearchEngine}}</div>
                                <div class=""row-desc"">{{t:Settings_BtnSearchEngineDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkShowSearchEngineSelector"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_BtnExtensions}}</div>
                                <div class=""row-desc"">{{t:Settings_BtnExtensionsDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkShowExtensionsButton"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_BtnDownloads}}</div>
                                <div class=""row-desc"">{{t:Settings_BtnDownloadsDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkShowDownloadsButton"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                    </div>
                </section>

                <!-- 4. DATENSCHUTZ & SICHERHEIT -->
                <section id=""section-privacy"">
                    <div class=""section-header"">
                        <h2 class=""section-title"">{{t:Settings_NavPrivacy}}</h2>
                    </div>

                    <div class=""settings-card"">
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_AdBlocker}}</div>
                                <div class=""row-desc"">{{t:Settings_AdBlockerDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkIsAdBlockerEnabled"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_UpdateFilterRules}}</div>
                                <div class=""row-desc"" id=""lblFilterRuleDesc"">{{t:Settings_UpdateFilterRulesDesc}}</div>
                            </div>
                            <button id=""btnUpdateAdBlockFilter"" class=""btn btn-primary"">{{t:Shield_UpdateFilters}}</button>
                        </div>
                    </div>

                    <div class=""settings-card"">
                        <div class=""row-title"">{{t:Settings_TrackingProtection}}</div>
                        <div class=""row-desc"">{{t:Settings_TrackingProtectionDesc}}</div>

                        <div class=""shield-level-grid"" id=""shieldGrid"">
                            <div class=""shield-card"" data-level=""balanced"">
                                <div class=""shield-card-title"">
                                    <span>{{t:Settings_LevelBalanced}}</span>
                                    <span class=""badge-active"">{{t:Settings_Recommended}}</span>
                                </div>
                                <div class=""shield-card-desc"">{{t:Settings_LevelBalancedDesc}}</div>
                            </div>
                            <div class=""shield-card"" data-level=""strict"">
                                <div class=""shield-card-title"">
                                    <span>{{t:Settings_LevelStrict}}</span>
                                </div>
                                <div class=""shield-card-desc"">{{t:Settings_LevelStrictDesc}}</div>
                            </div>
                            <div class=""shield-card"" data-level=""none"">
                                <div class=""shield-card-title"">
                                    <span>{{t:Settings_LevelNone}}</span>
                                </div>
                                <div class=""shield-card-desc"">{{t:Settings_LevelNoneDesc}}</div>
                            </div>
                        </div>
                    </div>

                    <div class=""settings-card"">
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_PopupBlocker}}</div>
                                <div class=""row-desc"">{{t:Settings_PopupBlockerDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkBlockPopups"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Shield_JavaScript}}</div>
                                <div class=""row-desc"">{{t:Settings_JavaScriptDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkEnableJavaScript"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_DoNotTrack}}</div>
                                <div class=""row-desc"">{{t:Settings_DoNotTrackDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkSendDoNotTrack"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                    </div>

                    <div class=""settings-card"">
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_ClearData}}</div>
                                <div class=""row-desc"">{{t:Settings_ClearDataDesc}}</div>
                            </div>
                            <button id=""btnOpenClearData"" class=""btn btn-danger"">{{t:Settings_ClearDataButton}}</button>
                        </div>
                    </div>
                </section>

                <!-- 5. DOWNLOADS -->
                <section id=""section-downloads"">
                    <div class=""section-header"">
                        <h2 class=""section-title"">{{t:Settings_NavDownloads}}</h2>
                    </div>

                    <div class=""settings-card"">
                        <div class=""row-title"">{{t:Settings_DownloadLocation}}</div>
                        <div class=""row-desc"" style=""margin-bottom: 12px;"">{{t:Settings_DownloadLocationDesc}}</div>

                        <div style=""display: flex; gap: 10px; align-items: center;"">
                            <input type=""text"" id=""txtDownloadPath"" class=""input-text"" style=""flex: 1;"" readonly>
                            <button id=""btnChangeDownloadPath"" class=""btn btn-primary"">{{t:Settings_Change}}</button>
                            <button id=""btnOpenDownloadFolder"" class=""btn"">{{t:Downloads_OpenFolder}}</button>
                        </div>
                    </div>

                    <div class=""settings-card"">
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_AskDownloadLocation}}</div>
                                <div class=""row-desc"">{{t:Settings_AskDownloadLocationDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkAskDownloadLocation"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                    </div>
                </section>

                <!-- 6. TABS & VERHALTEN -->
                <section id=""section-tabs"">
                    <div class=""section-header"">
                        <h2 class=""section-title"">{{t:Settings_NavTabs}}</h2>
                    </div>

                    <div class=""settings-card"">
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_OpenTabsInBackground}}</div>
                                <div class=""row-desc"">{{t:Settings_OpenTabsInBackgroundDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkOpenTabsInBackground"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_WarnCloseTabs}}</div>
                                <div class=""row-desc"">{{t:Settings_WarnCloseTabsDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkWarnCloseTabs"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                    </div>

                    <div class=""settings-card"">
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_Reset}}</div>
                                <div class=""row-desc"">{{t:Settings_ResetDesc}}</div>
                            </div>
                            <button id=""btnResetSettings"" class=""btn"">{{t:Settings_ResetButton}}</button>
                        </div>
                    </div>
                </section>

                <!-- 7. ÜBER ECHO -->
                <section id=""section-about"">
                    <div class=""section-header"">
                        <h2 class=""section-title"">{{t:Settings_AboutTitle}}</h2>
                    </div>

                    <div class=""about-card"">
                        <div class=""about-logo"">
                            <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""#C4C7CC"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                                <path d=""M2 12a10 10 0 0 1 20 0""/>
                                <path d=""M5 12a7 7 0 0 1 14 0""/>
                                <path d=""M8 12a4 4 0 0 1 8 0""/>
                                <circle cx=""12"" cy=""12"" r=""1.5"" fill=""#C4C7CC""/>
                            </svg>
                        </div>
                        <div class=""about-details"">
                            <div class=""about-title"">Echo-Browser</div>
                            <div class=""about-meta"">Version ##APP_VERSION## (Silver/Anthracite Edition) • 64-Bit</div>
                            <div class=""about-meta"">Chromium-Engine / WebView2: ##WEBVIEW_VERSION##</div>
                            <div class=""about-meta"">{{t:Settings_AboutPlatform}}</div>
                            <div id=""aboutStatusBox"" class=""about-status"">
                                <svg id=""svgStatusIcon"" width=""12"" height=""12"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""3""><path d=""M20 6L9 17l-5-5""/></svg>
                                <span id=""lblAboutStatus"">{{t:Settings_AboutUpToDate}}</span>
                            </div>
                            <div id=""updateProgressContainer"" class=""update-progress-bar"" style=""display:none;"">
                                <div id=""updateProgressBar"" class=""update-progress-fill""></div>
                            </div>
                            <div style=""margin-top: 10px; display: flex; gap: 10px; align-items: center;"">
                                <button id=""btnCheckUpdates"" class=""btn btn-primary"">
                                    <svg width=""14"" height=""14"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M23 4v6h-6M1 20v-6h6M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15""/></svg>
                                    <span>{{t:Settings_CheckForUpdates}}</span>
                                </button>
                            </div>
                        </div>
                    </div>

                    <div class=""settings-card"" style=""margin-top: 14px;"">
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_AutoCheckUpdates}}</div>
                                <div class=""row-desc"">{{t:Settings_AutoCheckUpdatesDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkAutoCheckUpdates"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                        <div class=""card-row"">
                            <div class=""row-info"">
                                <div class=""row-title"">{{t:Settings_CheckPrereleaseUpdates}}</div>
                                <div class=""row-desc"">{{t:Settings_CheckPrereleaseUpdatesDesc}}</div>
                            </div>
                            <label class=""toggle-switch"">
                                <input type=""checkbox"" id=""chkCheckPrereleases"">
                                <span class=""toggle-slider""></span>
                            </label>
                        </div>
                    </div>
                </section>

            </div>
        </main>
    </div>

    <!-- Clear Browsing Data Modal -->
    <div id=""modalClearData"" class=""modal-overlay"">
        <div class=""modal-card"">
            <div class=""modal-title"">{{t:Settings_ClearData}}</div>
            <div class=""modal-desc"">{{t:Settings_ClearDataModalDesc}}</div>

            <div class=""modal-checkboxes"">
                <label class=""modal-cb-row"">
                    <input type=""checkbox"" id=""cbClearHistory"" checked>
                    <span>{{t:Settings_ClearHistoryOption}}</span>
                </label>
                <label class=""modal-cb-row"">
                    <input type=""checkbox"" id=""cbClearCookies"" checked>
                    <span>{{t:Settings_ClearCookiesOption}}</span>
                </label>
                <label class=""modal-cb-row"">
                    <input type=""checkbox"" id=""cbClearCache"" checked>
                    <span>{{t:Settings_ClearCacheOption}}</span>
                </label>
            </div>

            <div class=""modal-actions"">
                <button id=""btnCancelClearData"" class=""btn"">{{t:Dialog_Cancel}}</button>
                <button id=""btnConfirmClearData"" class=""btn btn-danger"">{{t:Settings_ClearNow}}</button>
            </div>
        </div>
    </div>

    <!-- Toast Notification -->
    <div id=""toast"" class=""toast"">
        <svg width=""16"" height=""16"" viewBox=""0 0 24 24"" fill=""none"" stroke=""#34D399"" stroke-width=""3""><path d=""M20 6L9 17l-5-5""/></svg>
        <span id=""toastMsg"">{{t:Settings_Saved}}</span>
    </div>

    <!-- Interactive Logic Script -->
    <script>
        const initialSettings = ##SETTINGS_JSON##;

        function sendMessage(msg) {
            if (window.chrome && window.chrome.webview) {
                msg.__echoToken = '##ECHO_BRIDGE_TOKEN##';
                window.chrome.webview.postMessage(msg);
            }
        }

        function fmt(s, ...args) { return s.replace(/\{(\d+)\}/g, (m, i) => args[i] ?? m); }

        function showToast(text) {
            const toast = document.getElementById('toast');
            document.getElementById('toastMsg').innerText = text;
            toast.classList.add('show');
            setTimeout(() => toast.classList.remove('show'), 2400);
        }

        // Initialize UI with current settings
        function applySettingsToUI(s) {
            // 1. Startup behavior
            document.querySelectorAll('#startupGroup .radio-option').forEach(el => {
                const val = el.getAttribute('data-val');
                if (val === s.StartupBehavior) {
                    el.classList.add('selected');
                } else {
                    el.classList.remove('selected');
                }
            });
            const txtCustomUrl = document.getElementById('txtCustomUrl');
            txtCustomUrl.value = s.CustomStartupUrl || '';
            txtCustomUrl.style.display = s.StartupBehavior === 'custom_url' ? 'block' : 'none';

            // General toggles
            document.getElementById('chkShowHomeButton').checked = s.ShowHomeButton !== false;
            document.getElementById('chkStartpageFavorites').checked = s.IsStartpageFavoritesVisible !== false;
            const selLang = document.getElementById('selLanguage');
            if (selLang) selLang.value = s.Language || 'de';

            // 2. Search
            document.querySelectorAll('#engineGrid .engine-card').forEach(el => {
                const eng = el.getAttribute('data-engine');
                if (eng === (s.SearchEngine || 'duckduckgo').toLowerCase()) {
                    el.classList.add('active');
                } else {
                    el.classList.remove('active');
                }
            });
            document.getElementById('chkSearchSuggestions').checked = s.EnableSearchSuggestions !== false;

            // 3. Appearance
            document.querySelectorAll('#themeGrid .theme-card').forEach(el => {
                const preset = el.getAttribute('data-preset');
                if (preset === (s.ThemePreset || 'SilverAnthracite')) {
                    el.classList.add('active');
                } else {
                    el.classList.remove('active');
                }
            });

            document.querySelectorAll('#accentPalette .color-swatch').forEach(el => {
                const col = el.getAttribute('data-color');
                if (col.toLowerCase() === (s.AccentColor || '#c4c7cc').toLowerCase()) {
                    el.classList.add('active');
                } else {
                    el.classList.remove('active');
                }
            });
            document.getElementById('txtCustomAccent').value = s.AccentColor || '#C4C7CC';

            document.getElementById('chkBookmarksBar').checked = s.IsBookmarksBarVisible !== false;
            document.getElementById('chkSidebar').checked = s.IsSidebarVisible !== false;
            document.getElementById('selZoom').value = (s.DefaultZoomPercent || 100).toString();

            // Toolbar buttons
            if (document.getElementById('chkShowSidebarButton')) document.getElementById('chkShowSidebarButton').checked = s.ShowSidebarButton !== false;
            if (document.getElementById('chkShowBackButton')) document.getElementById('chkShowBackButton').checked = s.ShowBackButton !== false;
            if (document.getElementById('chkShowForwardButton')) document.getElementById('chkShowForwardButton').checked = s.ShowForwardButton !== false;
            if (document.getElementById('chkShowReloadButton')) document.getElementById('chkShowReloadButton').checked = s.ShowReloadButton !== false;
            if (document.getElementById('chkShowSearchEngineSelector')) document.getElementById('chkShowSearchEngineSelector').checked = s.ShowSearchEngineSelector !== false;
            if (document.getElementById('chkShowExtensionsButton')) document.getElementById('chkShowExtensionsButton').checked = s.ShowExtensionsButton !== false;
            if (document.getElementById('chkShowDownloadsButton')) document.getElementById('chkShowDownloadsButton').checked = s.ShowDownloadsButton !== false;

            // 4. Privacy & Shield
            if (document.getElementById('chkIsAdBlockerEnabled')) document.getElementById('chkIsAdBlockerEnabled').checked = s.IsAdBlockerEnabled !== false;

            document.querySelectorAll('#shieldGrid .shield-card').forEach(el => {
                const lvl = el.getAttribute('data-level');
                if (lvl === (s.TrackingPreventionLevel || 'balanced').toLowerCase()) {
                    el.classList.add('active');
                } else {
                    el.classList.remove('active');
                }
            });

            document.getElementById('chkBlockPopups').checked = s.BlockPopups !== false;
            document.getElementById('chkEnableJavaScript').checked = s.EnableJavaScript !== false;
            document.getElementById('chkSendDoNotTrack').checked = s.SendDoNotTrack !== false;

            // 5. Downloads
            document.getElementById('txtDownloadPath').value = s.DownloadPath || '';
            document.getElementById('chkAskDownloadLocation').checked = s.AskDownloadLocation === true;

            // 6. Tabs
            document.getElementById('chkOpenTabsInBackground').checked = s.OpenNewTabInBackground === true;
            document.getElementById('chkWarnCloseTabs').checked = s.WarnOnClosingMultipleTabs !== false;

            // 7. Updates
            if (document.getElementById('chkAutoCheckUpdates')) document.getElementById('chkAutoCheckUpdates').checked = s.AutoCheckForUpdates !== false;
            if (document.getElementById('chkCheckPrereleases')) document.getElementById('chkCheckPrereleases').checked = s.CheckPrereleaseUpdates === true;
        }

        applySettingsToUI(initialSettings);

        // Sidebar Navigation click handling
        document.querySelectorAll('.nav-item').forEach(item => {
            item.addEventListener('click', () => {
                document.querySelectorAll('.nav-item').forEach(i => i.classList.remove('active'));
                item.classList.add('active');
                const targetId = item.getAttribute('data-target');
                const targetEl = document.getElementById(targetId);
                if (targetEl) {
                    targetEl.scrollIntoView({ behavior: 'smooth', block: 'start' });
                }
            });
        });

        // Search Input Filter
        document.getElementById('searchSettings').addEventListener('input', (e) => {
            const query = e.target.value.toLowerCase().trim();
            const cards = document.querySelectorAll('.settings-card, .about-card');
            cards.forEach(card => {
                const text = card.innerText.toLowerCase();
                if (!query || text.includes(query)) {
                    card.style.display = '';
                } else {
                    card.style.display = 'none';
                }
            });
        });

        // Startup Behavior Radios
        document.querySelectorAll('#startupGroup .radio-option').forEach(el => {
            el.addEventListener('click', () => {
                document.querySelectorAll('#startupGroup .radio-option').forEach(r => r.classList.remove('selected'));
                el.classList.add('selected');
                const val = el.getAttribute('data-val');
                document.getElementById('txtCustomUrl').style.display = val === 'custom_url' ? 'block' : 'none';
                sendMessage({ type: 'updateSetting', key: 'StartupBehavior', value: val });
                showToast({{js:Settings_ToastStartup}});
            });
        });

        document.getElementById('txtCustomUrl').addEventListener('change', (e) => {
            sendMessage({ type: 'updateSetting', key: 'CustomStartupUrl', value: e.target.value.trim() });
            showToast({{js:Settings_ToastStartUrl}});
        });

        // General Toggles
        document.getElementById('chkShowHomeButton').addEventListener('change', (e) => {
            sendMessage({ type: 'updateSetting', key: 'ShowHomeButton', value: e.target.checked });
            showToast({{js:Settings_ToastHomeButton}});
        });

        document.getElementById('chkStartpageFavorites').addEventListener('change', (e) => {
            sendMessage({ type: 'updateSetting', key: 'IsStartpageFavoritesVisible', value: e.target.checked });
            showToast({{js:Settings_ToastStartpageShortcuts}});
        });

        const selLanguageEl = document.getElementById('selLanguage');
        if (selLanguageEl) {
            selLanguageEl.addEventListener('change', (e) => {
                sendMessage({ type: 'updateSetting', key: 'Language', value: e.target.value });
                showToast({{js:Settings_ToastLanguage}});
            });
        }

        // Search Engine Cards
        document.querySelectorAll('#engineGrid .engine-card').forEach(el => {
            el.addEventListener('click', () => {
                document.querySelectorAll('#engineGrid .engine-card').forEach(c => c.classList.remove('active'));
                el.classList.add('active');
                const engine = el.getAttribute('data-engine');
                sendMessage({ type: 'updateSetting', key: 'SearchEngine', value: engine });
                showToast(fmt({{js:Settings_ToastSearchEngine}}, engine));
            });
        });

        document.getElementById('chkSearchSuggestions').addEventListener('change', (e) => {
            sendMessage({ type: 'updateSetting', key: 'EnableSearchSuggestions', value: e.target.checked });
            showToast({{js:Settings_ToastSuggestions}});
        });

        // Theme Presets
        document.querySelectorAll('#themeGrid .theme-card').forEach(el => {
            el.addEventListener('click', () => {
                document.querySelectorAll('#themeGrid .theme-card').forEach(c => c.classList.remove('active'));
                el.classList.add('active');
                const preset = el.getAttribute('data-preset');
                sendMessage({ type: 'setThemePreset', preset: preset });
                showToast({{js:Settings_ToastTheme}});
            });
        });

        // Accent Color Swatches
        document.querySelectorAll('#accentPalette .color-swatch').forEach(el => {
            el.addEventListener('click', () => {
                document.querySelectorAll('#accentPalette .color-swatch').forEach(s => s.classList.remove('active'));
                el.classList.add('active');
                const hex = el.getAttribute('data-color');
                document.getElementById('txtCustomAccent').value = hex;
                sendMessage({ type: 'setAccentColor', hex: hex });
                showToast({{js:Settings_ToastAccent}});
            });
        });

        document.getElementById('btnApplyAccent').addEventListener('click', () => {
            let hex = document.getElementById('txtCustomAccent').value.trim();
            if (!hex.startsWith('#')) hex = '#' + hex;
            if (/^#[0-9A-Fa-f]{6}$/.test(hex)) {
                sendMessage({ type: 'setAccentColor', hex: hex });
                showToast({{js:Settings_ToastCustomAccent}});
            } else {
                alert({{js:Settings_InvalidHex}});
            }
        });

        // Appearance Toggles & Zoom
        document.getElementById('chkBookmarksBar').addEventListener('change', (e) => {
            sendMessage({ type: 'updateSetting', key: 'IsBookmarksBarVisible', value: e.target.checked });
            showToast({{js:Settings_ToastBookmarksBar}});
        });

        document.getElementById('chkSidebar').addEventListener('change', (e) => {
            sendMessage({ type: 'updateSetting', key: 'IsSidebarVisible', value: e.target.checked });
            showToast({{js:Settings_ToastSidebar}});
        });

        document.getElementById('selZoom').addEventListener('change', (e) => {
            const zoom = parseInt(e.target.value, 10);
            sendMessage({ type: 'updateSetting', key: 'DefaultZoomPercent', value: zoom });
            showToast(fmt({{js:Settings_ToastZoom}}, zoom));
        });

        // Toolbar Buttons Visibility Toggles
        const toolbarButtons = [
            { id: 'chkShowSidebarButton', key: 'ShowSidebarButton' },
            { id: 'chkShowBackButton', key: 'ShowBackButton' },
            { id: 'chkShowForwardButton', key: 'ShowForwardButton' },
            { id: 'chkShowReloadButton', key: 'ShowReloadButton' },
            { id: 'chkShowSearchEngineSelector', key: 'ShowSearchEngineSelector' },
            { id: 'chkShowExtensionsButton', key: 'ShowExtensionsButton' },
            { id: 'chkShowDownloadsButton', key: 'ShowDownloadsButton' }
        ];

        toolbarButtons.forEach(btn => {
            const el = document.getElementById(btn.id);
            if (el) {
                el.addEventListener('change', (e) => {
                    sendMessage({ type: 'updateSetting', key: btn.key, value: e.target.checked });
                    showToast({{js:Settings_ToastToolbar}});
                });
            }
        });

        // AdBlocker (Echo Shield) Toggles & Update
        const chkAdBlock = document.getElementById('chkIsAdBlockerEnabled');
        if (chkAdBlock) {
            chkAdBlock.addEventListener('change', (e) => {
                sendMessage({ type: 'updateSetting', key: 'IsAdBlockerEnabled', value: e.target.checked });
                showToast({{js:Settings_ToastAdBlocker}});
            });
        }

        const btnUpdateFilter = document.getElementById('btnUpdateAdBlockFilter');
        if (btnUpdateFilter) {
            btnUpdateFilter.addEventListener('click', () => {
                btnUpdateFilter.disabled = true;
                btnUpdateFilter.innerText = {{js:Shield_LoadingFilters}};
                sendMessage({ type: 'updateAdBlockFilter' });
            });
        }

        window.onAdBlockFilterUpdated = function(count) {
            const btn = document.getElementById('btnUpdateAdBlockFilter');
            if (btn) {
                btn.disabled = false;
                btn.innerText = {{js:Shield_Updated}};
                setTimeout(() => { btn.innerText = {{js:Shield_UpdateFilters}}; }, 2000);
            }
            const desc = document.getElementById('lblFilterRuleDesc');
            if (desc) {
                desc.innerText = fmt({{js:Settings_FilterRulesActive}}, count ? count.toLocaleString(document.documentElement.lang) : {{js:Settings_FilterRulesMany}});
            }
            showToast(fmt({{js:Settings_ToastFilterUpdated}}, count));
        };

        // Shield Level Cards
        document.querySelectorAll('#shieldGrid .shield-card').forEach(el => {
            el.addEventListener('click', () => {
                document.querySelectorAll('#shieldGrid .shield-card').forEach(c => c.classList.remove('active'));
                el.classList.add('active');
                const lvl = el.getAttribute('data-level');
                sendMessage({ type: 'updateSetting', key: 'TrackingPreventionLevel', value: lvl });
                showToast(fmt({{js:Settings_ToastShieldLevel}}, lvl));
            });
        });

        // Privacy Toggles
        document.getElementById('chkBlockPopups').addEventListener('change', (e) => {
            sendMessage({ type: 'updateSetting', key: 'BlockPopups', value: e.target.checked });
            showToast({{js:Settings_ToastPopups}});
        });

        document.getElementById('chkEnableJavaScript').addEventListener('change', (e) => {
            sendMessage({ type: 'updateSetting', key: 'EnableJavaScript', value: e.target.checked });
            showToast({{js:Settings_ToastJavaScript}});
        });

        document.getElementById('chkSendDoNotTrack').addEventListener('change', (e) => {
            sendMessage({ type: 'updateSetting', key: 'SendDoNotTrack', value: e.target.checked });
            showToast({{js:Settings_ToastDnt}});
        });

        // Clear Browsing Data Modal
        const modalClear = document.getElementById('modalClearData');
        document.getElementById('btnOpenClearData').addEventListener('click', () => {
            modalClear.classList.add('open');
        });
        document.getElementById('btnCancelClearData').addEventListener('click', () => {
            modalClear.classList.remove('open');
        });
        document.getElementById('btnConfirmClearData').addEventListener('click', () => {
            const clearHistory = document.getElementById('cbClearHistory').checked;
            const clearCookies = document.getElementById('cbClearCookies').checked;
            const clearCache = document.getElementById('cbClearCache').checked;
            sendMessage({
                type: 'clearBrowsingData',
                clearHistory: clearHistory,
                clearCookies: clearCookies,
                clearCache: clearCache
            });
            modalClear.classList.remove('open');
            showToast({{js:Settings_ToastClearing}});
        });

        // Downloads
        document.getElementById('btnChangeDownloadPath').addEventListener('click', () => {
            sendMessage({ type: 'browseDownloadFolder' });
        });

        document.getElementById('btnOpenDownloadFolder').addEventListener('click', () => {
            sendMessage({ type: 'openDownloadFolder' });
        });

        document.getElementById('chkAskDownloadLocation').addEventListener('change', (e) => {
            sendMessage({ type: 'updateSetting', key: 'AskDownloadLocation', value: e.target.checked });
            showToast({{js:Settings_ToastDownloadOption}});
        });

        // Tabs & Reset
        document.getElementById('chkOpenTabsInBackground').addEventListener('change', (e) => {
            sendMessage({ type: 'updateSetting', key: 'OpenNewTabInBackground', value: e.target.checked });
            showToast({{js:Settings_ToastTabBehavior}});
        });

        document.getElementById('chkWarnCloseTabs').addEventListener('change', (e) => {
            sendMessage({ type: 'updateSetting', key: 'WarnOnClosingMultipleTabs', value: e.target.checked });
            showToast({{js:Settings_ToastWarning}});
        });

        document.getElementById('btnResetSettings').addEventListener('click', () => {
            if (confirm({{js:Settings_ResetConfirm}})) {
                sendMessage({ type: 'resetSettings' });
                showToast({{js:Settings_ToastReset}});
            }
        });

        // Updates
        if (document.getElementById('chkAutoCheckUpdates')) {
            document.getElementById('chkAutoCheckUpdates').addEventListener('change', (e) => {
                sendMessage({ type: 'updateSetting', key: 'AutoCheckForUpdates', value: e.target.checked });
            });
        }

        if (document.getElementById('chkCheckPrereleases')) {
            document.getElementById('chkCheckPrereleases').addEventListener('change', (e) => {
                sendMessage({ type: 'updateSetting', key: 'CheckPrereleaseUpdates', value: e.target.checked });
            });
        }

        const btnCheckUpdates = document.getElementById('btnCheckUpdates');
        if (btnCheckUpdates) {
            btnCheckUpdates.addEventListener('click', () => {
                if (btnCheckUpdates.getAttribute('data-action') === 'restart') {
                    sendMessage({ type: 'restartToApplyUpdate' });
                } else {
                    sendMessage({ type: 'checkForUpdates' });
                }
            });
        }

        // Host callbacks
        window.onUpdateStatusChanged = function(data) {
            const box = document.getElementById('aboutStatusBox');
            const lbl = document.getElementById('lblAboutStatus');
            const btn = document.getElementById('btnCheckUpdates');
            const progCont = document.getElementById('updateProgressContainer');
            const progBar = document.getElementById('updateProgressBar');

            if (!box || !lbl || !btn) return;

            const btnSpan = btn.querySelector('span') || btn;
            box.className = 'about-status';
            if (progCont) progCont.style.display = 'none';
            btn.removeAttribute('data-action');
            btn.disabled = false;

            if (data.status === 'Checking') {
                box.classList.add('checking');
                lbl.innerText = data.message || {{js:Update_Checking}};
                btn.disabled = true;
                btnSpan.innerText = {{js:Update_Checking}};
            } else if (data.status === 'Downloading') {
                box.classList.add('checking');
                lbl.innerText = data.message || (data.progress + '%');
                if (progCont) progCont.style.display = 'block';
                if (progBar) progBar.style.width = (data.progress || 0) + '%';
                btn.disabled = true;
                btnSpan.innerText = (data.progress || 0) + '%';
            } else if (data.status === 'ReadyToRestart') {
                box.classList.add('ready');
                lbl.innerText = data.message || {{js:Update_Ready}};
                btn.setAttribute('data-action', 'restart');
                btnSpan.innerText = {{js:Update_RestartNow}};
            } else if (data.status === 'UpToDate') {
                lbl.innerText = data.message || {{js:Settings_AboutUpToDate}};
                btnSpan.innerText = {{js:Settings_CheckForUpdates}};
            } else if (data.status === 'NotInstalled') {
                box.classList.add('warning');
                lbl.innerText = data.message || {{js:Update_DevMode}};
                btnSpan.innerText = {{js:Settings_CheckForUpdates}};
            } else if (data.status === 'Error') {
                box.classList.add('warning');
                lbl.innerText = data.message || {{js:Update_Error}};
                btnSpan.innerText = {{js:Settings_CheckForUpdates}};
            } else {
                lbl.innerText = {{js:Settings_AboutUpToDate}};
                btnSpan.innerText = {{js:Settings_CheckForUpdates}};
            }
        };

        window.onSettingUpdatedFromHost = function(newSettings) {
            applySettingsToUI(newSettings);
            showToast({{js:Settings_ToastSynced}});
        };

        window.onBrowsingDataCleared = function() {
            showToast({{js:Settings_ToastDataCleared}});
        };

        window.onDownloadPathChanged = function(newPath) {
            document.getElementById('txtDownloadPath').value = newPath;
            showToast({{js:Settings_ToastDownloadPath}});
        };
    </script>
</body>
</html>";
    }
}
