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
        public int NumTracks { get; set; } = 8;
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

        /// <summary>
        /// Находит позицию без пересечений на дорожке. Соприкосновение (End == Start) допустимо.
        /// </summary>
        public double FindFreeStartTick(int trackIndex, double durationTicks, double preferredTick = 0, Guid? excludeId = null, IEnumerable<Guid>? excludeIds = null)
        {
            preferredTick = Math.Max(0, SnapToGrid(preferredTick));
            durationTicks = Math.Max(0, durationTicks);

            var exclude = new HashSet<Guid>();
            if (excludeId.HasValue)
                exclude.Add(excludeId.Value);
            if (excludeIds != null)
            {
                foreach (var id in excludeIds)
                    exclude.Add(id);
            }

            var others = AudioClips
                .Where(c => c.TrackIndex == trackIndex && !exclude.Contains(c.Id))
                .OrderBy(c => c.StartTick)
                .ToList();

            if (!others.Any(o => Overlaps(preferredTick, preferredTick + durationTicks, o.StartTick, o.EndTick)))
                return preferredTick;

            double pos = preferredTick;
            double minStep = GetSnapStepTicks() > 0 ? GetSnapStepTicks() : 1;

            // Итеративно выталкиваем из пересечений; защита от зацикливания при SnapToGrid назад.
            for (int guard = 0; guard < others.Count + 16; guard++)
            {
                bool moved = false;
                foreach (var other in others)
                {
                    if (!Overlaps(pos, pos + durationTicks, other.StartTick, other.EndTick))
                        continue;

                    double before = pos;
                    double next = other.EndTick;
                    if (minStep > 0)
                    {
                        double snapped = SnapToGrid(next);
                        next = snapped >= next - 1e-9 ? snapped : next;
                    }

                    if (next <= before + 1e-9)
                        next = before + minStep;

                    pos = next;
                    moved = true;
                }

                if (!moved)
                    break;
            }

            return pos;
        }

        /// <summary>Максимальный EndTick клипа, если справа на дорожке уже есть другой клип.</summary>
        public double GetMaxAllowedEndTick(TrackItemViewModel clip)
        {
            var next = AudioClips
                .Where(c => c.TrackIndex == clip.TrackIndex && c.Id != clip.Id && c.StartTick > clip.StartTick + 1e-6)
                .OrderBy(c => c.StartTick)
                .FirstOrDefault();

            return next != null ? next.StartTick : double.MaxValue;
        }

        public void ClampClipDurationToTrack(TrackItemViewModel clip, double minDurationTicks = 0)
        {
            minDurationTicks = Math.Max(minDurationTicks, PPQN / 4.0);
            double maxEnd = GetMaxAllowedEndTick(clip);
            double maxDur = maxEnd - clip.StartTick;
            if (maxDur < minDurationTicks)
                clip.DurationTicks = minDurationTicks;
            else
                clip.DurationTicks = Math.Max(minDurationTicks, Math.Min(clip.DurationTicks, maxDur));
        }

        /// <summary>
        /// Позиция вставки: при append — после последнего клипа на дорожке; при drop на клип — в точку drop.
        /// </summary>
        public double GetPreferredInsertTick(
            int trackIndex,
            double requestedTick,
            double durationTicks,
            bool replaceAtDropPoint)
        {
            requestedTick = Math.Max(0, requestedTick);
            if (replaceAtDropPoint)
                return FindFreeStartTick(trackIndex, durationTicks, SnapToGrid(requestedTick));

            var lastOnTrack = AudioClips
                .Where(c => c.TrackIndex == trackIndex)
                .OrderByDescending(c => c.EndTick)
                .FirstOrDefault();

            if (lastOnTrack != null)
                return FindFreeStartTick(trackIndex, durationTicks, lastOnTrack.EndTick);

            return FindFreeStartTick(trackIndex, durationTicks, SnapToGrid(requestedTick));
        }

        public void ResolveClipPlacement(TrackItemViewModel clip, double preferredTick, int trackIndex, IEnumerable<Guid>? excludeIds = null)
        {
            clip.TrackIndex = Math.Clamp(trackIndex, 0, Math.Max(0, NumTracks - 1));
            clip.StartTick = FindFreeStartTick(clip.TrackIndex, clip.DurationTicks, preferredTick, clip.Id, excludeIds);
        }

        /// <summary>
        /// Ограничивает сдвиг группы клипов, чтобы во время перетаскивания они не заходили на другие клипы на дорожке.
        /// </summary>
        public double ClampGroupTickDelta(
            IReadOnlyList<(Guid Id, double StartTick, int StartTrack, double DurationTicks)> moving,
            double tickDelta,
            int trackDelta)
        {
            if (moving.Count == 0)
                return tickDelta;

            var movingIds = moving.Select(m => m.Id).ToHashSet();
            double minDelta = double.NegativeInfinity;
            double maxDelta = double.PositiveInfinity;

            foreach (var m in moving)
            {
                minDelta = Math.Max(minDelta, -m.StartTick);

                int targetTrack = Math.Clamp(m.StartTrack + trackDelta, 0, Math.Max(0, NumTracks - 1));

                foreach (var other in AudioClips.Where(c => c.TrackIndex == targetTrack && !movingIds.Contains(c.Id)))
                {
                    double rightLimit = other.StartTick - m.DurationTicks - m.StartTick;
                    double leftLimit = other.EndTick - m.StartTick;

                    if (m.StartTick + m.DurationTicks <= other.StartTick + 1e-9)
                        maxDelta = Math.Min(maxDelta, rightLimit);
                    else if (m.StartTick >= other.EndTick - 1e-9)
                        minDelta = Math.Max(minDelta, leftLimit);
                    else if (tickDelta >= 0)
                        maxDelta = Math.Min(maxDelta, rightLimit);
                    else
                        minDelta = Math.Max(minDelta, leftLimit);
                }
            }

            if (minDelta > maxDelta)
                return 0;

            return Math.Clamp(tickDelta, minDelta, maxDelta);
        }

        public void ApplyClipResize(TrackItemViewModel clip, double durationTicks, double minDurationTicks = 0)
        {
            clip.DurationTicks = durationTicks;
            ClampClipDurationToTrack(clip, minDurationTicks);
        }

        public double TicksPerSecond => Bpm * PPQN / 60.0;

        public double TickToSeconds(double tick) => tick / TicksPerSecond;

        public double SecondsToTick(double seconds) => seconds * TicksPerSecond;
    }
}
