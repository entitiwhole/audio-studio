#define AUDIOBRIDGE_EXPORTS
#include "AudioBridge.h"
#include <vector>
#include <algorithm>
#include <cmath>
#include <fstream>
#include <string>
#include <cstring>
#include <cstring>

// Simple WAV writer (no external dependencies)
struct RecordState {
    std::ofstream file;
    int sampleRate = 44100;
    int channels = 2;
    int bitsPerSample = 16;
    int dataSizePos = 0; // position in file for data chunk size
    int totalDataBytes = 0;

    bool Open(const wchar_t* path, int sr, int ch) {
        sampleRate = sr;
        channels = ch;
        file.open(path, std::ios::binary);
        if (!file.is_open()) return false;

        // Write RIFF header (placeholder sizes)
        auto write32 = [&](uint32_t v) { file.write((char*)&v, 4); };
        auto write16 = [&](uint16_t v) { file.write((char*)&v, 2); };

        write32(0x46464952); // "RIFF"
        write32(0);          // file size - 8 (placeholder)
        write32(0x45564157); // "WAVE"
        write32(0x20746D66); // "fmt "
        write32(16);         // fmt chunk size
        write16(1);          // PCM
        write16((uint16_t)channels);
        write32((uint32_t)sampleRate);
        write32((uint32_t)(sampleRate * channels * bitsPerSample / 8)); // byte rate
        write16((uint16_t)(channels * bitsPerSample / 8)); // block align
        write16((uint16_t)bitsPerSample);
        write32(0x61746164); // "data"
        dataSizePos = (int)file.tellp();
        write32(0);          // data chunk size (placeholder)
        totalDataBytes = 0;
        return true;
    }

    void WriteSamples(const float* buffer, int numSamples) {
        if (!file.is_open()) return;
        for (int i = 0; i < numSamples; i++) {
            short sample = (short)(buffer[i] * 32767.0f);
            if (sample > 32767) sample = 32767;
            if (sample < -32768) sample = -32768;
            file.write((char*)&sample, 2);
        }
        totalDataBytes += numSamples * 2;
    }

    void Close() {
        if (!file.is_open()) return;
        // Update data chunk size
        file.seekp(dataSizePos);
        uint32_t dataSize = totalDataBytes;
        file.write((char*)&dataSize, 4);
        // Update RIFF file size
        file.seekp(4);
        uint32_t riffSize = 36 + totalDataBytes;
        file.write((char*)&riffSize, 4);
        file.close();
    }

    bool IsOpen() const { return file.is_open(); }
};

// Automation interpolation
struct AutomationTrack {
    AutomationPoint* points = nullptr;
    int numPoints = 0;
    int currentSegment = 0;

    void Set(AutomationPoint* pts, int n) {
        points = pts;
        numPoints = n;
        currentSegment = 0;
    }

    float GetValueAtSample(int64_t sample) const {
        if (!points || numPoints < 2) return 1.0f;
        // Binary search for the segment containing `sample`
        int lo = 0, hi = numPoints - 1;
        while (lo < hi - 1) {
            int mid = (lo + hi) / 2;
            if (points[mid].samplePosition <= sample) lo = mid;
            else hi = mid;
        }
        if (sample <= points[lo].samplePosition)
            return points[lo].value;
        if (sample >= points[hi].samplePosition)
            return points[hi].value;

        // Linear interpolation
        int64_t dx = points[hi].samplePosition - points[lo].samplePosition;
        if (dx == 0) return points[lo].value;
        float t = (float)(double)(sample - points[lo].samplePosition) / (float)dx;
        return points[lo].value * (1.0f - t) + points[hi].value * t;
    }
};

struct LowPassFilter {
    float sampleRate = 44100.0f;
    float RC = 1.0f / (2.0f * 3.14159f * 20000.0f);
    float alpha = 0.0f;
    float lastOutput = 0.0f;

    LowPassFilter(float sr) {
        sampleRate = sr;
        RC = 1.0f / (2.0f * 3.14159f * 20000.0f);
        alpha = RC / (RC + 1.0f / sampleRate);
    }
    
