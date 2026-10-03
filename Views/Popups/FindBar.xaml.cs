using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EchoBrowser.Services;

namespace EchoBrowser.Views.Popups
{
    /// <summary>Suchleiste "Auf Seite suchen" (Strg+F). Die eigentliche Suche steuert MainWindow.Find.cs.</summary>
    public partial class FindBar : UserControl
    {
        /// <summary>Suchbegriff oder Groß-/Kleinschreibung geändert: (Begriff, Groß-/Kleinschreibung beachten).</summary>
        public event Action<string, bool>? SearchChanged;
        public event Action? NextRequested;
        public event Action? PreviousRequested;
        public event Action? CloseRequested;

        public FindBar()
        {
            InitializeComponent();
        }

        public string SearchText => txtFind.Text;

        public bool MatchCase => btnMatchCase.IsChecked == true;

        /// <summary>Eingabefeld – das Hauptfenster fokussiert es (siehe MainWindow.FocusWpfInput).</summary>
        public TextBox InputBox => txtFind;

        /// <summary>Trefferanzeige: "3/12", "Keine Treffer" oder leer (kein Suchbegriff).</summary>
        public void ShowResult(int activeIndex, int count)
        {
            if (string.IsNullOrEmpty(txtFind.Text))
            {
                txtResult.Text = "";
            }
            else if (count <= 0)
            {
                txtResult.Text = Tr.Get("Find_NoResults");
                txtResult.SetResourceReference(TextBlock.ForegroundProperty, "StatusDangerBrush");
                return;
            }
            else
            {
                txtResult.Text = $"{Math.Max(activeIndex, 1)}/{count}";
            }
            txtResult.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        }

        private void TxtFind_TextChanged(object sender, TextChangedEventArgs e) =>
            SearchChanged?.Invoke(txtFind.Text, MatchCase);

        private void BtnMatchCase_Click(object sender, RoutedEventArgs e) =>
            SearchChanged?.Invoke(txtFind.Text, MatchCase);

        private void TxtFind_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) PreviousRequested?.Invoke();
                else NextRequested?.Invoke();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseRequested?.Invoke();
                e.Handled = true;
            }
        }

        private void BtnPrevious_Click(object sender, RoutedEventArgs e) => PreviousRequested?.Invoke();

        private void BtnNext_Click(object sender, RoutedEventArgs e) => NextRequested?.Invoke();

        private void BtnClose_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    }
}
