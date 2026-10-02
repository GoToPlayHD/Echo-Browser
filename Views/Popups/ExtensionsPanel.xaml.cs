using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser.Views.Popups
{
    /// <summary>
    /// Inhalt des Erweiterungen-Flyouts: Liste installierter Erweiterungen mit Aktionen.
    /// Die Aktionen selbst (Installieren, Entfernen, Popup öffnen …) erledigt das Hauptfenster,
    /// weil sie das Browser-Profil und die Symbolleiste brauchen.
    /// </summary>
    public partial class ExtensionsPanel : UserControl
    {
        public event Action<CoreWebView2BrowserExtension>? PinToggleRequested;

        /// <summary>Popup der Erweiterung öffnen; das zweite Argument ist der Button, unter dem es erscheinen soll.</summary>
        public event Action<CoreWebView2BrowserExtension, FrameworkElement>? OpenPopupRequested;

        public event Action<CoreWebView2BrowserExtension>? OpenSettingsRequested;
        public event Action<CoreWebView2BrowserExtension, bool>? EnableRequested;
        public event Action<CoreWebView2BrowserExtension>? RemoveRequested;
        public event Action? InstallFromFileRequested;
        public event Action? OpenWebStoreRequested;

        public ExtensionsPanel()
        {
            InitializeComponent();
        }

        /// <summary>Liste anzeigen (leer oder null = Hinweis "Keine Erweiterungen installiert").</summary>
        public void ShowExtensions(IReadOnlyList<CoreWebView2BrowserExtension>? extensions)
        {
            bool isEmpty = extensions == null || extensions.Count == 0;
            txtEmptyExtensions.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            icExtensionsList.ItemsSource = isEmpty ? null : extensions;
        }

        private static CoreWebView2BrowserExtension? ExtensionOf(object sender) =>
            (sender as FrameworkElement)?.Tag as CoreWebView2BrowserExtension;

        private void BtnTogglePinExtension_Click(object sender, RoutedEventArgs e)
        {
            if (ExtensionOf(sender) is { } ext) PinToggleRequested?.Invoke(ext);
        }

        private void BtnOpenExtensionPopup_Click(object sender, RoutedEventArgs e)
        {
            if (ExtensionOf(sender) is { } ext && sender is FrameworkElement anchor) OpenPopupRequested?.Invoke(ext, anchor);
        }

        private void BtnOpenExtensionSettings_Click(object sender, RoutedEventArgs e)
        {
            if (ExtensionOf(sender) is { } ext) OpenSettingsRequested?.Invoke(ext);
        }

        private void ChkExtensionToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && ExtensionOf(sender) is { } ext) EnableRequested?.Invoke(ext, chk.IsChecked ?? true);
        }

        private void BtnRemoveExtension_Click(object sender, RoutedEventArgs e)
        {
            if (ExtensionOf(sender) is { } ext) RemoveRequested?.Invoke(ext);
        }

        private void BtnInstallExtensionFromFile_Click(object sender, RoutedEventArgs e) => InstallFromFileRequested?.Invoke();

        private void BtnOpenChromeWebStore_Click(object sender, RoutedEventArgs e) => OpenWebStoreRequested?.Invoke();
    }
}
