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

        /// <summary>Formular zurücksetzen und den Fokus in das Namensfeld setzen.</summary>
        public void Prepare()
        {
            txtAddGroupName.Text = "";
            txtAddGroupName.Focus();
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
