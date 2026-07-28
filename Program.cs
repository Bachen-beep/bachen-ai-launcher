namespace BaChenAiLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 6 && args[0].Equals("--apply-update", StringComparison.OrdinalIgnoreCase))
        {
            return LauncherSelfUpdateService.ApplyUpdateAsync(args[1], args[2], int.Parse(args[3]), args[4], args[5]).GetAwaiter().GetResult();
        }
        if (args.Length >= 3 && args[0].Equals("--sign-update-manifest", StringComparison.OrdinalIgnoreCase))
        {
            LauncherUpdateManifestVerifier.SignFileAsync(args[1], args[2]).GetAwaiter().GetResult();
            return 0;
        }
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
