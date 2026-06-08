using System;
using AudioStudio.Models;

namespace AudioStudio.Commands
{
    public class MovePlaylistClipCommand : IAudioCommand
    {
        private readonly MainWindow _window;
        private readonly Guid _clipId;
        private readonly double _oldTick, _newTick;
        private readonly int _oldTrack, _newTrack;

        public string Description => "Переместить клип";

        public MovePlaylistClipCommand(MainWindow window, Guid clipId,
            double oldTick, int oldTrack, double newTick, int newTrack)
        {
            _window = window;
            _clipId = clipId;
            _oldTick = oldTick;
            _oldTrack = oldTrack;
            _newTick = newTick;
            _newTrack = newTrack;
        }

        public void Execute() =>
            _window.ApplyPlaylistClipLayout(_clipId, _newTick, _newTrack, null);

        public void Undo() =>
            _window.ApplyPlaylistClipLayout(_clipId, _oldTick, _oldTrack, null);
    }

    public class ResizePlaylistClipCommand : IAudioCommand
    {
        private readonly MainWindow _window;
        private readonly Guid _clipId;
        private readonly double _oldDuration, _newDuration;

        public string Description => "Изменить длину клипа";

        public ResizePlaylistClipCommand(MainWindow window, Guid clipId, double oldDuration, double newDuration)
        {
            _window = window;
            _clipId = clipId;
            _oldDuration = oldDuration;
            _newDuration = newDuration;
        }

        public void Execute() =>
            _window.ApplyPlaylistClipLayout(_clipId, null, null, _newDuration);

        public void Undo() =>
            _window.ApplyPlaylistClipLayout(_clipId, null, null, _oldDuration);
    }

    public class AddPlaylistClipCommand : IAudioCommand
    {
        private readonly MainWindow _window;
        private readonly TrackItemViewModel _clip;
        private readonly float[] _samples;

        public string Description => $"Добавить {_clip.Name}";

        public AddPlaylistClipCommand(MainWindow window, TrackItemViewModel clip, float[] samples)
        {
            _window = window;
            _clip = clip;
            _samples = samples;
        }

        public void Execute() => _window.InsertPlaylistClip(_clip, _samples);

        public void Undo() => _window.RemovePlaylistClipInternal(_clip.Id);
    }

    public class RemovePlaylistClipCommand : IAudioCommand
    {
        private readonly MainWindow _window;
        private readonly TrackItemViewModel _clipSnapshot;
        private readonly float[] _samples;
        private bool _removed;

        public string Description => $"Удалить {_clipSnapshot.Name}";

        public RemovePlaylistClipCommand(MainWindow window, TrackItemViewModel clip, float[] samples)
        {
            _window = window;
            _clipSnapshot = CloneClipMeta(clip);
            _samples = (float[])samples.Clone();
        }

        public void Execute()
        {
            if (!_removed)
            {
                _window.RemovePlaylistClipInternal(_clipSnapshot.Id);
                _removed = true;
            }
            else
            {
                _window.RemovePlaylistClipInternal(_clipSnapshot.Id);
            }
        }

        public void Undo()
        {
            _window.InsertPlaylistClip(_clipSnapshot, _samples);
            _removed = false;
        }

        private static TrackItemViewModel CloneClipMeta(TrackItemViewModel c) => new()
        {
            Id = c.Id,
            Name = c.Name,
            FilePath = c.FilePath,
            SampleRate = c.SampleRate,
            Channels = c.Channels,
            SourceDurationSeconds = c.SourceDurationSeconds,
            StartTick = c.StartTick,
            DurationTicks = c.DurationTicks,
            TrackIndex = c.TrackIndex,
            Color = c.Color
        };
    }

    public class SplicePlaylistSamplesCommand : IAudioCommand
    {
        private readonly MainWindow _window;
        private readonly Guid _clipId;
        private readonly int _startSample;
        private readonly float[] _removed;
        private readonly bool _saveToClipboard;

        public string Description => _saveToClipboard ? "Вырезать фрагмент" : "Удалить фрагмент";

