using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using NotchWin.UI;

namespace NotchWin;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, "Local\\Pill_SingleInstance_v1", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        MigrateLegacyAppData();

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => LogCrash(ex.ExceptionObject?.ToString() ?? "Unknown");
        base.OnStartup(e);

        try
        {
            var notch = new NotchWindow();
            MainWindow = notch;
            notch.Show();
        }
        catch (Exception ex)
        {
            LogCrash(ex.ToString());
            Shutdown(1);
        }
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception.ToString());
        e.Handled = true;
    }

    private static void MigrateLegacyAppData()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string legacyDir = Path.Combine(appData, "NotchWin");
            string newDir = Path.Combine(appData, "Pill");
            if (Directory.Exists(legacyDir) && !Directory.Exists(newDir))
            {
                Directory.CreateDirectory(newDir);
                foreach (string file in Directory.GetFiles(legacyDir, "*.json"))
                    File.Copy(file, Path.Combine(newDir, Path.GetFileName(file)), false);
            }
        }
        catch { }
    }

    private static void LogCrash(string text)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pill");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.Now:O}] {text}\n\n");
        }
        catch { }
        if (!NotchWin.Services.StartupService.IsPackaged())
        {
            try
            {
                string local = Path.Combine(AppContext.BaseDirectory, "crash.log");
                File.AppendAllText(local, $"[{DateTime.Now:O}] {text}\n\n");
            }
            catch { }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
