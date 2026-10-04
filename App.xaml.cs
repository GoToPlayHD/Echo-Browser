using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using EchoBrowser.Services;
using EchoBrowser.Views;
using Velopack;

namespace EchoBrowser;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan ErrorDialogInterval = TimeSpan.FromSeconds(30);
    private DateTime _lastErrorDialog = DateTime.MinValue;
    private bool _isShowingErrorDialog;

    [STAThread]
    public static void Main(string[] args)
    {
        // 1. Velopack-Lebenszyklus ganz am Anfang ausführen, noch vor dem Laden des Hauptfensters.
        //    Bei Installation/Update meldet sich Echo bei Windows als Browser an (Standard-Apps), bei Deinstallation ab.
        VelopackApp.Build()
            .OnAfterInstallFastCallback(v => DefaultBrowserService.Register(DefaultBrowserService.ExecutablePath))
            .OnAfterUpdateFastCallback(v => DefaultBrowserService.Register(DefaultBrowserService.ExecutablePath))
            .OnBeforeUninstallFastCallback(v => DefaultBrowserService.Unregister())
            .Run();

        // 2. Nur eine Echo-Instanz pro Benutzer: weitere Starts reichen ihre Adressen weiter
        var startupUrls = CommandLineParser.GetUrls(args);
        using var instance = SingleInstance.Acquire();
        if (!instance.IsPrimary && SingleInstance.TrySendToPrimary(startupUrls))
        {
            return;
        }

        if (args.Any(a => string.Equals(a, "--restored-after-update", StringComparison.OrdinalIgnoreCase)))
        {
            SessionService.Instance.IsRestoredAfterUpdate = true;
        }

        // 3. WPF-Anwendung initialisieren und Hauptfenster starten
        var app = new App();
        app.InitializeComponent();
        app.RegisterGlobalExceptionHandlers();

        SessionService.Instance.SetCollector(() => app.Windows.OfType<MainWindow>()
            .Where(w => w.IsPartOfSession)
            .Select(w => w.CaptureSession()));
        SessionService.Instance.MarkRunning();
        app.Exit += (s, e) =>
        {
            SessionService.Instance.MarkCleanExit();
            UpdateService.Instance.ApplyPendingUpdateOnExit();
        };

        instance.StartListening(urls => app.Dispatcher.BeginInvoke(() => app.OpenFromOtherInstance(urls)));

        app.Run(new MainWindow { IsInitialWindow = true, StartupUrls = startupUrls });
    }

    /// <summary>
    /// Ein weiterer Start (z.B. Klick auf einen Link in einer anderen App): Adressen als Tabs im zuletzt
    /// benutzten Fenster öffnen. Ohne Adressen wird – wie bei Chrome – ein neues Fenster geöffnet.
    /// </summary>
    private void OpenFromOtherInstance(List<string> urls)
    {
        var target = global::EchoBrowser.MainWindow.LastActive is { IsIncognito: false, IsLoaded: true } last
            ? last
            : Windows.OfType<MainWindow>().LastOrDefault(w => !w.IsIncognito && w.IsLoaded);

        if (urls.Count == 0 || target == null)
        {
            var window = new MainWindow { StartupUrls = urls };
            window.Show();
            window.Activate();
            return;
        }

        foreach (string url in urls)
        {
            target.AddNewTab(url);
        }
        target.BringToFront();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        // Fehler in Ereignis-Handlern sollen nicht den ganzen Browser (mit allen Tabs) beenden
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Log.Error("Schwerer Fehler, Echo wird beendet", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log.Warn("Unbeobachteter Fehler in einer Hintergrundaufgabe", e.Exception);
            e.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unbehandelter Fehler", e.Exception);
        e.Handled = true;

        // Höchstens ein Hinweis alle 30 Sekunden, und nie mehrere übereinander
        if (_isShowingErrorDialog || DateTime.Now - _lastErrorDialog < ErrorDialogInterval) return;

        _isShowingErrorDialog = true;
        _lastErrorDialog = DateTime.Now;
        try
        {
            ThemedDialogWindow.ShowMessage(
                global::EchoBrowser.MainWindow.LastActive,
                Tr.Get("Error_UnexpectedTitle"),
                Tr.Format("Error_UnexpectedMessage", e.Exception.Message, AppPaths.LogFolder),
                MessageBoxImage.Error);
        }
        catch (Exception dialogError)
        {
            Log.Error("Fehlerhinweis konnte nicht angezeigt werden", dialogError);
        }
        finally
        {
            _isShowingErrorDialog = false;
        }
    }
}
