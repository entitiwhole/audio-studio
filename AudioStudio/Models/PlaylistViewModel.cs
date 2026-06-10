using System;
using System.Collections.ObjectModel;

namespace AudioStudio.Models
{
    public enum SnapDivision
    {
        None,
        Step1_6,
        Step1_4,
        Step1_3,
        Step1_2,
        Step1,
        Step2,
        Step4
    }

    public class PlaylistViewModel
    {
        public const int PPQN = 480;

        public ObservableCollection<TrackItemViewModel> AudioClips { get; } = new();
        public double ZoomX { get; set; } = 0.15;
        public double TrackHeight { get; set; } = 82;
        public int Bpm { get; set; } = 128;
        public int NumTracks { get; set; } = 4;
        public SnapDivision CurrentSnapDivision { get; set; } = SnapDivision.Step1_4;

        public double TotalHeight => NumTracks * TrackHeight;

        public double GetSnapStepTicks()
        {
            return CurrentSnapDivision switch
            {
                SnapDivision.None => 0,
                SnapDivision.Step1_6 => PPQN / 6.0,
                SnapDivision.Step1_4 => PPQN / 4.0,
                SnapDivision.Step1_3 => PPQN / 3.0,
                SnapDivision.Step1_2 => PPQN / 2.0,
                SnapDivision.Step1 => PPQN,
                SnapDivision.Step2 => PPQN * 2,
                SnapDivision.Step4 => PPQN * 4,
                _ => PPQN / 4.0
            };
        }

        public double GetGridMajorStepTicks()
        {
            double step = GetSnapStepTicks();
            if (step <= 0) return PPQN / 4.0;
            double stepPx = step * ZoomX;
            if (stepPx >= 40) return step;
            double target = 80.0 / ZoomX;
            double[] niceSteps = { PPQN / 4.0, PPQN / 2.0, PPQN, PPQN * 2, PPQN * 4, PPQN * 8 };
            double best = niceSteps[0];
            for (int i = 0; i < niceSteps.Length; i++)
            {
                if (niceSteps[i] >= target) { best = niceSteps[i]; break; }
                best = niceSteps[i];
            }
            return best;
        }

        public double GetGridMinorStepTicks()
        {
            return GetGridMajorStepTicks() / 5;
        }

        public double SnapToGrid(double tickPos)
        {
            double step = GetSnapStepTicks();
            if (step <= 0) return tickPos;
            return Math.Round(tickPos / step) * step;
        }

        public static bool Overlaps(double startA, double endA, double startB, double endB) =>
            startA < endB && endA > startB;

        public double FindFreeStartTick(int trackIndex, double durationTicks, double preferredTick = 0, Guid? excludeId = null)
        {
            preferredTick = Math.Max(0, SnapToGrid(preferredTick));
            var others = AudioClips
                .Where(c => c.TrackIndex == trackIndex && c.Id != excludeId)
                .OrderBy(c => c.StartTick)
                .ToList();

            if (!others.Any(c => Overlaps(preferredTick, preferredTick + durationTicks, c.StartTick, c.EndTick)))
                return preferredTick;

            double pos = preferredTick;
            bool changed;
            do
            {
                changed = false;
                foreach (var other in others)
                {
                    if (Overlaps(pos, pos + durationTicks, other.StartTick, other.EndTick))
                    {
                        pos = SnapToGrid(other.EndTick);
                        changed = true;
                    }
                }
            } while (changed);

            return pos;
        }

        public void ResolveClipPlacement(TrackItemViewModel clip, double preferredTick, int trackIndex)
        {
            clip.TrackIndex = Math.Clamp(trackIndex, 0, Math.Max(0, NumTracks - 1));
            clip.StartTick = FindFreeStartTick(clip.TrackIndex, clip.DurationTicks, preferredTick, clip.Id);
        }

        public double TicksPerSecond => Bpm * PPQN / 60.0;

        public double TickToSeconds(double tick) => tick / TicksPerSecond;

        public double SecondsToTick(double seconds) => seconds * TicksPerSecond;
    }
}
