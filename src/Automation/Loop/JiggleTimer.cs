using System.Runtime.InteropServices;
using Automation.Logging;

namespace Automation.Loop;

/// Nudges the cursor 1px right, then 1px left, every `interval`. macOS only.
/// Gated by config.jiggle_enabled and --no-jiggle; dry-run implies jiggle-off.
public sealed class JiggleTimer : IDisposable
{
    private readonly ILogger _log;
    private readonly TimeSpan _interval;
    private readonly Timer _timer;

    public JiggleTimer(TimeSpan interval, ILogger log)
    {
        _interval = interval;
        _log = log;
        _timer = new Timer(_ => TryJiggle(), null, interval, interval);
    }

    private void TryJiggle()
    {
        try
        {
            if (!OperatingSystem.IsMacOS())
            {
                _log.Warn("jiggle.unsupported_os");
                return;
            }
            NativeMouse.NudgeAndReturn();
            _log.Info("jiggle.tick");
        }
        catch (Exception ex)
        {
            _log.Error("jiggle.failed", new { error = ex.Message });
        }
    }

    public void Dispose() => _timer.Dispose();
}

/// macOS CoreGraphics P/Invoke for cursor movement.
/// Uses kCGEventMouseMoved to post a synthetic mouse-move event.
internal static class NativeMouse
{
    private const string CoreGraphics = "/System/Library/Frameworks/ApplicationServices.framework/Frameworks/CoreGraphics.framework/CoreGraphics";

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint { public double X; public double Y; }

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventCreate(IntPtr sourceRef);

    [DllImport(CoreGraphics)]
    private static extern CGPoint CGEventGetLocation(IntPtr @event);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventCreateMouseEvent(
        IntPtr source, int mouseType, CGPoint location, int mouseButton);

    [DllImport(CoreGraphics)]
    private static extern void CGEventPost(int tap, IntPtr @event);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    private const int kCGEventMouseMoved = 5;
    private const int kCGHIDEventTap = 0;
    private const int kCGMouseButtonLeft = 0;

    public static void NudgeAndReturn()
    {
        var snapshot = CGEventCreate(IntPtr.Zero);
        var pos = CGEventGetLocation(snapshot);
        CFRelease(snapshot);

        Move(new CGPoint { X = pos.X + 1, Y = pos.Y });
        Thread.Sleep(25);
        Move(pos);
    }

    private static void Move(CGPoint to)
    {
        var ev = CGEventCreateMouseEvent(IntPtr.Zero, kCGEventMouseMoved, to, kCGMouseButtonLeft);
        CGEventPost(kCGHIDEventTap, ev);
        CFRelease(ev);
    }
}
