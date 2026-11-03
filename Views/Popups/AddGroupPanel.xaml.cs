using System;
using System.Windows;
using System.Windows.Controls;

namespace EchoBrowser.Views.Popups
{
    /// <summary>Formular "Neue Lesezeichen-Gruppe erstellen".</summary>
    public partial class AddGroupPanel : UserControl
    {
        /// <summary>Ein nicht leerer Gruppenname wurde bestätigt.</summary>
        public event Action<string>? GroupCreated;

        /// <summary>Abbrechen wurde geklickt.</summary>
        public event Action? Cancelled;

        public AddGroupPanel()
        {
            InitializeComponent();
        }

        /// <summary>Erstes Eingabefeld – das Hauptfenster setzt den Fokus darauf (siehe FocusWpfInput).</summary>
        public TextBox InitialFocusTarget => txtAddGroupName;

        /// <summary>Formular zurücksetzen.</summary>
        public void Prepare()
        {
            txtAddGroupName.Text = "";
        }

        private void BtnSaveAddGroup_Click(object sender, RoutedEventArgs e)
        {
            string name = txtAddGroupName.Text.Trim();
            if (name.Length > 0)
            {
                GroupCreated?.Invoke(name);
            }
        }

        private void BtnCancelAddGroup_Click(object sender, RoutedEventArgs e) => Cancelled?.Invoke();
    }
}
