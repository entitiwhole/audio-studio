using System;

namespace AudioStudio.Models
{
    public class PlaylistClipboard
    {
        public enum ContentKind { None, Samples, WholeClip }

        public ContentKind Kind { get; set; } = ContentKind.None;
        public float[] Samples { get; set; } = Array.Empty<float>();
        public int SampleRate { get; set; } = 44100;
        public int Channels { get; set; } = 2;
        public string Name { get; set; } = "Clip";
        public string? FilePath { get; set; }
        public double DurationTicks { get; set; }
        public bool WasCut { get; set; }

        public bool HasContent => Kind != ContentKind.None && Samples.Length > 0;

        public void Clear()
        {
            Kind = ContentKind.None;
            Samples = Array.Empty<float>();
            WasCut = false;
        }
    }
}
