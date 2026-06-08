using System;
using System.Threading.Tasks;

namespace AudioStudio.Services;

internal static class NativeAudioService
{
    private static bool _nativeAudioAvailable;
    private static bool _checked;
    private static readonly object _checkLock = new();

    public static Task<bool> CheckAvailableAsync()
    {
        return Task.Run(() =>
        {
            if (_checked) return _nativeAudioAvailable;
            lock (_checkLock)
            {
                if (_checked) return _nativeAudioAvailable;
                try
                {
                    var fx = NativeAudio.CreateEffectChain(44100, 2);
                    if (fx != IntPtr.Zero)
                    {
                        NativeAudio.DeleteEffectChain(fx);
                        _nativeAudioAvailable = true;
                    }
                }
                catch
                {
                    _nativeAudioAvailable = false;
                }
                _checked = true;
                return _nativeAudioAvailable;
            }
        });
    }

    public static Task<float[]> ProcessWithEffectsAsync(
        float[] samples, int sampleRate, int channels,
        Action<IntPtr> configureEffects)
    {
        return Task.Run(() =>
        {
            float[] result = (float[])samples.Clone();
            IntPtr fx = NativeAudio.CreateEffectChain(sampleRate, channels);
            try
            {
                configureEffects(fx);
                NativeAudio.ProcessBuffer(fx, result, result.Length);
            }
            finally
            {
                NativeAudio.DeleteEffectChain(fx);
            }
            return result;
        });
    }

    public static Task ApplyInPlaceAsync(
        float[] samples, int sampleRate, int channels,
        Action<IntPtr> configureEffects)
    {
        return Task.Run(() =>
        {
            IntPtr fx = NativeAudio.CreateEffectChain(sampleRate, channels);
            try
            {
                configureEffects(fx);
                NativeAudio.ProcessBuffer(fx, samples, samples.Length);
            }
            finally
            {
                NativeAudio.DeleteEffectChain(fx);
            }
        });
    }
}
