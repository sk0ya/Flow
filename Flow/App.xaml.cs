using System;
using System.Windows;

namespace Flow;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var startupProjectPath = GetStartupProjectPath(e.Args);
        var mainWindow = new MainWindow(startupProjectPath);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static string? GetStartupProjectPath(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (string.Equals(arg, "--project", StringComparison.OrdinalIgnoreCase)
             || string.Equals(arg, "-p", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                    return args[i + 1];

                return null;
            }

            if (!string.IsNullOrWhiteSpace(arg))
                return arg;
        }

        return null;
    }
}
