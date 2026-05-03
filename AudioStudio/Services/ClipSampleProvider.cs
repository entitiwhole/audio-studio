using NAudio.Wave;

namespace AudioStudio;

/// <summary>
/// Провайдер для воспроизведения одного клипа.
/// Каждый клип имеет свой независимый буфер и позицию.
/// </summary>
public class ClipSampleProvider : ISampleProvider
{
    private readonly float[] _samples;
    private int _position;
    
    public WaveFormat WaveFormat { get; }
    
    /// <summary>
    /// Время старта клипа на таймлайне (секунды)
    /// </summary>
    public float StartTime { get; set; }
    
    public ClipSampleProvider(float[] samples, int sampleRate, int channels, float startTime = 0)
    {
        // ВАЖНО: создаём копию массива
        _samples = samples.ToArray();
        StartTime = startTime;
        
        // ВСЕГДА используем IeeeFloat формат для NAudio
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }
    
    public int Read(float[] buffer, int offset, int count)
    {
        if (_position >= _samples.Length)
        {
            return 0;
        }
        
        int available = _samples.Length - _position;
        int toCopy = Math.Min(count, available);
        
        Array.Copy(_samples, _position, buffer, offset, toCopy);
        _position += toCopy;
        
        return toCopy;
    }
    
    public void Seek(float timeFromStart)
    {
        int samplePosition = (int)(timeFromStart * WaveFormat.SampleRate * WaveFormat.Channels);
        _position = Math.Clamp(samplePosition, 0, _samples.Length);
    }
    
    public void Reset()
    {
        _position = 0;
    }
    
    public int Position => _position;
    public int Length => _samples.Length;
}
