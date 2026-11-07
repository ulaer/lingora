using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Lingora.Core;
using Lingora.Core.Gemini;
using Lingora.Core.Storage;

namespace Lingora.Desktop;

public partial class App : Application
{
    private HttpClient? _http;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var paths = AppPaths.Default();
        _http = GeminiClient.CreateHttpClient();
        var gemini = new GeminiClient(_http);

        PracticeSession session;
        try
        {
            session = new PracticeSession(paths, gemini);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"Veri klasörü açılamadı:\n{paths.DataDirectory}\n\n{ex.Message}",
                "Lingora",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow(session, gemini);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _http?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Beklenmeyen bir hata oluştu:\n\n{e.Exception.Message}",
            "Lingora",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }
}
