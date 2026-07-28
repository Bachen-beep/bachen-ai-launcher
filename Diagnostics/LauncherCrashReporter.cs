using System.Text;

namespace BaChenAiLauncher;

internal static class LauncherCrashReporter
{
    public static void Initialize()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => Write(eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) => Write(eventArgs.ExceptionObject as Exception ?? new Exception(eventArgs.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Write(eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }

    private static void Write(Exception exception)
    {
        try
        {
            var directory = Path.Combine(LauncherPaths.UserConfigDirectory, "logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "crash.log");
            if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
            {
                File.Move(path, Path.Combine(directory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log"), true);
            }
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}]\n{exception}\n\n", Encoding.UTF8);
        }
        catch { }
    }
}
