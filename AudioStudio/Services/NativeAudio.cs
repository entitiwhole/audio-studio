using System;
using System.Runtime.InteropServices;

namespace AudioStudio;

[StructLayout(LayoutKind.Sequential)]
internal struct AutomationPoint
{
    public long SamplePosition;
    public float Value;
}

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

    // Loop
    [DllImport("AudioBridge.dll")]
    public static extern void SetLoopPoints(IntPtr h, bool enabled, long loopStartSample, long loopEndSample);

    // Record
    [DllImport("AudioBridge.dll", CharSet = CharSet.Unicode)]
    public static extern void StartRecording(IntPtr h, string outputPath);

    [DllImport("AudioBridge.dll")]
    public static extern void StopRecording(IntPtr h);

    [DllImport("AudioBridge.dll")]
    public static extern bool IsRecording(IntPtr h);

    // Export
    [DllImport("AudioBridge.dll", CharSet = CharSet.Unicode)]
    public static extern bool StartExport(IntPtr h, string outputPath, int sampleRate, int bitDepth);

    [DllImport("AudioBridge.dll")]
    public static extern float GetExportProgress(IntPtr h);

    [DllImport("AudioBridge.dll")]
    public static extern bool IsExporting(IntPtr h);

    // Automation
    [DllImport("AudioBridge.dll")]
    public static extern void SetAutomation(IntPtr h, int paramIndex,
        [In] AutomationPoint[] points, int numPoints);

    // Transport / BPM
    [DllImport("AudioBridge.dll")]
    public static extern void SetBPM(IntPtr h, double bpm);

    [DllImport("AudioBridge.dll")]
    public static extern double GetBPM(IntPtr h);

    [DllImport("AudioBridge.dll")]
    public static extern double SnapTimeToGrid(IntPtr h, double timeSec, double gridDivision);

    [DllImport("AudioBridge.dll")]
    public static extern bool IsBeatStartOfBar(IntPtr h, double timeSec);

    // Visual ring buffer API
    [DllImport("AudioBridge.dll")]
    public static extern IntPtr CreateVisualRingBuffer();

    [DllImport("AudioBridge.dll")]
    public static extern void DeleteVisualRingBuffer(IntPtr handle);

    [DllImport("AudioBridge.dll")]
    public static extern void AttachVisualBuffer(IntPtr chainHandle, IntPtr ringHandle);

    [DllImport("AudioBridge.dll")]
    public static extern bool ReadVisualData(IntPtr ringHandle,
        out float peak, out float rms, out long sampleIndex);
}
