using System.Text.Json;
using System.Text.Json.Serialization;

namespace Squelch;

public enum TriggerMode
{
    /// <summary>Active while the trigger is held. Classic push-to-talk.</summary>
    Hold = 0,

    /// <summary>Each trigger press flips between active and idle.</summary>
    Toggle = 1,
}

public enum MicAction
{
    /// <summary>Unmute while active (push to talk).</summary>
    UnmuteWhileActive = 0,

    /// <summary>Mute while active (push to mute).</summary>
    MuteWhileActive = 1,
}

public sealed class AppSettings
{
    private Core.TriggerConfig _trigger = new();

    /// <summary>
    /// The setter normalises unusable triggers (left mouse) to the default, so a hand-edited
    /// settings file or an older configuration cannot make the computer unclickable.
    /// </summary>
    public Core.TriggerConfig Trigger
    {
        get => _trigger;
        set => _trigger = value is { IsUsable: true } ? value : new Core.TriggerConfig();
    }

    public TriggerMode Mode { get; set; } = TriggerMode.Hold;

    public MicAction Action { get; set; } = MicAction.UnmuteWhileActive;

    public Core.CaptureDeviceRole Device { get; set; } = Core.CaptureDeviceRole.Communications;

    /// <summary>
    /// Ignore input that another program injected (SendInput, AutoHotkey, RDP, macro tools).
    /// Keeps synthetic keystrokes from re-triggering us.
    /// </summary>
    public bool IgnoreSimulatedInput { get; set; } = true;

    public bool StartWithWindows { get; set; }

    /// <summary>Set to false from the tray menu to disable triggering without exiting.</summary>
    public bool Enabled { get; set; } = true;

    public AppSettings Clone() => new()
    {
        Trigger = Trigger.Clone(),
        Mode = Mode,
        Action = Action,
        Device = Device,
        IgnoreSimulatedInput = IgnoreSimulatedInput,
        StartWithWindows = StartWithWindows,
        Enabled = Enabled,
    };
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}

public static class SettingsStore
{
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Squelch");

    public static string FilePath { get; } = Path.Combine(Directory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)
                   ?? new AppSettings();
        }
        catch
        {
            // A corrupt or unreadable config must never stop the app from starting.
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Best effort: losing the config is not worth crashing over.
        }
    }
}
