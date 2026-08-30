using System;
using System.Runtime.InteropServices;

namespace Demo;

/// <summary>
/// Asks RenderDoc to capture the next presented frame, from inside the game.
/// </summary>
/// <remarks>
/// RenderDoc's own trigger is F12, and synthesized key presses do not reach this process - the
/// same wall the capture tour ran into. Its in-application API has no such problem: when the game
/// was launched through <c>renderdoccmd capture</c>, renderdoc.dll is already loaded here and
/// TriggerCapture does what the key would have. Outside that, the library is absent and every call
/// here is a no-op.
/// </remarks>
public static class RenderDocCapture
{
    // renderdoc_app.h: the API is a table of function pointers in a fixed order. TriggerCapture is
    // the sixteenth, and 1.0.0 is the version every build since 2018 satisfies - asking for the
    // oldest one that has what we need keeps this working across RenderDoc versions.
    private const int ApiVersion100 = 10000;
    private const int TriggerCaptureSlot = 15;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TriggerCaptureDelegate();

    [DllImport("renderdoc.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int RENDERDOC_GetAPI(int version, out IntPtr apiPointers);

    private static TriggerCaptureDelegate trigger;
    private static bool probed;

    /// <summary>Whether RenderDoc is hosting this process and can be asked for a capture.</summary>
    public static bool Available
    {
        get
        {
            if (probed)
                return trigger is not null;

            probed = true;
            try
            {
                if (RENDERDOC_GetAPI(ApiVersion100, out var api) != 1 || api == IntPtr.Zero)
                    return false;

                var function = Marshal.ReadIntPtr(api, TriggerCaptureSlot * IntPtr.Size);
                if (function == IntPtr.Zero)
                    return false;

                trigger = Marshal.GetDelegateForFunctionPointer<TriggerCaptureDelegate>(function);
                return true;
            }
            catch (DllNotFoundException)
            {
                // Not running under RenderDoc, which is the normal case.
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    /// <summary>Captures the next frame, if RenderDoc is listening. Returns whether it asked.</summary>
    public static bool TriggerNextFrame()
    {
        if (!Available)
            return false;

        trigger();
        return true;
    }
}
