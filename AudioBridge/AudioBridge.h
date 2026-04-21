#pragma once

#ifdef AUDIOBRIDGE_EXPORTS
#define AUDIOBRIDGE_API __declspec(dllexport)
#else
#define AUDIOBRIDGE_API __declspec(dllimport)
#endif

extern "C" {
    AUDIOBRIDGE_API void* CreateEffectChain(int sampleRate, int channels);
    AUDIOBRIDGE_API void DeleteEffectChain(void* handle);
    AUDIOBRIDGE_API void SetLowPass(void* handle, bool enabled, float cutoff);
    AUDIOBRIDGE_API void SetHighPass(void* handle, bool enabled, float cutoff);
    AUDIOBRIDGE_API void SetGain(void* handle, bool enabled, float gainDb);
    AUDIOBRIDGE_API void SetEcho(void* handle, bool enabled, float delayMs, float feedback, float wetMix);
    AUDIOBRIDGE_API void SetReverb(void* handle, bool enabled, float wet, float roomSize);
    AUDIOBRIDGE_API void ProcessBuffer(void* handle, float* buffer, int sampleCount);
    AUDIOBRIDGE_API void ResetEffectChain(void* handle);
}
