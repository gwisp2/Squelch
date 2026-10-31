using Squelch.Interop;
using Squelch.UI;

namespace Squelch.Core;

public enum EngineState
{
    Idle,
    Active,
}

/// <summary>
/// Wires the input hook to the microphone.
///
/// <para>
/// The separation matters: <see cref="InputHook"/> callbacks run inside Windows' low-level hook
/// timeout, so the engine only flips a flag there and hands the audio work to
/// <see cref="UiDispatcher"/>. The device is touched later, on the message loop.
/// </para>
/// </summary>
public sealed class SquelchEngine : IDisposable
{
    private readonly InputHook _hook = new();
    private readonly TriggerDetector _detector = new();
    private readonly MicrophoneController _microphone = new();
    private readonly UiDispatcher _dispatcher;

    private bool _capturing;
    private bool _started;
    private bool _ownsMicrophone;
    private bool _disposed;

    // Tracks trigger presses we swallowed, so the matching release is swallowed too even when the
    // required modifiers were let go first.
    private bool _suppressedKeyDown;
    private bool _suppressedMouseDown;

    public SquelchEngine(UiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;

        _hook.Key += OnRawKey;
        _hook.Mouse += OnRawMouse;
        _hook.ShouldSuppressKey = ShouldSuppressKey;
        _hook.ShouldSuppressMouse = ShouldSuppressMouse;
        _detector.Activated += OnTriggerPressed;
        _detector.Deactivated += OnTriggerReleased;
        _microphone.Error += message => _dispatcher.Post(() => DeviceError?.Invoke(message));
    }

    public AppSettings Settings { get; private set; } = new();

    public MicrophoneController Microphone => _microphone;

    public EngineState State { get; private set; } = EngineState.Idle;

    /// <summary>Raised on the UI thread whenever <see cref="State"/> changes.</summary>
    public event Action? StateChanged;

    /// <summary>Raised when Core Audio reports a failure (no microphone, device removed, ...).</summary>
    public event Action<string>? DeviceError;

    /// <summary>
    /// While true, raw input is forwarded to <see cref="CaptureKey"/>/<see cref="CaptureMouse"/>
    /// instead of being fed to the trigger detector. Used by the "set trigger" dialog.
    /// </summary>
    public bool IsCapturing
    {
        get => _capturing;
        set
        {
            if (_capturing == value)
                return;

            _capturing = value;
            _suppressedKeyDown = false;
            _suppressedMouseDown = false;

            if (!value)
                _detector.Reset(); // keys held during capture must not stay "down"
        }
    }

    public event Action<KeyInput>? CaptureKey;
    public event Action<MouseInput>? CaptureMouse;

    public void Start()
    {
        if (_started)
            return;

        _started = true;

        _detector.Trigger = Settings.Trigger;
        _microphone.DeviceRole = Settings.Device;
        _hook.Start();

        // Push-to-talk means the microphone is muted until the trigger is held. Enforce that
        // immediately instead of waiting for the first key press, otherwise audio passes through
        // untouched until then (and, with the old restore-on-release logic, forever).
        if (Settings.Enabled)
            EngageMicrophone();
    }

    public void ApplySettings(AppSettings settings)
    {
        Settings = settings;

        _detector.Trigger = settings.Trigger;   // setter resets held-key state
        _microphone.DeviceRole = settings.Device;

        _suppressedKeyDown = false;
        _suppressedMouseDown = false;

        if (!_started)
            return;

        if (settings.Enabled)
            EngageMicrophone();
        else
            DisengageMicrophone();
    }

    /// <summary>
    /// Takes ownership of the endpoint's mute switch: remembers what the user had and applies the
    /// idle state right away. The saved state is restored on exit or when the app is disabled.
    /// </summary>
    private void EngageMicrophone()
    {
        if (!_ownsMicrophone)
        {
            if (!_microphone.SaveCurrentState())
                return; // Could not read the endpoint; MicrophoneController already reported why.

            _ownsMicrophone = true;
        }

        ApplyCurrentMuteState();
    }

    private void DisengageMicrophone()
    {
        var wasActive = State == EngineState.Active;
        State = EngineState.Idle;

        if (_ownsMicrophone)
        {
            _microphone.RestoreSavedState();
            _ownsMicrophone = false;
        }

        if (wasActive)
            StateChanged?.Invoke();
    }

