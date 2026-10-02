using System;
using System.Windows;
using System.Windows.Controls;
using EchoBrowser.Services;

namespace EchoBrowser.Views.Popups
{
    /// <summary>Inhalt des Theme-Popups: Preset und Akzentfarbe wählen.</summary>
    public partial class ThemePanel : UserControl
    {
        /// <summary>Ein Preset oder eine Farbe wurde gewählt – das Popup kann geschlossen werden.</summary>
        public event Action? SelectionMade;

        public ThemePanel()
        {
            InitializeComponent();
        }

        private void BtnThemePreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string presetName })
            {
                ThemeManager.Instance.ApplyAndSavePreset(presetName);
                SelectionMade?.Invoke();
            }
        }

        private void BtnAccentColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string hex })
            {
                ThemeManager.Instance.ApplyAndSaveAccentColor(hex);
                SelectionMade?.Invoke();
            }
        }
    }
}
