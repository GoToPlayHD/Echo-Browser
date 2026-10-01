using System;
using System.Collections.Generic;

namespace EchoBrowser.Services
{
    public class LocalizationService
    {
        private static LocalizationService? _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        public event Action? LanguageChanged;

        private string _currentLanguage = "de";
        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                string norm = (value ?? "de").ToLowerInvariant().Trim();
                if (norm != "de" && norm != "en" && norm != "fr" && norm != "es")
                {
                    norm = "de";
                }

                if (_currentLanguage != norm)
                {
                    _currentLanguage = norm;
                    LanguageChanged?.Invoke();
                }
            }
        }

        private readonly Dictionary<string, Dictionary<string, string>> _translations = new(StringComparer.OrdinalIgnoreCase);

        private LocalizationService()
        {
            RegisterTranslations();
        }

        public void SetLanguage(string lang)
        {
            CurrentLanguage = lang;
        }

        public string GetString(string key, string fallback = "")
        {
            if (string.IsNullOrWhiteSpace(key)) return fallback;

            if (_translations.TryGetValue(key, out var langDict))
            {
                if (langDict.TryGetValue(_currentLanguage, out var val) && !string.IsNullOrEmpty(val))
                {
                    return val;
                }
                if (langDict.TryGetValue("de", out var deVal) && !string.IsNullOrEmpty(deVal))
                {
                    return deVal;
                }
                if (langDict.TryGetValue("en", out var enVal) && !string.IsNullOrEmpty(enVal))
                {
                    return enVal;
                }
            }

            return fallback;
        }

        private void RegisterTranslations()
        {
            // Close Browser & Multi-Tab Dialog
            Add("Dialog_CloseBrowserTitle",
                de: "Echo-Browser beenden",
                en: "Close Echo-Browser",
                fr: "Fermer Echo-Browser",
                es: "Cerrar Echo-Browser");

            Add("Dialog_CloseMultipleTabsMessage",
                de: "Möchtest du wirklich alle {0} geöffneten Tabs schließen?",
                en: "Do you really want to close all {0} open tabs?",
                fr: "Voulez-vous vraiment fermer tous les {0} onglets ouverts ?",
                es: "¿Realmente deseas cerrar todas las {0} pestañas abiertas?");

            Add("Dialog_CloseAllTabs",
                de: "Alle Tabs schließen",
                en: "Close all tabs",
                fr: "Fermer tous les onglets",
                es: "Cerrar todas las pestañas");

            Add("Dialog_Cancel",
                de: "Abbrechen",
                en: "Cancel",
                fr: "Annuler",
                es: "Cancelar");

            Add("Dialog_OK",
                de: "OK",
                en: "OK",
                fr: "OK",
                es: "Aceptar");

            // Navigation & Toolbar Tooltips
            Add("Nav_Back",
                de: "Zurück (Alt+Links)",
                en: "Back (Alt+Left)",
                fr: "Précédent (Alt+Gauche)",
                es: "Atrás (Alt+Izquierda)");

            Add("Nav_Forward",
                de: "Vorwärts (Alt+Rechts)",
                en: "Forward (Alt+Right)",
                fr: "Suivant (Alt+Droite)",
                es: "Adelante (Alt+Derecha)");

            Add("Nav_Reload",
                de: "Neu laden (F5)",
                en: "Reload (F5)",
                fr: "Actualiser (F5)",
                es: "Recargar (F5)");

            Add("Nav_Home",
                de: "Startseite",
                en: "Home",
                fr: "Page d'accueil",
                es: "Página de inicio");

            Add("Nav_NewTab",
                de: "Neuer Tab (Strg+T)",
                en: "New Tab (Ctrl+T)",
                fr: "Nouvel onglet (Ctrl+T)",
                es: "Nueva pestaña (Ctrl+T)");

            Add("Nav_CloseTab",
                de: "Tab schließen",
                en: "Close Tab",
                fr: "Fermer l'onglet",
                es: "Cerrar pestaña");

            Add("Nav_AddressPlaceholder",
                de: "Suchen oder Webadresse eingeben...",
                en: "Search or enter web address...",
                fr: "Rechercher ou saisir une adresse...",
                es: "Buscar o escribir dirección web...");

            Add("Nav_EchoShield",
                de: "Echo Shield Schutz",
                en: "Echo Shield Protection",
                fr: "Protection Echo Shield",
                es: "Protección Echo Shield");

            Add("Nav_Extensions",
                de: "Erweiterungen",
                en: "Extensions",
                fr: "Extensions",
                es: "Extensiones");

            Add("Nav_Downloads",
                de: "Downloads",
                en: "Downloads",
                fr: "Téléchargements",
                es: "Descargas");

            Add("Nav_History",
                de: "Verlauf (Strg+H)",
                en: "History (Ctrl+H)",
                fr: "Historique (Ctrl+H)",
                es: "Historial (Ctrl+H)");

            Add("Nav_Bookmarks",
                de: "Lesezeichen (Strg+B)",
                en: "Bookmarks (Ctrl+B)",
                fr: "Favoris (Ctrl+B)",
                es: "Marcadores (Ctrl+B)");

            Add("Nav_Settings",
                de: "Einstellungen",
                en: "Settings",
                fr: "Paramètres",
                es: "Configuración");

            // Echo Shield Flyout
            Add("Shield_ActiveStateOn",
                de: "Echo Shield: Aktiviert",
                en: "Echo Shield: Enabled",
                fr: "Echo Shield : Activé",
                es: "Echo Shield: Activado");

            Add("Shield_ActiveStateOff",
                de: "Echo Shield: Deaktiviert",
                en: "Echo Shield: Disabled",
                fr: "Echo Shield : Désactivé",
                es: "Echo Shield: Desactivado");

            Add("Shield_SubtitleOn",
                de: "Werbe- & Tracking-Schutz aktiv",
                en: "Ad & tracking protection active",
                fr: "Protection publicitaire et suivi active",
                es: "Protección contra anuncios y rastreo activa");

            Add("Shield_SubtitleOff",
                de: "Schutz für diese Seite pausiert",
                en: "Protection paused for this site",
                fr: "Protection suspendue pour ce site",
                es: "Protección pausada para este sitio");

            Add("Shield_SiteToggle",
                de: "Schutz auf dieser Website",
                en: "Protection on this website",
                fr: "Protection sur ce site",
                es: "Protección en este sitio web");

            Add("Shield_SiteToggleDesc",
                de: "Tracker & Anzeigen auf dieser Domain blockieren",
                en: "Block trackers & ads on this domain",
                fr: "Bloquer les traqueurs et publicités",
                es: "Bloquear rastreadores y anuncios en este dominio");

            Add("Shield_GlobalToggle",
                de: "Echo Shield global aktiv",
                en: "Echo Shield globally active",
                fr: "Echo Shield actif globalement",
                es: "Echo Shield globalmente activo");

            Add("Shield_GlobalToggleDesc",
                de: "Netzwerkfilter für alle Tabs aktivieren",
                en: "Enable network filters for all tabs",
                fr: "Activer les filtres pour tous les onglets",
                es: "Activar filtros de red para todas las pestañas");

            Add("Shield_TrackersBlockedFormat",
                de: "{0} Tracker und Werbeanzeigen blockiert",
                en: "{0} trackers and ads blocked",
                fr: "{0} traqueurs et pubs bloqués",
                es: "{0} rastreadores y anuncios bloqueados");

            Add("Shield_FilterRuleCountFormat",
                de: "{0:N0} Filterregeln geladen",
                en: "{0:N0} filter rules loaded",
                fr: "{0:N0} règles de filtrage chargées",
                es: "{0:N0} reglas de filtrado cargadas");

            Add("Shield_UpdateFilters",
                de: "Filter aktualisieren",
                en: "Update filters",
                fr: "Mettre à jour les filtres",
                es: "Actualizar filtros");

            Add("Shield_Updating",
                de: "Lade Filter...",
                en: "Updating...",
                fr: "Mise à jour...",
                es: "Actualizando...");

            Add("Shield_Updated",
                de: "Aktualisiert!",
                en: "Updated!",
                fr: "Mis à jour !",
                es: "¡Actualizado!");

            Add("Shield_JavaScript",
                de: "JavaScript ausführen",
                en: "Execute JavaScript",
                fr: "Exécuter JavaScript",
                es: "Ejecutar JavaScript");

            Add("Shield_JavaScriptDesc",
                de: "Skripte für diese Seite erlauben",
                en: "Allow scripts on this site",
                fr: "Autoriser les scripts",
                es: "Permitir scripts en este sitio");

            Add("Shield_Popups",
                de: "Popups blockieren",
                en: "Block popups",
                fr: "Bloquer les popups",
                es: "Bloquear ventanas emergentes");

            Add("Shield_PopupsDesc",
                de: "Unerwünschte Fenster abfangen",
                en: "Catch unwanted windows",
                fr: "Intercepter les fenêtres indésirables",
                es: "Interceptar ventanas no deseadas");

            Add("Shield_ClearCookies",
                de: "Cookies & Websitedaten leeren",
                en: "Clear cookies & site data",
                fr: "Vider cookies et données",
                es: "Borrar cookies y datos");

            // Extensions UI
            Add("Ext_Title",
                de: "Erweiterungen",
                en: "Extensions",
                fr: "Extensions",
                es: "Extensiones");

            Add("Ext_Subtitle",
                de: "Browser-Erweiterungen verwalten",
                en: "Manage browser extensions",
                fr: "Gérer les extensions",
                es: "Administrar extensiones");

            Add("Ext_Empty",
                de: "Keine Erweiterungen installiert",
                en: "No extensions installed",
                fr: "Aucune extension installée",
                es: "No hay extensiones instaladas");

            Add("Ext_InstallCrx",
                de: "Aus .crx installieren",
                en: "Install from .crx",
                fr: "Installer depuis .crx",
                es: "Instalar desde .crx");

            Add("Ext_ChromeStore",
                de: "Chrome Web Store öffnen",
                en: "Open Chrome Web Store",
                fr: "Ouvrir Chrome Web Store",
                es: "Abrir Chrome Web Store");

            Add("Ext_OpenPopup",
                de: "Öffnen",
                en: "Open",
                fr: "Ouvrir",
                es: "Abrir");

            Add("Ext_Options",
                de: "Optionen",
                en: "Options",
                fr: "Options",
                es: "Opciones");

            Add("Ext_Remove",
                de: "Entfernen",
                en: "Remove",
                fr: "Supprimer",
                es: "Eliminar");

            Add("Ext_Pin",
                de: "Anheften",
                en: "Pin",
                fr: "Épingler",
                es: "Fijar");

            Add("Ext_Unpin",
                de: "Lösen",
                en: "Unpin",
                fr: "Détacher",
                es: "Desfijar");

            // Onboarding & Setup
            Add("Setup_HeaderTitle",
                de: "Sprachauswahl",
                en: "Language Selection",
                fr: "Choix de la langue",
                es: "Selección de idioma");

            Add("Setup_Subtitle",
                de: "Wähle deine bevorzugte Sprache für Echo-Browser",
                en: "Select your preferred language for Echo-Browser",
                fr: "Choisissez votre langue préférée pour Echo-Browser",
                es: "Elige tu idioma preferido para Echo-Browser");

            Add("Setup_Continue",
                de: "Fortfahren →",
                en: "Continue →",
                fr: "Continuer →",
                es: "Continuar →");
        }

        private void Add(string key, string de, string en, string fr, string es)
        {
            _translations[key] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "de", de },
                { "en", en },
                { "fr", fr },
                { "es", es }
            };
        }
    }
}