    void SetCutoff(float freq) {
        RC = 1.0f / (2.0f * 3.14159f * freq);
        alpha = RC / (RC + 1.0f / sampleRate);
    }
    
    void Process(float* buffer, int samples) {
        for (int i = 0; i < samples; i++) {
            lastOutput = alpha * lastOutput + (1.0f - alpha) * buffer[i];
            buffer[i] = lastOutput;
        }
    }
};

struct HighPassFilter {
    float sampleRate = 44100.0f;
    float RC = 1.0f / (2.0f * 3.14159f * 20.0f);
    float alpha = 0.0f;
    float lastInput = 0.0f;
    float lastOutput = 0.0f;

    HighPassFilter(float sr) {
        sampleRate = sr;
        RC = 1.0f / (2.0f * 3.14159f * 20.0f);
        alpha = RC / (RC + 1.0f / sampleRate);
    }
    
    void SetCutoff(float freq) {
        RC = 1.0f / (2.0f * 3.14159f * freq);
        alpha = RC / (RC + 1.0f / sampleRate);
    }
    
    void Process(float* buffer, int samples) {
        for (int i = 0; i < samples; i++) {
            float currentInput = buffer[i];
            buffer[i] = alpha * (lastOutput + currentInput - lastInput);
            lastInput = currentInput;
            lastOutput = buffer[i];
        }
    }
};

struct GainProcessor {
    float multiplier = 1.0f;
    
    void SetGain(float dB) {
        multiplier = powf(10.0f, dB / 20.0f);
    }
    
    void Process(float* buffer, int samples) {
        for (int i = 0; i < samples; i++) {
            buffer[i] *= multiplier;
        }
    }
};

struct EchoEffect {
    int sampleRate = 44100;
    int delaySamples = 22050;
    float feedback = 0.5f;
    float wetMix = 0.3f;
    std::vector<float> delayBuffer;
    int writeIndex = 0;

    EchoEffect(int sr) : sampleRate(sr) {
        delaySamples = (int)(0.5f * sampleRate);
        delayBuffer.resize(delaySamples, 0.0f);
    }

    void SetParams(float delayMs, float fb, float wet) {
        feedback = fb;
        wetMix = wet;
        delaySamples = (int)(delayMs * sampleRate / 1000.0f);
        if (delaySamples < 1) delaySamples = 1;
        if ((int)delayBuffer.size() != delaySamples) {
            delayBuffer.resize(delaySamples, 0.0f);
        }
        writeIndex = 0;
    }

    void Process(float* buffer, int samples) {
        for (int i = 0; i < samples; i++) {
            float delayed = delayBuffer[writeIndex];
            float newSample = buffer[i] + delayed * feedback;
            buffer[i] = buffer[i] * (1.0f - wetMix) + delayed * wetMix;
            delayBuffer[writeIndex] = newSample;
            writeIndex = (writeIndex + 1) % delaySamples;
        }
    }
};

struct ReverbEffect {
    int sampleRate = 44100;
    std::vector<std::vector<float>> combBuffers;
    std::vector<int> combIndices;
    std::vector<int> combDelays = { 1557, 1617, 1491, 1422, 1277, 1356, 1188, 1116 };
    std::vector<std::vector<float>> allpassBuffers;
    std::vector<int> allpassIndices;
    std::vector<int> allpassDelays = { 225, 556, 441, 341 };
    float wetMix = 0.3f;
    float roomSize = 0.5f;

    ReverbEffect(int sr) : sampleRate(sr) {
        combBuffers.resize(combDelays.size());
        combIndices.resize(combDelays.size(), 0);
        for (size_t i = 0; i < combDelays.size(); i++) {
            combBuffers[i].resize(combDelays[i], 0.0f);
        }

        allpassBuffers.resize(allpassDelays.size());
        allpassIndices.resize(allpassDelays.size(), 0);
        for (size_t i = 0; i < allpassDelays.size(); i++) {
            allpassBuffers[i].resize(allpassDelays[i], 0.0f);
        }
    }

    void SetParams(float wet, float size) {
        wetMix = wet;
        roomSize = size;
    }

