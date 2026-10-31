using NAudio.CoreAudioApi;
using Squelch.Interop;

namespace Squelch.Core;

public enum CaptureDeviceRole
{
    Communications = 0,
    Multimedia = 1,
    Console = 2,
}

/// <summary>
/// Mutes/unmutes the default capture endpoint through the Core Audio API
/// (IMMDeviceEnumerator -> IMMDevice -> IAudioEndpointVolume).
///
/// <para>
/// This mutes the device <b>system-wide</b> - every application sees a muted microphone. That is
/// usually what push-to-talk wants. There is no clean public API for muting "just for one app".
/// </para>
///
/// <para>
/// The default device is re-resolved on every call instead of being cached, which makes device
/// hot-swapping and default-device changes work without implementing IMMNotificationClient. The
/// cost is one extra COM call per toggle, which is irrelevant because this never runs inside the
/// hook callback.
/// </para>
/// </summary>
public sealed class MicrophoneController : IDisposable
{
    private static bool _comInitialized;

    private MMDeviceEnumerator? _enumerator;
    private bool _hasSavedState;
    private bool _savedMuteState;

    public CaptureDeviceRole DeviceRole { get; set; } = CaptureDeviceRole.Communications;

    public event Action<string>? Error;

    private Role NaudioRole => DeviceRole switch
    {
        CaptureDeviceRole.Multimedia => Role.Multimedia,
        CaptureDeviceRole.Console => Role.Console,
        _ => Role.Communications,
    };

    private MMDeviceEnumerator Enumerator
    {
        get
        {
            EnsureComInitialized();
            return _enumerator ??= new MMDeviceEnumerator();
        }
    }

    /// <summary>
    /// The UI thread is already an STA, but this keeps the controller usable from a thread that has
    /// not initialized COM itself. An HRESULT of S_FALSE or RPC_E_CHANGED_MODE just means "already
    /// initialized", which is fine. We deliberately never call CoUninitialize: the owner of the
    /// apartment is whoever got here first.
    /// </summary>
    private static void EnsureComInitialized()
    {
        if (_comInitialized)
            return;

        _comInitialized = true;
        NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.COINIT_APARTMENTTHREADED);
    }

    public bool? TryGetMuted(out string? deviceName)
    {
        deviceName = null;

        try
        {
            using var device = Enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, NaudioRole);
            deviceName = device.FriendlyName;
            return device.AudioEndpointVolume.Mute;
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex.Message);
            return null;
        }
    }

    public void SetMuted(bool muted)
    {
        try
        {
            using var device = Enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, NaudioRole);
            device.AudioEndpointVolume.Mute = muted;
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex.Message);
        }
    }

    /// <summary>
    /// Remembers whether the microphone was muted before Squelch took control, so exiting or
    /// disabling the app restores the user's own choice. Returns false when the endpoint could not
    /// be read (the error has already been reported through <see cref="Error"/>).
    /// </summary>
    public bool SaveCurrentState()
    {
        var muted = TryGetMuted(out _);
        if (muted is null)
            return false;

        _savedMuteState = muted.Value;
        _hasSavedState = true;
        return true;
    }

    public void RestoreSavedState()
    {
        if (!_hasSavedState)
            return;

        SetMuted(_savedMuteState);
        _hasSavedState = false;
    }

    public void Dispose()
    {
        _enumerator?.Dispose();
        _enumerator = null;
    }
}
