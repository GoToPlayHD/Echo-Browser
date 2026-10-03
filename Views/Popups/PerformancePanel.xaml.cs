using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using EchoBrowser.Models;
using EchoBrowser.Services;

namespace EchoBrowser.Views.Popups
{
    /// <summary>Messergebnis für das Leistungs-Panel.</summary>
    public sealed record PerformanceSnapshot(long TotalBytes, long SharedBytes, int SleepingTabs, int TabCount);

    /// <summary>Leistung: Speicher gesamt und pro Tab, Tab-Schlaf einstellen und sofort auslösen.</summary>
    public partial class PerformancePanel : UserControl
    {
        public event Action? SleepNowRequested;
        public event Action<int>? SleepMinutesChanged;
        public event Action<BrowserTab>? TabChosen;

        public PerformancePanel()
        {
            InitializeComponent();
        }

        /// <summary>"30 Min.", "2 Std.", "Nie" – auch für die Einstellungsseite.</summary>
        public static string SleepChoiceLabel(int minutes) =>
            minutes <= 0 ? Tr.Get("Perf_Never")
            : minutes >= 60 ? Tr.Format("Perf_HoursShort", minutes / 60)
            : Tr.Format("Perf_MinutesShort", minutes);

        public void ShowSleepSetting(int minutes)
        {
            panelSleepChoices.Children.Clear();
            foreach (int choice in TabSleepPolicy.Choices)
            {
                var chip = new Button
                {
                    Content = SleepChoiceLabel(choice),
                    Tag = choice,
                    Margin = new Thickness(0, 0, 2, 2)
                };
                chip.SetResourceReference(StyleProperty, "BookmarkChipStyle");
                if (choice == minutes)
                {
                    chip.SetResourceReference(BackgroundProperty, "SurfaceHoverBrush");
                    chip.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
                    chip.FontWeight = FontWeights.SemiBold;
                }
                chip.Click += (s, e) =>
                {
                    ShowSleepSetting(choice);
                    SleepMinutesChanged?.Invoke(choice);
                };
                panelSleepChoices.Children.Add(chip);
            }
        }

        /// <summary>Tabs nach Speicher sortiert; die Zeilen aktualisieren sich über die Bindungen selbst.</summary>
        public void ShowTabs(IEnumerable<BrowserTab> tabs)
        {
            listTabs.ItemsSource = tabs
                .OrderByDescending(t => t.MemoryBytes ?? -1)
                .ThenBy(t => t.IsDiscarded)
                .ToList();
        }

        public void ShowSnapshot(PerformanceSnapshot? snapshot)
        {
            if (snapshot == null)
            {
                txtTotal.Text = "–";
                txtSummary.Text = Tr.Get("Perf_Measuring");
                txtShared.Visibility = Visibility.Collapsed;
                return;
            }

            txtTotal.Text = MemoryFormat.Format(snapshot.TotalBytes);
            txtSummary.Text = Tr.Format("Perf_Summary", snapshot.SleepingTabs, snapshot.TabCount);
            txtShared.Text = Tr.Format("Perf_Shared", MemoryFormat.Format(snapshot.SharedBytes));
            txtShared.Visibility = Visibility.Visible;
        }

        private void BtnSleepNow_Click(object sender, RoutedEventArgs e) => SleepNowRequested?.Invoke();

        private void TabRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: BrowserTab tab }) TabChosen?.Invoke(tab);
        }
    }
}
