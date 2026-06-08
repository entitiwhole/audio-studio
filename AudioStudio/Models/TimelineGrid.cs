using System;

namespace AudioStudio.Models
{
    /// <summary>
    /// Shared FL Studio–style adaptive time grid: minor/major/label steps in seconds.
    /// </summary>
    public readonly struct TimelineGridMetrics
    {
        public double MinorStepSeconds { get; init; }
        public double MajorStepSeconds { get; init; }
        public double LabelStepSeconds { get; init; }
        public double BarStepSeconds { get; init; }
        public int MinorPerMajor { get; init; }
        public int MinorPerLabel { get; init; }
        public int FractionDigits { get; init; }
    }

    public static class TimelineGrid
    {
        private static readonly double[] MinorCandidates =
        {
            0.001, 0.002, 0.005,
            0.01, 0.02, 0.025, 0.05,
            0.1, 0.125, 0.2, 0.25, 0.5,
            1, 2, 5, 10, 15, 30, 60
        };

        private static readonly int[] MajorMultiples = { 2, 4, 5, 8, 10 };

        public const double TargetMinorPx = 14;
        public const double TargetMajorPx = 56;
        public const double MinLabelPx = 54;

        public static TimelineGridMetrics Compute(double pixelsPerSecond, int bpm = 128)
        {
            if (pixelsPerSecond <= 1e-6)
                pixelsPerSecond = 1;

            double minorStep = PickAtLeast(MinorCandidates, TargetMinorPx / pixelsPerSecond);

            int minorPerMajor = 5;
            foreach (int n in MajorMultiples)
            {
                if (minorStep * n * pixelsPerSecond >= TargetMajorPx)
                {
                    minorPerMajor = n;
                    break;
                }
            }

            double majorStep = minorStep * minorPerMajor;

            int minorPerLabel = minorPerMajor;
            for (int n = MajorMultiples.Length - 1; n >= 0; n--)
            {
                int mult = MajorMultiples[n];
                double labelCandidate = minorStep * mult;
                if (labelCandidate * pixelsPerSecond >= MinLabelPx)
                {
                    minorPerLabel = mult;
                    break;
                }
            }

            // Prefer whole-minute labels when zoomed out
            if (pixelsPerSecond < 8 && minorStep <= 60)
            {
                double minutePx = 60 * pixelsPerSecond;
                if (minutePx >= MinLabelPx)
                {
                    minorPerLabel = Math.Max(minorPerLabel, (int)Math.Round(60 / minorStep));
                }
            }

            double labelStep = minorStep * minorPerLabel;
            double barStep = 240.0 / Math.Max(1, bpm); // 4 beats

            return new TimelineGridMetrics
            {
                MinorStepSeconds = minorStep,
                MajorStepSeconds = majorStep,
                LabelStepSeconds = labelStep,
                BarStepSeconds = barStep,
                MinorPerMajor = minorPerMajor,
                MinorPerLabel = minorPerLabel,
                FractionDigits = FractionDigitsForStep(labelStep)
            };
        }

        private static double PickAtLeast(double[] steps, double rough)
        {
            foreach (double s in steps)
            {
                if (s >= rough - 1e-12)
                    return s;
            }
            return steps[^1];
        }

        private static int FractionDigitsForStep(double labelStepSeconds)
        {
            if (labelStepSeconds >= 1) return 0;
            if (labelStepSeconds >= 0.1) return 1;
            if (labelStepSeconds >= 0.01) return 2;
            return 3;
        }

        public static int StartIndex(double startSeconds, double stepSeconds)
        {
            if (stepSeconds <= 0) return 0;
            return (int)Math.Floor(startSeconds / stepSeconds + 1e-9);
        }

        public static int EndIndex(double endSeconds, double stepSeconds)
        {
            if (stepSeconds <= 0) return 0;
            return (int)Math.Ceiling(endSeconds / stepSeconds - 1e-9);
        }

        public static string FormatLabel(double seconds, int fractionDigits)
        {
            if (seconds < 0) seconds = 0;
            long totalMs = (long)Math.Round(seconds * 1000);
            int ms = (int)(totalMs % 1000);
            long totalSec = totalMs / 1000;
            int sec = (int)(totalSec % 60);
            int min = (int)(totalSec / 60);

            return fractionDigits switch
            {
                0 => $"{min}:{sec:D2}",
                1 => $"{min}:{sec:D2}.{ms / 100}",
                2 => $"{min}:{sec:D2}.{ms / 10:D2}",
                _ => $"{min}:{sec:D2}.{ms:D3}"
            };
        }

        public static bool IsBarLine(int minorIndex, double minorStepSeconds, double barStepSeconds)
        {
            if (barStepSeconds <= 0 || minorStepSeconds <= 0) return false;
            int minorPerBar = (int)Math.Round(barStepSeconds / minorStepSeconds);
            if (minorPerBar <= 0) return false;
            return minorIndex % minorPerBar == 0;
        }
    }
}
