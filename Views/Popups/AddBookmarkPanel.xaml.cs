using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using EchoBrowser.Models;
using EchoBrowser.Services;

namespace EchoBrowser.Views.Popups
{
    /// <summary>Formular "Lesezeichen hinzufügen" mit Auswahl der Zielgruppe.</summary>
    public partial class AddBookmarkPanel : UserControl
    {
        /// <summary>Gültige Eingabe bestätigt: Titel, URL, Zielgruppe (null = Hauptleiste).</summary>
        public event Action<string, string, Bookmark?>? BookmarkCreated;

        /// <summary>Abbrechen wurde geklickt.</summary>
        public event Action? Cancelled;

        public AddBookmarkPanel()
        {
            InitializeComponent();
        }

        /// <summary>Formular füllen und den Fokus in das Namensfeld setzen.</summary>
        /// <param name="title">Vorschlag für den Namen (z.B. Titel des aktuellen Tabs).</param>
        /// <param name="url">Vorschlag für die Adresse, leer = "https://".</param>
        /// <param name="groups">Verfügbare Lesezeichen-Gruppen.</param>
        /// <param name="preselectedGroup">Vorausgewählte Gruppe, null = Hauptleiste.</param>
        public void Prepare(string title, string url, IEnumerable<Bookmark> groups, Bookmark? preselectedGroup)
        {
            txtAddBmTitle.Text = title;
            txtAddBmUrl.Text = string.IsNullOrWhiteSpace(url) ? "https://" : url;

            cmbAddBmGroup.Items.Clear();
            var mainItem = new ComboBoxItem { Content = Tr.Get("Bookmark_MainBar"), Tag = null };
            cmbAddBmGroup.Items.Add(mainItem);
            cmbAddBmGroup.SelectedItem = mainItem;

            foreach (var group in groups)
            {
                var item = new ComboBoxItem { Content = "📁 " + group.Title, Tag = group };
                cmbAddBmGroup.Items.Add(item);
                if (preselectedGroup == group)
                {
                    cmbAddBmGroup.SelectedItem = item;
                }
            }

            txtAddBmTitle.Focus();
        }

        private void BtnSaveAddBm_Click(object sender, RoutedEventArgs e)
        {
            if (UrlHelper.IsEmptyInput(txtAddBmUrl.Text)) return;

            string url = UrlHelper.EnsureScheme(txtAddBmUrl.Text);
            string title = txtAddBmTitle.Text.Trim();
            if (title.Length == 0)
            {
                title = UrlHelper.HostForDisplay(url);
            }

            var targetGroup = (cmbAddBmGroup.SelectedItem as ComboBoxItem)?.Tag as Bookmark;
            BookmarkCreated?.Invoke(title, url, targetGroup);
        }

        private void BtnCancelAddBm_Click(object sender, RoutedEventArgs e) => Cancelled?.Invoke();
    }
}
