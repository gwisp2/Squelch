using Squelch.Core;

namespace Squelch.UI;

/// <summary>
/// The small settings window. Built entirely in code - no designer files, no .resx, which keeps the
/// whole UI reviewable in a diff.
/// </summary>
public sealed class SettingsForm : Form
{
    private static readonly Lazy<Icon> AppIcon = new(LoadAppIcon);

    private readonly AppSettings _working;
    private readonly SquelchEngine _engine;

    private readonly Button _triggerButton = new();
    private readonly ComboBox _modeCombo = new();
    private readonly ComboBox _actionCombo = new();
    private readonly ComboBox _deviceCombo = new();
    private readonly CheckBox _ignoreSimulatedCheck = new();
    private readonly CheckBox _autoStartCheck = new();

    public SettingsForm(AppSettings current, SquelchEngine engine)
    {
        _working = current.Clone();
        _engine = engine;

        Text = "Squelch - Settings";
        Icon = AppIcon.Value;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        Font = SystemFonts.MessageBoxFont;

        var grid = BuildOptionGrid();
        var buttons = BuildButtonRow();

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        root.Controls.Add(grid, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
    }

    /// <summary>The edited settings. Only meaningful when the dialog returned OK.</summary>
    public AppSettings Result => _working;

    private static Icon LoadAppIcon()
    {
        var assembly = typeof(SettingsForm).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(".squelch.ico", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded application icon 'squelch.ico' is missing.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        return new Icon(stream);
    }

    private TableLayoutPanel BuildOptionGrid()
    {
        int row = 0;

        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void AddRow(string label, Control control)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(
                new Label
                {
                    Text = label,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 7, 12, 0),
                },
                0,
                row);
            grid.Controls.Add(control, 1, row);
            row++;
        }

        void AddFullWidth(Control control)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(control, 0, row);
            grid.SetColumnSpan(control, 2);
            row++;
        }

        _triggerButton.AutoSize = false;
        _triggerButton.Size = new Size(240, 28);
        _triggerButton.TextAlign = ContentAlignment.MiddleLeft;
        _triggerButton.Margin = new Padding(0, 2, 0, 2);
        _triggerButton.Click += (_, _) => EditTrigger();
        UpdateTriggerButtonText();

        _modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeCombo.Width = 240;
        _modeCombo.Margin = new Padding(0, 2, 0, 2);
        _modeCombo.Items.AddRange(
        [
            "Hold - active while the trigger is held",
            "Toggle - press to switch on or off",
        ]);
        _modeCombo.SelectedIndex = (int)_working.Mode;

        _actionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _actionCombo.Width = 240;
        _actionCombo.Margin = new Padding(0, 2, 0, 2);
        _actionCombo.Items.AddRange(
        [
            "Unmute while active (push to talk)",
            "Mute while active (push to mute)",
        ]);
        _actionCombo.SelectedIndex = (int)_working.Action;

        _deviceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _deviceCombo.Width = 240;
        _deviceCombo.Margin = new Padding(0, 2, 0, 2);
        _deviceCombo.Items.AddRange(
        [
            "Default communications device",
            "Default multimedia device",
            "Default console device",
        ]);
        _deviceCombo.SelectedIndex = (int)_working.Device;

        _ignoreSimulatedCheck.Text = "Ignore input sent by other programs (macros, remote desktop)";
        _ignoreSimulatedCheck.AutoSize = true;
        _ignoreSimulatedCheck.Checked = _working.IgnoreSimulatedInput;
        _ignoreSimulatedCheck.Margin = new Padding(0, 10, 0, 0);

        _autoStartCheck.Text = "Start with Windows";
        _autoStartCheck.AutoSize = true;
        _autoStartCheck.Checked = _working.StartWithWindows;

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 14, 0, 0),
            Text = "The trigger key or button is swallowed while Squelch is enabled, so it never " +
                   "reaches the focused window; modifier keys still work normally. Left mouse " +
                   "cannot be used as a trigger. Disable Squelch from the tray menu to let the " +
                   "trigger through. A non-elevated app cannot see keystrokes aimed at elevated " +
                   "windows, and some anti-cheat systems block low-level hooks.",
        };

        AddRow("Trigger:", _triggerButton);
        AddRow("Mode:", _modeCombo);
        AddRow("Behaviour:", _actionCombo);
        AddRow("Device:", _deviceCombo);
        AddFullWidth(_ignoreSimulatedCheck);
        AddFullWidth(_autoStartCheck);
        AddFullWidth(note);

        return grid;
    }

    private FlowLayoutPanel BuildButtonRow()
    {
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(88, 28),
        };
        okButton.Click += (_, _) => Commit();

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(88, 28),
        };

        AcceptButton = okButton;
        CancelButton = cancelButton;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 12, 0, 0),
        };

        buttons.Controls.Add(okButton);      // right-most, because of RightToLeft
        buttons.Controls.Add(cancelButton);

        return buttons;
    }

    private void EditTrigger()
    {
        var captured = TriggerCaptureForm.CaptureTrigger(this, _engine, _working.Trigger);
        if (captured is null)
            return;

        _working.Trigger = captured;
        UpdateTriggerButtonText();
    }

    private void UpdateTriggerButtonText() => _triggerButton.Text = _working.Trigger.Describe();

    private void Commit()
    {
        _working.Mode = (TriggerMode)Math.Max(0, _modeCombo.SelectedIndex);
        _working.Action = (MicAction)Math.Max(0, _actionCombo.SelectedIndex);
        _working.Device = (CaptureDeviceRole)Math.Max(0, _deviceCombo.SelectedIndex);
        _working.IgnoreSimulatedInput = _ignoreSimulatedCheck.Checked;
        _working.StartWithWindows = _autoStartCheck.Checked;
    }
}
