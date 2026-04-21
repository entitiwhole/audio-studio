using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;

namespace AudioStudio;

public class AudioEngine
{
    private WaveOutEvent? _waveOut;
    private MixingSampleProvider? _mixer;
    
    // Отдельный провайдер для каждого клипа
    private readonly List<ClipSampleProvider> _providers = new();
    
    // Мастер-формат для всех треков
    private readonly int _masterSampleRate = 44100;
    private readonly int _masterChannels = 2;
    
    // Источник истины для времени
    public float CurrentTime { get; private set; }
    
    private readonly Stopwatch _clock = new();
    private float _seekTime;
    
    public event Action? OnPlaybackStopped;
    public event Action<float>? OnTimeChanged;

    public void LoadClips(List<AudioClipModel> clips)
    {
        Stop();

        if (clips.All(c => c.Samples.Length == 0))
            return;

        // Мастер-формат для всех треков
        var masterFormat = WaveFormat.CreateIeeeFloatWaveFormat(_masterSampleRate, _masterChannels);
        
        _mixer = new MixingSampleProvider(masterFormat);
        _providers.Clear();

        foreach (var clip in clips.Where(c => c.Samples.Length > 0))
        {
            // Создаём ОТДЕЛЬНЫЙ провайдер для каждого клипа
            var provider = new ClipSampleProvider(
                clip.Samples,
                clip.SampleRate,
                clip.Channels,
                clip.StartTime);

            _providers.Add(provider);
            
            // Приводим к мастер-формату
            ISampleProvider formattedProvider = provider;
            
            // 1. Ресемплинг (если sample rate разный)
            if (provider.WaveFormat.SampleRate != _masterSampleRate)
            {
                formattedProvider = new WdlResamplingSampleProvider(
                    formattedProvider, 
                    _masterSampleRate);
            }
            
            // 2. Приведение каналов
            if (provider.WaveFormat.Channels == 1 && _masterChannels == 2)
            {
                formattedProvider = new MonoToStereoSampleProvider(
                    formattedProvider);
            }
            else if (provider.WaveFormat.Channels == 2 && _masterChannels == 1)
            {
                formattedProvider = new StereoToMonoSampleProvider(
                    formattedProvider);
            }
            
            // 3. Offset (позиция на таймлайне)
            var offset = new OffsetSampleProvider(formattedProvider)
            {
                DelayBy = TimeSpan.FromSeconds(clip.StartTime)
            };
            
            _mixer.AddMixerInput(offset);
        }

        _waveOut = new WaveOutEvent { DesiredLatency = 100 };
        _waveOut.Init(_mixer);
        
        _waveOut.PlaybackStopped += (s, e) =>
        {
            _clock.Stop();
            OnPlaybackStopped?.Invoke();
        };
    }
    
    // Legacy support for old AudioClip
    public void LoadTracks(List<AudioClip> tracks)
    {
        var clips = tracks.Select(t => new AudioClipModel
        {
            Samples = (float[])t.Samples.Clone(),
            StartTime = (float)t.StartTime,
            Duration = (float)t.Duration,
            SampleRate = t.SampleRate,
            Channels = t.Channels,
            Name = t.Name,
            TrackIndex = t.TrackIndex
        }).ToList();
        
        LoadClips(clips);
    }

    public void Play()
    {
        _waveOut?.Play();
        _clock.Start();
    }

    public void Pause()
    {
        _waveOut?.Pause();
        _clock.Stop();
    }

    public void Stop()
    {
        _waveOut?.Stop();
        _clock.Reset();
        CurrentTime = _seekTime;
    }
    
    public void Seek(float time)
    {
        _seekTime = time;
        CurrentTime = time;
        
        // Seek всех провайдеров относительно их StartTime
        foreach (var provider in _providers)
        {
            float timeFromClipStart = Math.Max(0, time - provider.StartTime);
            provider.Seek(timeFromClipStart);
        }
        
        _clock.Restart();
        OnTimeChanged?.Invoke(CurrentTime);
    }
    
    public void UpdateTime()
    {
        if (_clock.IsRunning)
        {
            CurrentTime = _seekTime + (float)_clock.Elapsed.TotalSeconds;
            OnTimeChanged?.Invoke(CurrentTime);
        }
    }
    
    public bool IsPlaying => _clock.IsRunning;
}
