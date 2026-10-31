using Squelch.Core;

namespace Squelch.UI;

/// <summary>
/// The app itself: a tray icon plus a live <see cref="SquelchEngine"/>. There is no main window, which
/// is why this uses ApplicationContext instead of a Form.
/// </summary>
public sealed class SquelchApplicationContext : ApplicationContext
{
    private readonly UiDispatcher _dispatcher = new();
    private readonly TrayIcons _icons = new();
    private readonly SquelchEngine _engine;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _statusItem;

    private AppSettings _settings;
    private SettingsForm? _openSettingsForm;

    public SquelchApplicationContext()
    {
        _settings = SettingsStore.Load();
        _engine = new SquelchEngine(_dispatcher);

        _statusItem = new ToolStripMenuItem { Enabled = false };
        _enabledItem = new ToolStripMenuItem("Enabled") { CheckOnClick = true, Checked = _settings.Enabled };
        _enabledItem.CheckedChanged += (_, _) => SetEnabled(_enabledItem.Checked);

        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => ShowSettings();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enabledItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _tray = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _icons.Disabled,
            Visible = true,
            Text = "Squelch",
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        try
        {
            _engine.ApplySettings(_settings);
            _engine.Start();
            _engine.StateChanged += UpdateTray;

            // Keep the Run-key entry pointing at wherever the exe actually lives now.
            AutoStart.Set(_settings.StartWithWindows);

            UpdateTray();
        }
        catch (Exception ex)
        {
            _tray.Visible = false;
            MessageBox.Show(
                "Squelch could not install its low-level input hooks, so it cannot run.\n\n" + ex.Message,
                "Squelch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            ExitThread();
        }
    }

    private void UpdateTray()
    {
        if (!_settings.Enabled)
        {
            _tray.Icon = _icons.Disabled;
            _statusItem.Text = "Disabled";
        }
        else if (_engine.State == EngineState.Active)
        {
            _tray.Icon = _icons.Active;
            _statusItem.Text = _settings.Action == MicAction.MuteWhileActive ? "Muting" : "Talking";
        }
        else
        {
            _tray.Icon = _icons.Idle;
            _statusItem.Text = _settings.Action == MicAction.UnmuteWhileActive ? "Muted" : "Live";
        }

        var hold = _settings.Mode == TriggerMode.Hold ? "hold" : "press";
        _tray.Text = Truncate($"Squelch - {_statusItem.Text} ({hold} {_settings.Trigger.Describe()})", 63);
    }

    private void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        SettingsStore.Save(_settings);
        _engine.ApplySettings(_settings);

        if (_enabledItem.Checked != enabled)
            _enabledItem.Checked = enabled;

        UpdateTray();
    }

    private void ShowSettings()
    {
        if (_openSettingsForm is { Visible: true })
        {
            _openSettingsForm.Activate();
            return;
        }

        using var form = new SettingsForm(_settings, _engine);
        _openSettingsForm = form;

        try
        {
            if (form.ShowDialog() == DialogResult.OK)
                ApplySettings(form.Result);
        }
        finally
        {
            _openSettingsForm = null;
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        var autoStartChanged = settings.StartWithWindows != _settings.StartWithWindows;

        _settings = settings;
        SettingsStore.Save(_settings);
        _engine.ApplySettings(_settings);

        if (autoStartChanged)
            AutoStart.Set(_settings.StartWithWindows);

        _enabledItem.Checked = _settings.Enabled;
        UpdateTray();
    }

    private void ExitApplication()
    {
        _tray.Visible = false;
        ExitThread();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "\u2026";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _engine.StateChanged -= UpdateTray;
            _engine.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _icons.Dispose();
            _dispatcher.Dispose();
        }

        base.Dispose(disposing);
    }
}