    /// <summary>
    /// "Unmute while active" (push to talk) means mute while idle, and vice versa for push to mute.
    /// </summary>
    private bool ShouldMute(EngineState state) => state == EngineState.Active
        ? Settings.Action == MicAction.MuteWhileActive
        : Settings.Action == MicAction.UnmuteWhileActive;

    private void ApplyCurrentMuteState() => _microphone.SetMuted(ShouldMute(State));

    // -- Input suppression (runs inside the hook callback) ------------------------------------

    private bool ShouldSuppressKey(KeyInput input)
    {
        if (!Settings.Enabled || _capturing)
            return false;

        var trigger = Settings.Trigger;
        if (trigger.Kind != TriggerKind.Keyboard || input.VirtualKey != trigger.Code)
            return false;

        if (Settings.IgnoreSimulatedInput && input.IsInjected)
            return false;

        if (!input.IsDown)
        {
            var suppress = _suppressedKeyDown;
            _suppressedKeyDown = false;
            return suppress;
        }

        // Leave plain presses of the key alone when the required modifiers are not held: for a
        // Ctrl+J trigger, typing a bare J must still work. Once the combo is engaged, auto-repeat
        // is swallowed as well.
        if (!_detector.IsActive && !_detector.AreModifiersSatisfied)
            return false;

        _suppressedKeyDown = true;
        return true;
    }

    private bool ShouldSuppressMouse(MouseInput input)
    {
        if (!Settings.Enabled || _capturing)
            return false;

        var trigger = Settings.Trigger;
        if (trigger.Kind != TriggerKind.Mouse || (int)input.Button != trigger.Code)
            return false;

        if (Settings.IgnoreSimulatedInput && input.IsInjected)
            return false;

        if (!input.IsDown)
        {
            var suppress = _suppressedMouseDown;
            _suppressedMouseDown = false;
            return suppress;
        }

        if (!_detector.IsActive && !_detector.AreModifiersSatisfied)
            return false;

        _suppressedMouseDown = true;
        return true;
    }

    private void OnRawKey(KeyInput input)
    {
        if (_capturing)
        {
            CaptureKey?.Invoke(input);
            return;
        }

        if (Settings.IgnoreSimulatedInput && input.IsInjected)
            return;

        _detector.HandleKey(input);
    }

    private void OnRawMouse(MouseInput input)
    {
        if (_capturing)
        {
            CaptureMouse?.Invoke(input);
            return;
        }

        if (Settings.IgnoreSimulatedInput && input.IsInjected)
            return;

        _detector.HandleMouse(input);
    }

    // -- Trigger edges (still on the hook thread) --------------------------------------------

    private void OnTriggerPressed()
    {
        if (!Settings.Enabled)
            return;

        if (Settings.Mode == TriggerMode.Toggle && State == EngineState.Active)
            ReturnToIdle();
        else
            GoActive();
    }

    private void OnTriggerReleased()
    {
        if (Settings.Mode != TriggerMode.Hold)
            return;

        ReturnToIdle();
    }

    // The flags move immediately so that rapid press/release sequences stay ordered; only the
    // audio work is deferred.

    private void GoActive()
    {
        if (!Settings.Enabled || State == EngineState.Active)
            return;

        State = EngineState.Active;
        var muted = ShouldMute(EngineState.Active);

        _dispatcher.Post(() =>
        {
            // Guard against a release, disable or settings change that happened while this was
            // queued: the state flags always move before the audio work is posted.
            if (!Settings.Enabled || State != EngineState.Active)
                return;

            _microphone.SetMuted(muted);
            StateChanged?.Invoke();
        });
    }

    private void ReturnToIdle()
    {
        if (State == EngineState.Idle)
            return;

        State = EngineState.Idle;
        var muted = ShouldMute(EngineState.Idle);

        _dispatcher.Post(() =>
        {
            // When the app was disabled in the meantime, DisengageMicrophone has already restored
            // the user's original state; don't override it.
            if (!Settings.Enabled || State != EngineState.Idle)
                return;

            _microphone.SetMuted(muted);
            StateChanged?.Invoke();
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Never leave the user's microphone in a state we created.
        if (_ownsMicrophone)
        {
            _microphone.RestoreSavedState();
            _ownsMicrophone = false;
        }

        _hook.Dispose();
        _microphone.Dispose();
    }
}
