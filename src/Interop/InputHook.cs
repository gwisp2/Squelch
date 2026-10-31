using System.ComponentModel;
using System.Runtime.InteropServices;
using static Squelch.Interop.NativeMethods;

namespace Squelch.Interop;

public enum MouseButtonKind
{
    Left = 0,
    Right = 1,
    Middle = 2,
    X1 = 3,
    X2 = 4,
}

public readonly record struct KeyInput(int VirtualKey, bool IsDown, bool IsInjected);

public readonly record struct MouseInput(MouseButtonKind Button, bool IsDown, bool IsInjected);

/// <summary>
/// A global (system-wide) low-level keyboard and mouse hook.
///
/// <para>
/// <see cref="WH_KEYBOARD_LL"/> / <see cref="WH_MOUSE_LL"/> are special: unlike the classic
/// <c>WH_KEYBOARD</c>/<c>WH_MOUSE</c> hooks they do <b>not</b> require a DLL injected into every
/// process. Windows marshals the event to the thread that installed the hook, so everything stays
/// inside our own process.
/// </para>
///
/// <para>
/// Consequences you cannot opt out of:
/// </para>
/// <list type="bullet">
///   <item>The hooks must be installed from a thread that pumps messages, and callbacks are
///   delivered on that same thread.</item>
///   <item>Windows enforces a <c>LowLevelHooksTimeout</c> (~300 ms by default since Windows 10).
///   A callback that blocks longer than that causes the OS to <b>silently remove the hook</b>.
///   So the handlers below must never touch the audio device, the disk or the registry. They only
///   publish an event; the work happens later on the message loop.</item>
///   <item>Input aimed at an elevated window is invisible to a non-elevated hook (UIPI). See
///   app.manifest.</item>
/// </list>
/// </summary>
public sealed class InputHook : IDisposable
{
    // These fields exist purely to root the delegates for the lifetime of the hook. Do not inline
    // them into the SetWindowsHookExW calls.
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;

    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private bool _disposed;

    public event Action<KeyInput>? Key;
    public event Action<MouseInput>? Mouse;

    /// <summary>
    /// Consulted before <see cref="Key"/> is raised. Returning true keeps the keystroke from
    /// reaching the rest of the system: the hook returns a non-zero value instead of calling
    /// <c>CallNextHookEx</c>. Runs inside the low-level hook callback, so it must be fast and
    /// must not throw.
    /// </summary>
    public Func<KeyInput, bool>? ShouldSuppressKey { get; set; }

    /// <summary>Mouse counterpart of <see cref="ShouldSuppressKey"/>.</summary>
    public Func<MouseInput, bool>? ShouldSuppressMouse { get; set; }

    public InputHook()
    {
        _keyboardProc = OnKeyboardCallback;
        _mouseProc = OnMouseCallback;
    }

    public bool IsInstalled => _keyboardHook != IntPtr.Zero && _mouseHook != IntPtr.Zero;

    public void Start()
    {
        if (IsInstalled)
            return;

        var module = GetModuleHandleW(null);

        _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_keyboardHook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx(WH_KEYBOARD_LL) failed.");

        _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, module, 0);
        if (_mouseHook == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "SetWindowsHookEx(WH_MOUSE_LL) failed.");
        }
    }

    public void Stop()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    /// <summary>Modifier keys as reported by a low-level keyboard hook (left/right specific).</summary>
    public static bool IsModifierKey(int virtualKey) => virtualKey is
        0x10 or 0xA0 or 0xA1 or   // Shift / LShift / RShift
        0x11 or 0xA2 or 0xA3 or   // Ctrl  / LCtrl  / RCtrl
        0x12 or 0xA4 or 0xA5 or   // Alt   / LAlt   / RAlt
        0x5B or 0x5C;             // LWin  / RWin

    private IntPtr OnKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HC_ACTION)
        {
            var message = (int)wParam;
            var isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;   // Alt+X arrives as WM_SYSKEYDOWN
            var isUp = message is WM_KEYUP or WM_SYSKEYUP;

            if (isDown || isUp)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var input = new KeyInput(
                    (int)data.vkCode,
                    isDown,
                    (data.flags & LLKHF_INJECTED) != 0);

                var suppress = WantsSuppressedKey(input);
                PublishKey(input);

                if (suppress)
                    return new IntPtr(1);
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr OnMouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HC_ACTION)
        {
            var message = (int)wParam;
            MouseButtonKind? button = message switch
            {
                WM_LBUTTONDOWN or WM_LBUTTONUP => MouseButtonKind.Left,
                WM_RBUTTONDOWN or WM_RBUTTONUP => MouseButtonKind.Right,
                WM_MBUTTONDOWN or WM_MBUTTONUP => MouseButtonKind.Middle,
                WM_XBUTTONDOWN or WM_XBUTTONUP => MouseButtonKind.Left, // resolved from mouseData below
                _ => null,
            };

            if (button is not null)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                // Side buttons pack the button index into the high word of mouseData.
                if (message is WM_XBUTTONDOWN or WM_XBUTTONUP)
                {
                    var index = (int)(data.mouseData >> 16);
                    button = index == XBUTTON2 ? MouseButtonKind.X2 : MouseButtonKind.X1;
                }

                var isDown = message is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN;
                var input = new MouseInput(
                    button.Value,
                    isDown,
                    (data.flags & LLMHF_INJECTED) != 0);

                var suppress = WantsSuppressedMouse(input);
                PublishMouse(input);

                if (suppress)
                    return new IntPtr(1);
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool WantsSuppressedKey(KeyInput input)
    {
        try
        {
            return ShouldSuppressKey?.Invoke(input) == true;
        }
        catch
        {
            // Never let an exception escape a hook callback; default to letting input through.
            return false;
        }
    }

    private bool WantsSuppressedMouse(MouseInput input)
    {
        try
        {
            return ShouldSuppressMouse?.Invoke(input) == true;
        }
        catch
        {
            return false;
        }
    }

    private void PublishKey(KeyInput input)
    {
        try
        {
            Key?.Invoke(input);
        }
        catch
        {
            // An exception escaping a hook callback unwinds into the OS. Never let that happen.
        }
    }

    private void PublishMouse(MouseInput input)
    {
        try
        {
            Mouse?.Invoke(input);
        }
        catch
        {
            // See PublishKey.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
    }
}
