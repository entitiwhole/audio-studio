using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;

namespace AudioStudio;

public class AudioEngine
{
    private WasapiOut? _waveOut;
    private MixingSampleProvider? _mixer;
    
    // Отдельный провайдер для каждого клипа
    private readonly List<ClipSampleProvider> _providers = new();
    
    // Мастер-формат для всех треков
    private readonly int _masterSampleRate = 44100;
    private readonly int _masterChannels = 2;
    
    // Источник истины для времени
    public double CurrentTime { get; private set; }
    
    private readonly Stopwatch _clock = new();
    private float _seekTime;
    private bool _manualStop;
    
    public event Action? OnPlaybackStopped;
        public event Action<double>? OnTimeChanged;

    public void LoadClips(List<AudioClipModel> clips)
    {
        Stop();

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

        _waveOut = new WasapiOut(AudioClientShareMode.Shared, 50);
        _waveOut.Init(_mixer);
        
        _waveOut.PlaybackStopped += OnWaveOutPlaybackStopped;
    }

    private void OnWaveOutPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_manualStop)
        {
            _manualStop = false;
            return;
        }

        _clock.Stop();
        OnPlaybackStopped?.Invoke();
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
        if (_waveOut == null) return;
        _waveOut.Play();
        if (_clock.IsRunning)
            _clock.Restart();
        else
            _clock.Start();
    }

    public void Pause()
    {
        _waveOut?.Pause();
        _clock.Stop();
    }

    public void Stop()
    {
        _manualStop = true;
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

        if (_clock.IsRunning)
            _clock.Restart();
        else
            _clock.Reset();

        OnTimeChanged?.Invoke(CurrentTime);
    }
    
    public void UpdateTime()
    {
        if (_clock.IsRunning)
        {
            CurrentTime = _seekTime + _clock.Elapsed.TotalSeconds;
            OnTimeChanged?.Invoke(CurrentTime);
        }
    }
    
    public bool IsPlaying => _clock.IsRunning;
    public bool IsLooping { get; set; }

    public void SetLoopMode(bool loop)
    {
        IsLooping = loop;
        foreach (var provider in _providers)
        {
            provider.IsLooping = loop;
        }
    }
    
    // Preview - играет одиночные сэмплы (для Instruments window)
    public void PlayPreview(float[] samples, int sampleRate, int channels)
    {
        Stop();
        
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        var provider = new ClipSampleProvider(samples, sampleRate, channels, 0);
        
        ISampleProvider formattedProvider = provider;
        
        // Ресемплинг если нужно
        if (sampleRate != _masterSampleRate)
        {
            formattedProvider = new WdlResamplingSampleProvider(formattedProvider, _masterSampleRate);
        }
        
        // Приведение каналов
        if (channels == 1 && _masterChannels == 2)
        {
            formattedProvider = new MonoToStereoSampleProvider(formattedProvider);
        }
        
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(_masterSampleRate, _masterChannels));
        _mixer.AddMixerInput(formattedProvider);
        
        _waveOut = new WasapiOut(AudioClientShareMode.Shared, 50);
        _waveOut.Init(_mixer);
        
        _waveOut.PlaybackStopped += OnWaveOutPlaybackStopped;
        
        _waveOut.Play();
        _clock.Start();
    }
}
