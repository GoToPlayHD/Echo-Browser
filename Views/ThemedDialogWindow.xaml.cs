using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EchoBrowser.Services;

namespace EchoBrowser.Views
{
    public partial class ThemedDialogWindow : Window
    {
        public bool ResultConfirmed { get; private set; }

        public ThemedDialogWindow()
        {
            InitializeComponent();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            ResultConfirmed = false;
            DialogResult = false;
            Close();
        }

        private void BtnSecondary_Click(object sender, RoutedEventArgs e)
        {
            ResultConfirmed = false;
            DialogResult = false;
            Close();
        }

        private void BtnPrimary_Click(object sender, RoutedEventArgs e)
        {
            ResultConfirmed = true;
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Nachfrage vor der Installation mit den Berechtigungen aus dem Manifest (wie Chrome: »Diese Erweiterung kann …«).
        /// </summary>
        /// <param name="source">Herkunft für die letzte Zeile, z.B. »Erweiterungs-ID: …« oder der Dateipfad.</param>
        public static bool ShowExtensionInstallPrompt(Window? owner, PreparedExtension extension, string? source = null)
        {
            var dlg = new ThemedDialogWindow
            {
                Owner = owner ?? Application.Current.MainWindow,
                txtDialogTitle = { Text = Tr.Get("Ext_InstallTitle") },
                txtHeadline = { Text = Tr.Format("Ext_InstallHeadline", extension.Name) },
                txtMessage = { Text = Tr.Get("Ext_InstallWarning") },
                btnPrimary = { Content = Tr.Get("Ext_AddButton") },
                btnSecondary = { Content = Tr.Get("Dialog_Cancel"), Visibility = Visibility.Visible }
            };

            // Icon: Puzzle / Extension – gelb, wenn die Erweiterung alle Websites lesen und ändern darf
            dlg.pathStatusIcon.Data = Geometry.Parse("M20.5 11H19V7c0-1.1-.9-2-2-2h-4V3.5C13 2.12 11.88 1 10.5 1S8 2.12 8 3.5V5H4c-1.1 0-1.99.9-1.99 2v3.8H3.5c1.49 0 2.7 1.21 2.7 2.7s-1.21 2.7-2.7 2.7H2V20c0 1.1.9 2 2 2h3.8v-1.5c0-1.49 1.21-2.7 2.7-2.7 1.49 0 2.7 1.21 2.7 2.7V22H17c1.1 0 2-.9 2-2v-4h1.5c1.38 0 2.5-1.12 2.5-2.5s-1.12-2.5-2.5-2.5z");
            dlg.pathStatusIcon.Fill = (Brush)dlg.FindResource(extension.Permissions.AllSites ? "StatusWarningBrush" : "ShieldActiveBrush");

            dlg.ShowPermissionDetails(extension, source);
            return dlg.ShowDialog() == true;
        }

        /// <summary>
        /// Update einer Erweiterung verlangt neue Berechtigungen: erst nach Zustimmung einspielen (wie Chrome).
        /// Abgelehnt bleibt die bisherige Version installiert.
        /// </summary>
        public static bool ShowExtensionUpdatePrompt(Window? owner, PreparedExtension update)
        {
            var dlg = new ThemedDialogWindow
            {
                Owner = owner ?? Application.Current.MainWindow,
                txtDialogTitle = { Text = Tr.Get("Ext_UpdateConsentTitle") },
                txtHeadline = { Text = Tr.Format("Ext_UpdateConsentHeadline", update.Name) },
                txtMessage = { Text = Tr.Format("Ext_UpdateConsentMessage", update.Version ?? "?") },
                btnPrimary = { Content = Tr.Get("Ext_UpdateAllow") },
                btnSecondary = { Content = Tr.Get("Ext_UpdateLater"), Visibility = Visibility.Visible }
            };

            dlg.pathStatusIcon.Data = Geometry.Parse("M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z");
            dlg.pathStatusIcon.Fill = (Brush)dlg.FindResource("StatusWarningBrush");

            dlg.ShowPermissionDetails(update, update.StoreId != null ? Tr.Format("Ext_IdLabel", update.StoreId) : null);
            return dlg.ShowDialog() == true;
        }

        /// <summary>»Diese Erweiterung kann:« mit der Liste aus dem Manifest, darunter Version und Herkunft.</summary>
        private void ShowPermissionDetails(PreparedExtension extension, string? source)
        {
            var details = new System.Text.StringBuilder(ExtensionPermissions.Format(extension.Permissions));
            details.Append("\n\n");
            if (!string.IsNullOrWhiteSpace(extension.Version)) details.Append(Tr.Format("Ext_VersionLabel", extension.Version)).Append('\n');
            if (!string.IsNullOrWhiteSpace(source)) details.Append(source);

            borderDetails.Visibility = Visibility.Visible;
            txtDetailsLabel.Text = Tr.Get("Ext_Perm_Header");
            txtDetailsContent.Text = details.ToString().TrimEnd();
        }

        public static void ShowExtensionInstalledSuccess(Window? owner, string extensionName, string? extensionId = null)
        {
            var dlg = new ThemedDialogWindow
            {
                Owner = owner ?? Application.Current.MainWindow,
                txtDialogTitle = { Text = Tr.Get("Ext_DialogTitle") },
                txtHeadline = { Text = Tr.Get("Ext_AddedHeadline") },
                txtMessage = { Text = Tr.Format("Ext_AddedMessage", extensionName) },
                btnPrimary = { Content = Tr.Get("Common_Done") },
                btnSecondary = { Visibility = Visibility.Collapsed }
            };

            // Icon: Checkmark
            dlg.pathStatusIcon.Data = Geometry.Parse("M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z");
            dlg.pathStatusIcon.Fill = (Brush)dlg.FindResource("StatusSuccessBrush");

            if (!string.IsNullOrWhiteSpace(extensionId))
            {
                dlg.borderDetails.Visibility = Visibility.Visible;
                dlg.txtDetailsLabel.Text = "ID:";
                dlg.txtDetailsContent.Text = extensionId;
            }

            dlg.ShowDialog();
        }

        public static bool ShowExtensionRemovePrompt(Window? owner, string extensionName)
        {
            var dlg = new ThemedDialogWindow
            {
                Owner = owner ?? Application.Current.MainWindow,
                txtDialogTitle = { Text = Tr.Get("Ext_RemoveTitle") },
                txtHeadline = { Text = Tr.Format("Ext_RemoveHeadline", extensionName) },
                txtMessage = { Text = Tr.Get("Ext_RemoveWarning") },
                btnPrimary = { Content = Tr.Get("Common_Remove") },
                btnSecondary = { Content = Tr.Get("Dialog_Cancel"), Visibility = Visibility.Visible }
            };

            // Primary button danger style
            dlg.btnPrimary.Background = (Brush)dlg.FindResource("StatusDangerBrush");
            dlg.btnPrimary.Foreground = Brushes.White;

            // Icon: Trash / Warning
            dlg.pathStatusIcon.Data = Geometry.Parse("M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z");
            dlg.pathStatusIcon.Fill = (Brush)dlg.FindResource("StatusDangerBrush");

            return dlg.ShowDialog() == true;
        }

        public static bool ShowConfirm(Window? owner, string title, string message, string? confirmText = null, string? cancelText = null)
        {
            var dlg = new ThemedDialogWindow
            {
                Owner = owner ?? Application.Current.MainWindow,
                txtDialogTitle = { Text = title },
                txtHeadline = { Text = title },
                txtMessage = { Text = message },
                btnPrimary = { Content = confirmText ?? Tr.Get("Common_OK") },
                btnSecondary = { Content = cancelText ?? Tr.Get("Dialog_Cancel"), Visibility = Visibility.Visible }
            };

            dlg.pathStatusIcon.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z");
            dlg.pathStatusIcon.Fill = (Brush)dlg.FindResource("ShieldActiveBrush");

            return dlg.ShowDialog() == true;
        }

        public static void ShowMessage(Window? owner, string title, string message, MessageBoxImage image = MessageBoxImage.Information)
        {
            var dlg = new ThemedDialogWindow
            {
                Owner = owner ?? Application.Current.MainWindow,
                txtDialogTitle = { Text = "Echo" },
                txtHeadline = { Text = title },
                txtMessage = { Text = message },
                btnPrimary = { Content = Tr.Get("Common_OK") },
                btnSecondary = { Visibility = Visibility.Collapsed }
            };

            if (image == MessageBoxImage.Error)
            {
                dlg.pathStatusIcon.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z");
                dlg.pathStatusIcon.Fill = (Brush)dlg.FindResource("StatusDangerBrush");
            }
            else if (image == MessageBoxImage.Warning)
            {
                dlg.pathStatusIcon.Data = Geometry.Parse("M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z");
                dlg.pathStatusIcon.Fill = (Brush)dlg.FindResource("StatusWarningBrush");
            }
            else
            {
                dlg.pathStatusIcon.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z");
                dlg.pathStatusIcon.Fill = (Brush)dlg.FindResource("ShieldActiveBrush");
            }

            dlg.ShowDialog();
        }
    }
}
