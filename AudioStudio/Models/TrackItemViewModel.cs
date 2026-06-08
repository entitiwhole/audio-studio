using System;

namespace AudioStudio.Models
{
    public class TrackItemViewModel
    {
        public const int PPQN = 480;

        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Clip";
        public string? FilePath { get; set; }
        public int SampleRate { get; set; } = 44100;
        public int Channels { get; set; } = 2;
        public double SourceDurationSeconds { get; set; }
        public double StartTick { get; set; }
        public double DurationTicks { get; set; } = PPQN * 4;
        public int TrackIndex { get; set; }
        public string Color { get; set; } = "#FF7881FF";

        public double EndTick => StartTick + DurationTicks;
    }
}
