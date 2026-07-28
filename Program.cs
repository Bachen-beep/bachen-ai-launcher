namespace BaChenAiLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0].Equals("--self-test", StringComparison.OrdinalIgnoreCase))
        {
            return LauncherSelfTests.RunAsync(args[1]).GetAwaiter().GetResult();
        }
        if (args.Length >= 3 && args[0].Equals("--canonicalize-manifest", StringComparison.OrdinalIgnoreCase))
        {
            return LauncherSelfTests.WriteCanonicalManifestPayloadAsync(args[1], args[2]).GetAwaiter().GetResult();
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherForm());
        return 0;
    }
}
