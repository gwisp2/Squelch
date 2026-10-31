using Squelch.UI;

namespace Squelch;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\Squelch.SingleInstance";

    [STAThread]
    private static int Main()
    {
        // Tray utilities are single-instance: two copies would fight over the microphone and the
        // shell would show two icons.
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
            return 0;

        // Generated from the ApplicationHighDpiMode / ApplicationVisualStyles MSBuild properties.
        // Must run before any window or handle exists.
        ApplicationConfiguration.Initialize();

        using var context = new SquelchApplicationContext();
        Application.Run(context);
        return 0;
    }
}
