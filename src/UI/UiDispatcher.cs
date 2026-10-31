namespace Squelch.UI;

/// <summary>
/// A UI-thread message pump anchor.
///
/// <para>
/// <see cref="Post"/> <b>always</b> queues the work, even when called from the UI thread. That is
/// the whole point: the hook callback already runs on the UI thread, and the thing we are avoiding
/// is doing slow work (Core Audio) inside the callback, not cross-thread access. Control.BeginInvoke
/// posts a message and returns immediately, so the hook stays well under the
/// LowLevelHooksTimeout budget.
/// </para>
/// </summary>
public sealed class UiDispatcher : IDisposable
{
    private readonly Control _anchor = new();
    private bool _disposed;

    public UiDispatcher()
    {
        // Touching .Handle forces handle creation; BeginInvoke throws without one.
        _ = _anchor.Handle;
    }

    public void Post(Action action)
    {
        if (_disposed)
            return;

        try
        {
            if (_anchor.IsHandleCreated)
                _anchor.BeginInvoke(action);
            else
                action();
        }
        catch (ObjectDisposedException)
        {
            // Shutting down; nothing left to run.
        }
        catch (InvalidOperationException)
        {
            // Handle destroyed between the check and the call.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _anchor.Dispose();
    }
}
