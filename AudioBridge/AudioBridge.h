#pragma once

#include <atomic>
#include <cmath>
#include <cstdint>

#ifdef AUDIOBRIDGE_EXPORTS
#define AUDIOBRIDGE_API __declspec(dllexport)
#else
#define AUDIOBRIDGE_API __declspec(dllimport)
#endif

// Lock-free ring buffer entry for visualization data
struct VisualData {
    float peak;
    float rms;
    int64_t sampleIndex;
};

struct VisualRingBuffer {
    static constexpr int kSize = 256;
    VisualData data[kSize];
    alignas(64) std::atomic<int> writeIndex{ 0 };
    alignas(64) std::atomic<int> readIndex{ 0 };

    bool TryPush(float peak, float rms, int64_t sampleIdx) {
        int w = writeIndex.load(std::memory_order_relaxed);
        int next = (w + 1) % kSize;
        if (next == readIndex.load(std::memory_order_acquire))
            return false;
        data[w].peak = peak;
        data[w].rms = rms;
        data[w].sampleIndex = sampleIdx;
        writeIndex.store(next, std::memory_order_release);
        return true;
    }

    bool TryPop(VisualData& out) {
        int r = readIndex.load(std::memory_order_relaxed);
        if (r == writeIndex.load(std::memory_order_acquire))
            return false;
        out = data[r];
        int next = (r + 1) % kSize;
        readIndex.store(next, std::memory_order_release);
        return true;
    }
};

// Automation point structure
struct AutomationPoint {
    int64_t samplePosition;
    float value; // 0.0 – 1.0
};

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

    // Loop
    AUDIOBRIDGE_API void SetLoopPoints(void* handle, bool enabled, int64_t loopStartSample, int64_t loopEndSample);

    // Record
    AUDIOBRIDGE_API void StartRecording(void* handle, const wchar_t* outputPath);
    AUDIOBRIDGE_API void StopRecording(void* handle);
    AUDIOBRIDGE_API bool IsRecording(void* handle);

    // Export
    AUDIOBRIDGE_API bool StartExport(void* handle, const wchar_t* outputPath, int sampleRate, int bitDepth);
    AUDIOBRIDGE_API float GetExportProgress(void* handle);
    AUDIOBRIDGE_API bool IsExporting(void* handle);

    // Automation
    AUDIOBRIDGE_API void SetAutomation(void* handle, int paramIndex,
                                       const AutomationPoint* points, int numPoints);

    // Transport / BPM
    AUDIOBRIDGE_API void SetBPM(void* handle, double bpm);
    AUDIOBRIDGE_API double GetBPM(void* handle);
    AUDIOBRIDGE_API double SnapTimeToGrid(void* handle, double timeSec, double gridDivision);
    AUDIOBRIDGE_API bool IsBeatStartOfBar(void* handle, double timeSec);

    // Visual ring buffer API
    AUDIOBRIDGE_API void* CreateVisualRingBuffer();
    AUDIOBRIDGE_API void  DeleteVisualRingBuffer(void* handle);
    AUDIOBRIDGE_API void  AttachVisualBuffer(void* chainHandle, void* ringHandle);
    AUDIOBRIDGE_API bool  ReadVisualData(void* ringHandle, float* outPeak, float* outRms, int64_t* outSampleIndex);
}

// Internal: write visual data from EffectChain
void WriteVisualData(void* ringHandle, float peak, float rms, int64_t sampleIdx);
