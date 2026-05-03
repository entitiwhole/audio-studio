using System;
using System.Runtime.InteropServices;

namespace AudioStudio;

internal static class NativeAudio
{
    [DllImport("AudioBridge.dll")]
    public static extern IntPtr CreateEffectChain(int sampleRate, int channels);

    [DllImport("AudioBridge.dll")]
    public static extern void DeleteEffectChain(IntPtr handle);

    [DllImport("AudioBridge.dll")]
    public static extern void ProcessBuffer(IntPtr handle, float[] buffer, int sampleCount);

    [DllImport("AudioBridge.dll")]
    public static extern void SetLowPass(IntPtr h, bool en, float cutoff);

    [DllImport("AudioBridge.dll")]
    public static extern void SetHighPass(IntPtr h, bool en, float cutoff);

    [DllImport("AudioBridge.dll")]
    public static extern void SetGain(IntPtr h, bool en, float gainDb);

    [DllImport("AudioBridge.dll")]
    public static extern void SetEcho(IntPtr h, bool en, float delay, float fb, float mix);

    [DllImport("AudioBridge.dll")]
    public static extern void SetReverb(IntPtr h, bool en, float wet, float room);
}