    void Process(float* buffer, int samples) {
        float damp = 0.5f * roomSize;
        float fb = 0.84f * roomSize;

        for (int i = 0; i < samples; i++) {
            float input = buffer[i];
            float wetSignal = 0.0f;

            for (size_t c = 0; c < combBuffers.size(); c++) {
                int readIdx = combIndices[c] - combDelays[c];
                if (readIdx < 0) readIdx += (int)combBuffers[c].size();
                float delayed = combBuffers[c][readIdx];
                wetSignal += delayed;
                combBuffers[c][combIndices[c]] = input + delayed * fb;
                combIndices[c] = (combIndices[c] + 1) % (int)combBuffers[c].size();
            }

            wetSignal /= (float)combBuffers.size();

            for (size_t a = 0; a < allpassBuffers.size(); a++) {
                int readIdx = allpassIndices[a] - allpassDelays[a];
                if (readIdx < 0) readIdx += (int)allpassBuffers[a].size();
                float delayed = allpassBuffers[a][readIdx];
                float temp = -wetSignal + delayed;
                allpassBuffers[a][allpassIndices[a]] = wetSignal + delayed * 0.5f;
                wetSignal = temp;
                allpassIndices[a] = (allpassIndices[a] + 1) % (int)allpassBuffers[a].size();
            }

            buffer[i] = input * (1.0f - wetMix) + wetSignal * wetMix;
        }
    }
};

struct EffectChain {
    int sampleRate = 44100;
    LowPassFilter* lowPass = nullptr;
    HighPassFilter* highPass = nullptr;
    GainProcessor* gain = nullptr;
    EchoEffect* echo = nullptr;
    ReverbEffect* reverb = nullptr;
    bool lpEnabled = false;
    bool hpEnabled = false;
    bool gainEnabled = false;
    bool echoEnabled = false;
    bool reverbEnabled = false;
    VisualRingBuffer* visualBuffer = nullptr;
    int64_t processedSamples = 0;

    // Loop state
    bool isLooping = false;
    int64_t loopStartSample = 0;
    int64_t loopEndSample = 0;

    // Record state
    RecordState* recorder = nullptr;

    // Export state
    RecordState* exporter = nullptr;
    int64_t exportTotalSamples = 0;
    int64_t exportProcessedSamples = 0;

    // Automation
    AutomationTrack volAutomation;
    AutomationTrack cutoffAutomation;

    // Transport / BPM
    double bpm = 120.0;

    EffectChain(int sr) : sampleRate(sr) {
        lowPass = new LowPassFilter((float)sr);
        highPass = new HighPassFilter((float)sr);
        gain = new GainProcessor();
        echo = new EchoEffect(sr);
        reverb = new ReverbEffect(sr);
    }

    ~EffectChain() {
        delete lowPass;
        delete highPass;
        delete gain;
        delete echo;
        delete reverb;
    }

    void Process(float* buffer, int samples) {
        int processed = 0;
        while (processed < samples) {
            // Loop wrapping
            if (isLooping && loopEndSample > loopStartSample && processedSamples >= loopEndSample) {
                processedSamples = loopStartSample;
            }

            int toProcess = samples - processed;
            if (isLooping && loopEndSample > loopStartSample) {
                int64_t remaining = loopEndSample - processedSamples;
                if (remaining <= 0) {
                    processedSamples = loopStartSample;
                    remaining = loopEndSample - loopStartSample;
                }
                if (toProcess > remaining) toProcess = (int)remaining;
            }

            float* chunk = buffer + processed;

            // Apply automation (per-sample interpolation)
            if (volAutomation.numPoints > 0) {
                for (int i = 0; i < toProcess; i++) {
                    chunk[i] *= volAutomation.GetValueAtSample(processedSamples + i);
                }
            }
            if (cutoffAutomation.numPoints > 0) {
                lowPass->SetCutoff(
                    20000.0f * cutoffAutomation.GetValueAtSample(processedSamples));
            }

            if (lpEnabled) lowPass->Process(chunk, toProcess);
            if (hpEnabled) highPass->Process(chunk, toProcess);
            if (gainEnabled) gain->Process(chunk, toProcess);
            if (echoEnabled) echo->Process(chunk, toProcess);
            if (reverbEnabled) reverb->Process(chunk, toProcess);

            // Record processed output
            if (recorder && recorder->IsOpen()) {
                recorder->WriteSamples(chunk, toProcess);
            }

            // Export processed output
            if (exporter && exporter->IsOpen()) {
                exporter->WriteSamples(chunk, toProcess);
                exportProcessedSamples += toProcess;
            }

            // Write visual data
            if (visualBuffer) {
                float peak = 0.0f, rmsAccum = 0.0f;
                for (int i = 0; i < toProcess; i++) {
                    float absVal = fabsf(chunk[i]);
                    if (absVal > peak) peak = absVal;
                    rmsAccum += chunk[i] * chunk[i];
                }
                float rms = sqrtf(rmsAccum / (toProcess > 0 ? toProcess : 1));
                visualBuffer->TryPush(peak, rms, processedSamples);
            }

            processedSamples += toProcess;
            processed += toProcess;
        }
    }
};

