using System;
using System.Windows;
using Velopack;

namespace EchoBrowser;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 1. Velopack-Lebenszyklus ganz am Anfang ausführen, noch vor dem Laden des Hauptfensters
        VelopackApp.Build().Run();

        // 2. WPF-Anwendung initialisieren und Hauptfenster starten
        var app = new App();
        app.InitializeComponent();
        app.Run(new MainWindow());
    }
}


