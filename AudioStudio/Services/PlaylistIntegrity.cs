using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AudioStudio.Models;

namespace AudioStudio.Services
{
    /// <summary>
    /// Проверки и нормализация состояния плейлиста (защита от «дыр» в данных).
    /// </summary>
    public static class PlaylistIntegrity
    {
        public static float[] TrimToDuration(float[] samples, TrackItemViewModel clip, PlaylistViewModel model)
        {
            if (samples.Length == 0) return samples;

            int frameRate = clip.SampleRate * Math.Max(1, clip.Channels);
            if (frameRate <= 0) return samples;

            int maxFrames = (int)Math.Round(model.TickToSeconds(clip.DurationTicks) * frameRate);
            maxFrames = Math.Clamp(maxFrames, 0, samples.Length);
            if (maxFrames == samples.Length) return samples;

            var trimmed = new float[maxFrames];
            Array.Copy(samples, trimmed, maxFrames);
            return trimmed;
        }

        public static bool HasPlayableAudio(TrackItemViewModel clip, float[] samples) =>
            !string.IsNullOrEmpty(clip.FilePath) && samples.Length > 0;

        public static bool IsClipFileAccessible(TrackItemViewModel clip) =>
            !string.IsNullOrEmpty(clip.FilePath) && File.Exists(clip.FilePath);

        public static IEnumerable<TrackItemViewModel> GetClipsContainingPoint(
            PlaylistViewModel model,
            int trackIndex,
            double tick,
            Guid? excludeId = null) =>
            model.AudioClips.Where(c =>
                c.TrackIndex == trackIndex
                && c.Id != excludeId
                && tick >= c.StartTick - 1e-6
                && tick < c.EndTick - 1e-6);

        public static IEnumerable<TrackItemViewModel> GetOverlappingClips(
            PlaylistViewModel model,
            TrackItemViewModel clip,
            Guid? excludeId = null) =>
            GetOverlappingClips(model, clip.TrackIndex, clip.StartTick, clip.EndTick, excludeId);

        public static IEnumerable<TrackItemViewModel> GetOverlappingClips(
            PlaylistViewModel model,
            int trackIndex,
            double startTick,
            double endTick,
            Guid? excludeId = null) =>
            model.AudioClips.Where(c =>
                c.TrackIndex == trackIndex
                && c.Id != excludeId
                && PlaylistViewModel.Overlaps(startTick, endTick, c.StartTick, c.EndTick));

        public static void NormalizeClipBounds(TrackItemViewModel clip, PlaylistViewModel model)
        {
            clip.TrackIndex = Math.Clamp(clip.TrackIndex, 0, Math.Max(0, model.NumTracks - 1));
            clip.StartTick = Math.Max(0, clip.StartTick);
            clip.DurationTicks = Math.Max(TrackItemViewModel.PPQN / 4.0, clip.DurationTicks);
        }

        /// <summary>Выставляет DurationTicks из длины сэмплов (единый источник при insert/load).</summary>
        public static void EnsureDurationFromSamples(
            TrackItemViewModel clip,
            float[] samples,
            PlaylistViewModel model)
        {
            if (samples.Length == 0) return;

            int denom = clip.SampleRate * Math.Max(1, clip.Channels);
            if (denom <= 0) return;

            double sec = samples.Length / (double)denom;
            clip.SourceDurationSeconds = sec;
            clip.DurationTicks = Math.Max(
                TrackItemViewModel.PPQN / 4.0,
                model.SecondsToTick(sec));
        }
    }

}
