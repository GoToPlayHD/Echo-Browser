using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Sorgt dafür, dass pro Benutzer nur ein Echo-Prozess läuft (alle teilen sich die Daten unter %LOCALAPPDATA%).
    /// Ein zweiter Start schickt seine Adressen per Named Pipe an die laufende Instanz und beendet sich.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        // Ein eigener Datenordner (ECHO_USER_DATA_DIR) ist eine eigene Instanz – wie Chrome-Profile mit --user-data-dir
        private static readonly string Name = "EchoBrowser-" + SanitizeForName(Environment.UserName)
            + (AppPaths.IsCustomDataFolder ? "-" + ShortHash(AppPaths.DataFolder.ToLowerInvariant()) : "");
        private static string PipeName => Name + "-pipe";

        private readonly Mutex _mutex;
        private readonly CancellationTokenSource _cts = new();

        public bool IsPrimary { get; }

        private SingleInstance(Mutex mutex, bool isPrimary)
        {
            _mutex = mutex;
            IsPrimary = isPrimary;
        }

        public static SingleInstance Acquire()
        {
            var mutex = new Mutex(initiallyOwned: true, @"Local\" + Name, out bool createdNew);
            return new SingleInstance(mutex, createdNew);
        }

        /// <summary>Adressen an die laufende Instanz schicken. Eine leere Liste bedeutet "neues Fenster öffnen".</summary>
        public static bool TrySendToPrimary(IReadOnlyList<string> urls)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(3000);
                using var writer = new StreamWriter(client);
                writer.Write(JsonSerializer.Serialize(urls));
                writer.Flush();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Laufende Echo-Instanz nicht erreichbar", ex);
                return false;
            }
        }

        /// <summary>Wartet im Hintergrund auf Nachrichten weiterer Starts. Der Callback läuft NICHT auf dem UI-Thread.</summary>
        public void StartListening(Action<List<string>> onUrlsReceived)
        {
            if (!IsPrimary) return;

            _ = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        await using var server = new NamedPipeServerStream(
                            PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                        await server.WaitForConnectionAsync(_cts.Token);
                        using var reader = new StreamReader(server);
                        string json = await reader.ReadToEndAsync(_cts.Token);
                        var urls = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                        onUrlsReceived(CommandLineParser.GetUrls(urls));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Fehler beim Empfangen einer Nachricht einer weiteren Echo-Instanz", ex);
                    }
                }
            });
        }

        private static string SanitizeForName(string value)
        {
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            }
            return new string(chars);
        }

        private static string ShortHash(string value) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..8];

        public void Dispose()
        {
            _cts.Cancel();
            if (IsPrimary)
            {
                try { _mutex.ReleaseMutex(); } catch { }
            }
            _mutex.Dispose();
        }
    }
}
