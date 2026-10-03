using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EchoBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser.Views.Popups
{
    /// <summary>Abfrage einer Website-Berechtigung. Die Entscheidung verarbeitet MainWindow.Permissions.cs.</summary>
    public partial class PermissionPrompt : UserControl
    {
        public event Action? Allowed;
        public event Action? Blocked;
        public event Action? Dismissed;

        public PermissionPrompt()
        {
            InitializeComponent();
        }

        public void Show(string host, CoreWebView2PermissionKind kind)
        {
            txtTitle.Text = Tr.Format("Perm_Title", host);
            txtRequest.Text = SitePermissionTexts.Request(kind);
            pathIcon.Data = Geometry.Parse(SitePermissionTexts.IconPath(kind));
        }

        private void BtnAllow_Click(object sender, RoutedEventArgs e) => Allowed?.Invoke();

        private void BtnBlock_Click(object sender, RoutedEventArgs e) => Blocked?.Invoke();

        private void BtnDismiss_Click(object sender, RoutedEventArgs e) => Dismissed?.Invoke();
    }
}
