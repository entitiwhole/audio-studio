namespace AudioStudio.Models;

public class StudioProject
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Untitled";
    public int Bpm { get; set; } = 128;
    public int NumTracks { get; set; } = 4;
    public List<StudioProjectClip> Clips { get; set; } = new();
}

public class StudioProjectClip
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "Clip";
    public string FilePath { get; set; } = "";
    public double StartTick { get; set; }
    public double DurationTicks { get; set; }
    public int TrackIndex { get; set; }
    public int SampleRate { get; set; } = 44100;
    public int Channels { get; set; } = 2;
    public double SourceDurationSeconds { get; set; }
}
