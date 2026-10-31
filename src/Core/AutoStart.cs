using Microsoft.Win32;

namespace Squelch.Core;

/// <summary>Per-user auto-start via the HKCU Run key (no admin rights required).</summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Squelch";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string { Length: > 0 };
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        // Environment.ProcessPath (not Assembly.Location) so this keeps working for
        // single-file and self-contained publishes.
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
            return;

        key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
    }
}
