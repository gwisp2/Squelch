# Squelch

A Windows tray utility that mutes/unmutes the default microphone while you hold a key or mouse
button (or toggles it, if you prefer). Built with .NET 9 + WinForms, global input via low-level
Win32 hooks, and microphone control via the Core Audio API.

Named after the squelch circuit in radio gear, which does the same job: the channel stays silent
until you key the microphone.

```
src/
  Program.cs                     entry point, single-instance guard
  AppSettings.cs                 settings model + JSON persistence (%APPDATA%\Squelch\settings.json)
  Interop/
    NativeMethods.cs             raw Win32 P/Invokes and structs
    InputHook.cs                 global WH_KEYBOARD_LL / WH_MOUSE_LL hooks
  Core/
    TriggerConfig.cs             trigger model (modifiers + key/button) and display names
    TriggerDetector.cs           raw event stream -> press/release edges
    MicrophoneController.cs      Core Audio (IAudioEndpointVolume) mute/unmute
    SquelchEngine.cs             hook -> trigger -> microphone, with the work deferred off the hook
    AutoStart.cs                 HKCU Run key
  UI/
    UiDispatcher.cs              always-queues UI-thread message anchor
    TrayIcons.cs                 embedded tray artwork (idle / active / disabled)
    SquelchApplicationContext.cs the tray app itself
    SettingsForm.cs              the settings window
    TriggerCaptureForm.cs        "press the combination you want" dialog

assets/
  tray-idle.png                  PixelLab walkie-talkie, 32x32
  tray-active.png                green LED + signal waves
  tray-disabled.png              gray + red slash
```

## Build and run

```powershell
dotnet build -c Release
.\bin\Release\net9.0-windows\Squelch.exe
```

There is no main window. Look for the walkie-talkie icon in the notification area; right-click it
for **Settings**, **Enabled** and **Exit**, or double-click for settings.

Verification is manual, which is all this app needs: hold the trigger and watch the tray icon
change. If the low-level hooks fail to install, it says so in a message box rather than running
silently broken.

### Single-file publish

```powershell
# ~100 MB: everything including the .NET desktop runtime in one exe.
dotnet publish -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:PublishTrimmed=false

# ~46 MB: same, with the payload compressed (slightly slower first start).
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true `
  -p:PublishTrimmed=false -p:EnableCompressionInSingleFile=true

# ~1 MB: needs the .NET Desktop Runtime installed on the target machine.
dotnet publish -c Release -p:SelfContained=false
```

Trimming is deliberately off. NAudio reaches Core Audio through COM interfaces the trimmer cannot
see, and silently broken audio interop is a bad trade for the size win. Framework-dependent is fine
if you are the only user, since the .NET 9 SDK is already on this machine.

## How it works, and the parts that will bite you

### Tray icon

The three states are embedded 32x32 PixelLab pixel art in `assets/`: a dark walkie-talkie instead
of the old near-white microphone, so it stays legible on the light notification-area background.
`TrayIcons` loads them from the assembly; replacing the PNGs is enough to re-skin the app. Idle
uses the amber LED, active adds green signal waves, and disabled is gray with a red slash.

### Global input

`SetWindowsHookEx` with `WH_KEYBOARD_LL` / `WH_MOUSE_LL`, thread id `0`. The `_LL` variants are the
right ones because Windows marshals the event to the installing thread instead of injecting a DLL
into every process — which also keeps antivirus software calm.

The rules that matter:

- **The installing thread must pump messages**, and callbacks arrive on that thread. That is the
  WinForms UI thread here.
- **Windows enforces a ~300 ms `LowLevelHooksTimeout`.** A callback that blocks longer than that
  causes the OS to *silently remove your hook* — the app keeps running and just stops working. So
  the callback never touches audio, disk or registry. `SquelchEngine` flips a flag and hands the work to
  `UiDispatcher`, which posts it to the message queue.
- **The callback delegate must stay rooted.** `InputHook` holds it in a field. If you inline it into
  the `SetWindowsHookExW` call, the GC collects the thunk and the next keypress crashes the process.
- **The trigger is swallowed, everything else passes through.** For a matching trigger event the
  callback returns a non-zero value instead of calling `CallNextHookEx`, so the key/button never
  reaches the focused window. Modifier keys are never swallowed. For a combo like Ctrl+J, a bare J
  still types normally; only the combo (and its auto-repeat) is suppressed. Suppression is off
  while Squelch is disabled or while the "set trigger" dialog is capturing input.
- **Elevation is a hard wall.** A non-elevated process cannot see input aimed at an elevated window
  (UIPI). If you need the trigger to fire while Task Manager or an admin console has focus, change
  `requestedExecutionLevel` to `requireAdministrator` in `app.manifest`. There is no middle ground,
  and running elevated means no drag-and-drop from Explorer.
- **Anti-cheat blocks hooks.** Games with kernel-level anti-cheat may ignore or block low-level
  hooks entirely.

### Trigger semantics

`TriggerDetector` implements the conventional push-to-talk rules:

- The trigger latches only if the required modifiers are **already held** when the primary key goes
  down. Pressing the primary first, then adding Ctrl, does nothing.
- While latched, releasing the primary **or** any required modifier deactivates. Adding an extra
  modifier does not.
- In **Toggle** mode, only presses matter; each one flips the state.

`Ctrl` vs `LCtrl`: the detector treats either physical side as satisfying a logical modifier, which
is almost always what you want for a hotkey.

Left mouse is refused as a trigger. The trigger is swallowed, so a left-click trigger would leave
you unable to click anything; the capture dialog rejects it, and a saved/hand-edited configuration
falls back to the default (Space) on load.

### Microphone

`MicrophoneController` walks `IMMDeviceEnumerator -> GetDefaultAudioEndpoint(eCapture, role) ->
IAudioEndpointVolume -> Mute`. This mutes the device **system-wide**: every application sees a muted
microphone. That is usually the point of push-to-talk, but note there is no clean public API for
muting "only for one application".

While enabled, Squelch owns the mute switch: `Unmute while active` (push to talk) keeps the
microphone muted whenever the trigger is not held, and applies that idle state at startup too.
The mute state from before Squelch took control is saved and restored on exit or when the app is
disabled, so it never leaves the microphone in a state it created.

The earlier implementation only saved the state at key-down and restored it on release, which meant
an already-unmuted microphone stayed live the whole time: the "idle" state was never applied.

The default device is re-resolved on every toggle rather than cached. That handles device hot-swaps
and default-device changes without implementing `IMMNotificationClient`; the extra COM call costs
nothing because it never runs inside the hook callback. If you later want the tray icon to track
device changes live, `MMDeviceEnumerator.RegisterEndpointNotificationCallback` is the hook to add.

### Why `UiDispatcher` always queues

`Post` uses `Control.BeginInvoke` even when already on the UI thread. The goal is not thread safety —
the hook callback is already on the UI thread — it is getting out of the callback before the
timeout. `BeginInvoke` posts a message and returns immediately.

## Things that are deliberately not here

- **No swallowing of non-trigger input.** Only the trigger key/button is suppressed.
- **No per-app muting.** Needs a virtual audio device (VB-Cable and friends).
- **Single instance, no IPC.** A second launch exits silently instead of telling the first instance
  to open its settings window.
- **Fixed trigger, one per config.** No multiple hotkeys or per-app profiles.
