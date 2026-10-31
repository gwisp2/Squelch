using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Squelch.Interop;

namespace Squelch.UI;

/// <summary>
/// Tray icons loaded from the embedded PixelLab artwork. The dark walkie-talkie stays legible on
/// the light notification-area background, unlike the old near-white microphone.
///
/// <para>
/// <see cref="Bitmap.GetHicon"/> returns an HICON that we own; <see cref="Icon.FromHandle"/> does
/// not take ownership of it, so every handle is tracked and destroyed in <see cref="Dispose"/>.
/// </para>
/// </summary>
public sealed class TrayIcons : IDisposable
{
    private const int Size = 32; // the shell downscales to 16x16 or 20x20 as needed

    private readonly List<IntPtr> _handles = [];

    public TrayIcons()
    {
        Idle = Load("tray-idle.png");
        Active = Load("tray-active.png");
        Disabled = Load("tray-disabled.png");
    }

    /// <summary>Microphone muted / waiting for the trigger.</summary>
    public Icon Idle { get; }

    /// <summary>Trigger held: the microphone is live.</summary>
    public Icon Active { get; }

    /// <summary>Disabled from the tray menu.</summary>
    public Icon Disabled { get; }

    private Icon Load(string fileName)
    {
        var assembly = typeof(TrayIcons).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded tray icon '{fileName}' is missing.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var source = new Bitmap(stream);

        using var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.Clear(Color.Transparent);
            g.DrawImage(source, 0, 0, Size, Size);
        }

        var handle = bitmap.GetHicon();
        _handles.Add(handle);
        return Icon.FromHandle(handle);
    }

    public void Dispose()
    {
        foreach (var handle in _handles)
        {
            NativeMethods.DestroyIcon(handle);
        }

        _handles.Clear();
    }
}
