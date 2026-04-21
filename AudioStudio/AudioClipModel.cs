namespace AudioStudio;

public class AudioClipModel
{
    private float[] _samples = Array.Empty<float>();
    
    public float[] Samples 
    { 
        get => _samples;
        set => _samples = value ?? Array.Empty<float>();
    }
    
    public float StartTime { get; set; } = 0f;   // позиция на таймлайне (сек)
    public float Duration { get; set; }           // длительность
    
    public int SampleRate { get; set; } = 44100;
    public int Channels { get; set; } = 2;
    
    public string Name { get; set; } = "";
    
    // Настройки клипа
    public float Volume { get; set; } = 1.0f;
    public float Pan { get; set; } = 0.0f;  // -1.0 (L) to 1.0 (R)
    
    // Визуальные настройки
    public int TrackIndex { get; set; } = 0;
    public string Color { get; set; } = "#FF7881FF";
    
    public float EndTime => StartTime + Duration;
    
    // Конструктор который делает копию Samples
    public AudioClipModel()
    {
    }
    
    public AudioClipModel Clone()
    {
        return new AudioClipModel
        {
            Samples = (float[])_samples.Clone(),
            StartTime = StartTime,
            Duration = Duration,
            SampleRate = SampleRate,
            Channels = Channels,
            Name = Name,
            Volume = Volume,
            Pan = Pan,
            TrackIndex = TrackIndex,
            Color = Color
        };
    }
}