extern "C" {
    AUDIOBRIDGE_API void* CreateEffectChain(int sampleRate, int channels) {
        return new EffectChain(sampleRate);
    }

    AUDIOBRIDGE_API void DeleteEffectChain(void* handle) {
        if (handle) delete (EffectChain*)handle;
    }

    AUDIOBRIDGE_API void SetLowPass(void* handle, bool enabled, float cutoff) {
        if (!handle) return;
        auto* chain = (EffectChain*)handle;
        chain->lpEnabled = enabled;
        chain->lowPass->SetCutoff(cutoff);
    }

    AUDIOBRIDGE_API void SetHighPass(void* handle, bool enabled, float cutoff) {
        if (!handle) return;
        auto* chain = (EffectChain*)handle;
        chain->hpEnabled = enabled;
        chain->highPass->SetCutoff(cutoff);
    }

    AUDIOBRIDGE_API void SetGain(void* handle, bool enabled, float gainDb) {
        if (!handle) return;
        auto* chain = (EffectChain*)handle;
        chain->gainEnabled = enabled;
        chain->gain->SetGain(gainDb);
    }

    AUDIOBRIDGE_API void SetEcho(void* handle, bool enabled, float delayMs, float feedback, float wetMix) {
        if (!handle) return;
        auto* chain = (EffectChain*)handle;
        chain->echoEnabled = enabled;
        chain->echo->SetParams(delayMs, feedback, wetMix);
    }

    AUDIOBRIDGE_API void SetReverb(void* handle, bool enabled, float wet, float roomSize) {
        if (!handle) return;
        auto* chain = (EffectChain*)handle;
        chain->reverbEnabled = enabled;
        chain->reverb->SetParams(wet, roomSize);
    }

    AUDIOBRIDGE_API void ProcessBuffer(void* handle, float* buffer, int sampleCount) {
        if (!handle) return;
        ((EffectChain*)handle)->Process(buffer, sampleCount);
    }

    AUDIOBRIDGE_API void ResetEffectChain(void* handle) {
        if (handle) ((EffectChain*)handle)->Process(nullptr, 0);
    }

    // Loop
    AUDIOBRIDGE_API void SetLoopPoints(void* handle, bool enabled, int64_t loopStartSample, int64_t loopEndSample) {
        if (!handle) return;
        auto* chain = (EffectChain*)handle;
        chain->isLooping = enabled;
        chain->loopStartSample = loopStartSample;
        chain->loopEndSample = loopEndSample;
    }

    // Record
    AUDIOBRIDGE_API void StartRecording(void* handle, const wchar_t* outputPath) {
        if (!handle || !outputPath) return;
        auto* chain = (EffectChain*)handle;
        if (!chain->recorder) {
            chain->recorder = new RecordState();
        }
        chain->recorder->Open(outputPath, chain->sampleRate, 2);
    }

    AUDIOBRIDGE_API void StopRecording(void* handle) {
        if (!handle) return;
        auto* chain = (EffectChain*)handle;
        if (chain->recorder) {
            chain->recorder->Close();
        }
    }

    AUDIOBRIDGE_API bool IsRecording(void* handle) {
        if (!handle) return false;
        auto* chain = (EffectChain*)handle;
        return chain->recorder && chain->recorder->IsOpen();
    }

    // Export
    AUDIOBRIDGE_API bool StartExport(void* handle, const wchar_t* outputPath, int sampleRate, int bitDepth) {
        if (!handle || !outputPath) return false;
        auto* chain = (EffectChain*)handle;
        if (!chain->exporter) {
            chain->exporter = new RecordState();
        }
        bool ok = chain->exporter->Open(outputPath, sampleRate, 2);
        if (ok) {
            chain->exportTotalSamples = chain->processedSamples;
            chain->exportProcessedSamples = 0;
        }
        return ok;
    }

    AUDIOBRIDGE_API float GetExportProgress(void* handle) {
        if (!handle) return 0.0f;
        auto* chain = (EffectChain*)handle;
        if (chain->exportTotalSamples <= 0) return 1.0f;
        return (float)chain->exportProcessedSamples / (float)chain->exportTotalSamples;
    }

    AUDIOBRIDGE_API bool IsExporting(void* handle) {
        if (!handle) return false;
        auto* chain = (EffectChain*)handle;
        return chain->exporter && chain->exporter->IsOpen();
    }

    // Automation
    AUDIOBRIDGE_API void SetAutomation(void* handle, int paramIndex,
                                       const AutomationPoint* points, int numPoints) {
        if (!handle || !points) return;
        auto* chain = (EffectChain*)handle;
        // We need to copy the points since the array is transient
        auto copy = new AutomationPoint[numPoints];
        memcpy(copy, points, numPoints * sizeof(AutomationPoint));

        switch (paramIndex) {
            case 0: // Volume
                chain->volAutomation.Set(copy, numPoints);
                break;
            case 1: // Low-pass cutoff
                chain->cutoffAutomation.Set(copy, numPoints);
                break;
        }
    }

    // Transport / BPM
    AUDIOBRIDGE_API void SetBPM(void* handle, double bpm) {
        if (!handle) return;
        ((EffectChain*)handle)->bpm = bpm;
    }

    AUDIOBRIDGE_API double GetBPM(void* handle) {
        if (!handle) return 120.0;
        return ((EffectChain*)handle)->bpm;
    }

    AUDIOBRIDGE_API double SnapTimeToGrid(void* handle, double timeSec, double gridDivision) {
        if (!handle) return timeSec;
        auto* chain = (EffectChain*)handle;
        double beats = timeSec * (chain->bpm / 60.0);
        double snapped = round(beats / gridDivision) * gridDivision;
        return snapped * (60.0 / chain->bpm);
    }

    AUDIOBRIDGE_API bool IsBeatStartOfBar(void* handle, double timeSec) {
        if (!handle) return false;
        auto* chain = (EffectChain*)handle;
        double beats = timeSec * (chain->bpm / 60.0);
        int barBeats = 4;
        return fmod(round(beats * 1000.0) / 1000.0, (double)barBeats) < 0.001;
    }

    // Visual ring buffer API
    AUDIOBRIDGE_API void* CreateVisualRingBuffer() {
        return new VisualRingBuffer();
    }

    AUDIOBRIDGE_API void DeleteVisualRingBuffer(void* handle) {
        if (handle) delete (VisualRingBuffer*)handle;
    }

    AUDIOBRIDGE_API void AttachVisualBuffer(void* chainHandle, void* ringHandle) {
        if (!chainHandle) return;
        ((EffectChain*)chainHandle)->visualBuffer = (VisualRingBuffer*)ringHandle;
    }

    AUDIOBRIDGE_API bool ReadVisualData(void* ringHandle, float* outPeak, float* outRms, int64_t* outSampleIndex) {
        if (!ringHandle || !outPeak || !outRms || !outSampleIndex) return false;
        VisualData data;
        if (((VisualRingBuffer*)ringHandle)->TryPop(data)) {
            *outPeak = data.peak;
            *outRms = data.rms;
            *outSampleIndex = data.sampleIndex;
            return true;
        }
        return false;
    }
}

void WriteVisualData(void* ringHandle, float peak, float rms, int64_t sampleIdx) {
    if (!ringHandle) return;
    ((VisualRingBuffer*)ringHandle)->TryPush(peak, rms, sampleIdx);
}
