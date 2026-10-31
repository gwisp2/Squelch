using Squelch.Core;
using Squelch.Interop;

namespace Squelch.UI;

/// <summary>
/// Modal "press the combination you want" dialog. It does not install a second hook; the engine is
/// switched into capture mode so the events flow from the existing hook to this form.
/// </summary>
public sealed class TriggerCaptureForm : Form
{
    // A mouse-down that arrives immediately after the dialog opens is almost certainly the click
    // that brought the dialog to the foreground, not an intentional choice.
    private const int MouseArmDelayMs = 400;

    private readonly SquelchEngine _engine;
    private readonly Label _preview = new();

    // Fully qualified: "Timer" is ambiguous between System.Threading and System.Windows.Forms
    // under ImplicitUsings.
    private readonly System.Windows.Forms.Timer _armTimer = new() { Interval = MouseArmDelayMs };

    private HotkeyModifiers _heldModifiers = HotkeyModifiers.None;
    private bool _mouseArmed;
    private TriggerConfig? _captured;

    public TriggerCaptureForm(SquelchEngine engine, TriggerConfig current)
    {
        _engine = engine;

        Text = "Set trigger";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = SystemFonts.MessageBoxFont;

        var heading = new Label
        {
            Text = "Press the key or mouse button you want to use.",
            AutoSize = true,
            MaximumSize = new Size(360, 0),
        };

        var hint = new Label
        {
            Text = $"Modifiers held at that moment are included, e.g. Ctrl + {TriggerConfig.MouseButtonName((int)MouseButtonKind.X1)}.\n" +
                   $"Currently set to: {current.Describe()}\n" +
                   "Left mouse cannot be used as a trigger. Esc cancels.",
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 8, 0, 8),
        };

        _preview.Text = "Waiting for input...";
        _preview.AutoSize = true;
        _preview.Font = new Font(Font.FontFamily, Font.Size + 1.5f, FontStyle.Bold);
        _preview.Margin = new Padding(0, 4, 0, 12);

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(88, 28),
        };
        CancelButton = cancelButton;
        _armTimer.Tick += (_, _) => { _armTimer.Stop(); _mouseArmed = true; };

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        root.Controls.Add(heading);
        root.Controls.Add(_preview);
        root.Controls.Add(hint);
        root.Controls.Add(cancelButton);
        Controls.Add(root);
    }

    /// <summary>Returns the captured trigger, or null if the user cancelled.</summary>
    public static TriggerConfig? CaptureTrigger(IWin32Window owner, SquelchEngine engine, TriggerConfig current)
    {
        using var form = new TriggerCaptureForm(engine, current);
        form.ShowDialog(owner);
        return form._captured;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        _engine.CaptureKey += OnCapturedKey;
        _engine.CaptureMouse += OnCapturedMouse;
        _engine.IsCapturing = true;

        _mouseArmed = false;
        _armTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _armTimer.Stop();
        _engine.IsCapturing = false;
        _engine.CaptureKey -= OnCapturedKey;
        _engine.CaptureMouse -= OnCapturedMouse;

        base.OnFormClosed(e);
    }

    private void OnCapturedKey(KeyInput input)
    {
        if (!input.IsDown)
        {
            UpdateModifiers(input);
            return;
        }

        if (input.VirtualKey == 0x1B) // Esc
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (InputHook.IsModifierKey(input.VirtualKey))
        {
            UpdateModifiers(input);
            return;
        }

        Complete(new TriggerConfig
        {
            Kind = TriggerKind.Keyboard,
            Code = input.VirtualKey,
            Modifiers = _heldModifiers,
        });
    }

    private void OnCapturedMouse(MouseInput input)
    {
        if (!input.IsDown || !_mouseArmed)
            return;

        // The trigger is swallowed by the hook, so a left-click trigger would make the desktop
        // impossible to use. Reject it here and leave the dialog open for another choice.
        if (input.Button == MouseButtonKind.Left)
        {
            _preview.Text = "Left mouse cannot be used - choose another button or key.";
            return;
        }

        Complete(new TriggerConfig
        {
            Kind = TriggerKind.Mouse,
            Code = (int)input.Button,
            Modifiers = _heldModifiers,
        });
    }

    private void UpdateModifiers(KeyInput input)
    {
        var flag = input.VirtualKey switch
        {
            0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,
            0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Control,
            0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,
            0x5B or 0x5C => HotkeyModifiers.Win,
            _ => HotkeyModifiers.None,
        };

        if (flag == HotkeyModifiers.None)
            return;

        if (input.IsDown)
            _heldModifiers |= flag;
        else
            _heldModifiers &= ~flag;

        _preview.Text = _heldModifiers == HotkeyModifiers.None
            ? "Waiting for input..."
            : TriggerConfig.DescribeModifiers(_heldModifiers) + " + ...";
    }

    private void Complete(TriggerConfig trigger)
    {
        _captured = trigger;
        DialogResult = DialogResult.OK;
        Close();
    }
}