        public SplicePlaylistSamplesCommand(MainWindow window, Guid clipId, int startSample,
            float[] removed, bool saveToClipboard)
        {
            _window = window;
            _clipId = clipId;
            _startSample = startSample;
            _removed = removed;
            _saveToClipboard = saveToClipboard;
        }

        public void Execute()
        {
            _window.SplicePlaylistSamplesInternal(_clipId, _startSample, _removed, _saveToClipboard);
        }

        public void Undo() =>
            _window.InsertPlaylistSamplesInternal(_clipId, _startSample, _removed);
    }

    public class PastePlaylistSamplesCommand : IAudioCommand
    {
        private readonly MainWindow _window;
        private readonly Guid _clipId;
        private readonly int _insertSample;
        private readonly float[] _data;

        public string Description => "Вставить фрагмент";

        public PastePlaylistSamplesCommand(MainWindow window, Guid clipId, int insertSample, float[] data)
        {
            _window = window;
            _clipId = clipId;
            _insertSample = insertSample;
            _data = data;
        }

        public void Execute() =>
            _window.InsertPlaylistSamplesInternal(_clipId, _insertSample, _data);

        public void Undo()
        {
            var removed = (float[])_data.Clone();
            _window.SplicePlaylistSamplesInternal(_clipId, _insertSample, removed, saveToClipboard: false);
        }
    }

    public class CutWholePlaylistClipCommand : IAudioCommand
    {
        private readonly MainWindow _window;
        private readonly TrackItemViewModel _clip;
        private readonly float[] _samples;
        private bool _removed;

        public string Description => $"Вырезать {_clip.Name}";

        public CutWholePlaylistClipCommand(MainWindow window, TrackItemViewModel clip, float[] samples)
        {
            _window = window;
            _clip = CloneMeta(clip);
            _samples = (float[])samples.Clone();
        }

        public void Execute()
        {
            if (!_removed)
            {
                _window.SetPlaylistClipboardWholeClip(_clip, _samples, wasCut: true);
                _window.RemovePlaylistClipInternal(_clip.Id);
                _removed = true;
            }
            else
            {
                _window.SetPlaylistClipboardWholeClip(_clip, _samples, wasCut: true);
                _window.RemovePlaylistClipInternal(_clip.Id);
            }
        }

        public void Undo()
        {
            _window.InsertPlaylistClip(_clip, _samples);
            _window.PlaylistClipboard.Clear();
            _removed = false;
        }

        private static TrackItemViewModel CloneMeta(TrackItemViewModel c) => new()
        {
            Id = c.Id,
            Name = c.Name,
            FilePath = c.FilePath,
            SampleRate = c.SampleRate,
            Channels = c.Channels,
            SourceDurationSeconds = c.SourceDurationSeconds,
            StartTick = c.StartTick,
            DurationTicks = c.DurationTicks,
            TrackIndex = c.TrackIndex,
            Color = c.Color
        };
    }

    public class PasteWholePlaylistClipCommand : IAudioCommand
    {
        private readonly MainWindow _window;
        private readonly TrackItemViewModel _clip;
        private readonly float[] _samples;
        private readonly bool _clearCutClipboard;
        private bool _added;

        public string Description => $"Вставить {_clip.Name}";

        public PasteWholePlaylistClipCommand(MainWindow window, TrackItemViewModel clip,
            float[] samples, bool clearCutClipboard)
        {
            _window = window;
            _clip = clip;
            _samples = samples;
            _clearCutClipboard = clearCutClipboard;
        }

        public void Execute()
        {
            if (!_added)
            {
                _window.InsertPlaylistClip(_clip, _samples);
                if (_clearCutClipboard) _window.PlaylistClipboard.Clear();
                _added = true;
            }
            else
            {
                _window.InsertPlaylistClip(_clip, _samples);
            }
        }

        public void Undo()
        {
            _window.RemovePlaylistClipInternal(_clip.Id);
            if (_clearCutClipboard)
                _window.SetPlaylistClipboardWholeClip(_clip, _samples, wasCut: true);
            _added = false;
        }
    }
}
