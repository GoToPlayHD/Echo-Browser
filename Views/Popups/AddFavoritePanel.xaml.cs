using System;
using System.Windows;
using System.Windows.Controls;
using EchoBrowser.Services;

namespace EchoBrowser.Views.Popups
{
    /// <summary>Formular "Favorit hinzufügen" für die Seitenleiste.</summary>
    public partial class AddFavoritePanel : UserControl
    {
        private const string DefaultIconKey = "globe";

        private string _iconKey = DefaultIconKey;

        /// <summary>Gültige Eingabe bestätigt: Titel, URL, Icon-Key, Icon-Farbe.</summary>
        public event Action<string, string, string, string>? FavoriteCreated;

        /// <summary>Abbrechen wurde geklickt.</summary>
        public event Action? Cancelled;

        public AddFavoritePanel()
        {
            InitializeComponent();
        }

        /// <summary>Formular zurücksetzen und den Fokus in das Namensfeld setzen.</summary>
        public void Prepare()
        {
            txtAddFavTitle.Text = "";
            txtAddFavUrl.Text = "https://";
            _iconKey = DefaultIconKey;
            txtAddFavTitle.Focus();
        }

        private static string ColorForIcon(string iconKey) => iconKey switch
        {
            "youtube" => "#FF4444",
            "reddit" => "#FF6633",
            "chatgpt" => "#34D399",
            "google" => "#4285F4",
            _ => "#C4C7CC"
        };

        private void SelectFavIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string iconKey })
            {
                _iconKey = iconKey;
            }
        }

        private void BtnSaveAddFavorite_Click(object sender, RoutedEventArgs e)
        {
            string url = txtAddFavUrl.Text;
            if (UrlHelper.IsEmptyInput(url))
            {
                ThemedDialogWindow.ShowMessage(Window.GetWindow(this), Tr.Get("Sidebar_AddFavoriteTitle"), Tr.Get("Sidebar_InvalidUrl"), MessageBoxImage.Warning);
                return;
            }

            url = UrlHelper.EnsureScheme(url);
            string title = txtAddFavTitle.Text.Trim();
            if (title.Length == 0)
            {
                title = UrlHelper.HostForDisplay(url);
            }

            FavoriteCreated?.Invoke(title, url, _iconKey, ColorForIcon(_iconKey));
        }

        private void BtnCancelAddFavorite_Click(object sender, RoutedEventArgs e) => Cancelled?.Invoke();
    }
}
