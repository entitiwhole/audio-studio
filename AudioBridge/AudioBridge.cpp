#define AUDIOBRIDGE_EXPORTS
#include "AudioBridge.h"
#include <vector>
#include <cmath>
#include <algorithm>

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
        if (lpEnabled) lowPass->Process(buffer, samples);
        if (hpEnabled) highPass->Process(buffer, samples);
        if (gainEnabled) gain->Process(buffer, samples);
        if (echoEnabled) echo->Process(buffer, samples);
        if (reverbEnabled) reverb->Process(buffer, samples);
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
}
