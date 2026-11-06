using System.IO;
using System.Runtime.CompilerServices;
using EchoBrowser.Services;

namespace EchoBrowser.Tests;

/// <summary>
/// Tests laufen nie gegen die echten Daten unter %LOCALAPPDATA%\EchoBrowserData: Vor dem ersten Zugriff auf
/// <see cref="AppPaths"/> zeigt ECHO_USER_DATA_DIR auf einen eigenen, leeren Ordner (Dienste wie
/// BookmarkService.Instance lesen und schreiben sonst die Lesezeichen des Nutzers).
/// </summary>
internal static class TestDataFolder
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        string folder = Path.Combine(Path.GetTempPath(), "echo-tests-" + Environment.ProcessId);
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable(AppPaths.DataFolderVariable, folder);
    }
}
