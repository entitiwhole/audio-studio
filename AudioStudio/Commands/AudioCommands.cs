using System;
using System.IO;
using System.Windows;
using AudioStudio;

namespace AudioStudio.Commands
{
    /// <summary>
    /// Команда вырезания (Cut) - сохраняет только удаляемые данные, не все треки
    /// </summary>
    public class CutCommand : IAudioCommand
    {
        private readonly MainWindow _mainWindow;
        private readonly int _clipIndex;
        private float[] _removedData = Array.Empty<float>();
        private int _startSample;
        private int _length;
        private int _channels;
        private int _sampleRate;

        public string Description => "Вырезать";

        public CutCommand(MainWindow window, int clipIndex, int startSample, int length, int channels, int sampleRate)
        {
            _mainWindow = window;
            _clipIndex = clipIndex;
            _startSample = startSample;
            _length = length;
            _channels = channels;
            _sampleRate = sampleRate;
        }

        public void Execute()
        {
            var clip = _mainWindow.Tracks[_clipIndex];
            if (_length <= 0 || _startSample + _length > clip.Samples.Length) return;

            // Save removed data to clipboard
            _removedData = new float[_length];
            Array.Copy(clip.Samples, _startSample, _removedData, 0, _length);

            // Copy to window clipboard
            _mainWindow.ClipboardData = (float[])_removedData.Clone();
            _mainWindow.ClipboardChannels = _channels;
            _mainWindow.ClipboardSampleRate = _sampleRate;

            // Remove data from clip
            var newSamples = new float[clip.Samples.Length - _length];
            Array.Copy(clip.Samples, 0, newSamples, 0, _startSample);
            Array.Copy(clip.Samples, _startSample + _length, newSamples, _startSample, 
                clip.Samples.Length - _startSample - _length);
            
            // Crossfade 5ms на стыке чтобы избежать щелчка
            int fadeLen = (int)(0.005f * _sampleRate * _channels);
            if (fadeLen > 0 && _startSample >= fadeLen && _startSample + fadeLen < newSamples.Length)
            {
                for (int i = 0; i < fadeLen; i++)
                {
                    float gain = (float)i / fadeLen;
                    newSamples[_startSample - fadeLen + i] *= (1 - gain);
                    newSamples[_startSample + i] *= gain;
                }
            }
            
            clip.Samples = newSamples;
            
            _mainWindow.ClearSelection();
            _mainWindow.RebuildMixer();
            _mainWindow.DrawTimeline();
        }

        public void Undo()
        {
            var clip = _mainWindow.Tracks[_clipIndex];
            var newSamples = new float[clip.Samples.Length + _length];
            
            Array.Copy(clip.Samples, 0, newSamples, 0, _startSample);
            Array.Copy(_removedData, 0, newSamples, _startSample, _length);
            Array.Copy(clip.Samples, _startSample, newSamples, _startSample + _length, 
                clip.Samples.Length - _startSample);
            
            clip.Samples = newSamples;
            
            _mainWindow.RebuildMixer();
            _mainWindow.DrawTimeline();
        }
    }

    /// <summary>
    /// Команда вставки (Paste) - сохраняет только вставляемые данные
    /// </summary>
    public class PasteCommand : IAudioCommand
    {
        private readonly MainWindow _mainWindow;
        private readonly int _clipIndex;
        private readonly int _pasteSample;
        private readonly float[] _pasteData;
        private int _pastedLength;

        public string Description => "Вставить";

        public PasteCommand(MainWindow window, int clipIndex, int pasteSample, float[] data)
        {
            _mainWindow = window;
            _clipIndex = clipIndex;
            _pasteSample = pasteSample;
            _pasteData = (float[])data.Clone();
        }

        public void Execute()
        {
            var clip = _mainWindow.Tracks[_clipIndex];
            _pastedLength = _pasteData.Length;
            
            var newSamples = new float[clip.Samples.Length + _pastedLength];
            Array.Copy(clip.Samples, 0, newSamples, 0, _pasteSample);
            Array.Copy(_pasteData, 0, newSamples, _pasteSample, _pastedLength);
            Array.Copy(clip.Samples, _pasteSample, newSamples, _pasteSample + _pastedLength, 
                clip.Samples.Length - _pasteSample);
            
            clip.Samples = newSamples;
            
            _mainWindow.RebuildMixer();
            _mainWindow.DrawTimeline();
        }

        public void Undo()
        {
            var clip = _mainWindow.Tracks[_clipIndex];
            var newSamples = new float[clip.Samples.Length - _pastedLength];
            
            Array.Copy(clip.Samples, 0, newSamples, 0, _pasteSample);
            Array.Copy(clip.Samples, _pasteSample + _pastedLength, newSamples, _pasteSample, 
                clip.Samples.Length - _pasteSample - _pastedLength);
            
            clip.Samples = newSamples;
            
            _mainWindow.RebuildMixer();
            _mainWindow.DrawTimeline();
        }
    }

    /// <summary>
    /// Команда удаления (Delete) - сохраняет только удаляемые данные
    /// </summary>
    public class DeleteCommand : IAudioCommand
    {
        private readonly MainWindow _mainWindow;
        private readonly int _clipIndex;
        private float[] _removedData = Array.Empty<float>();
        private int _startSample;
        private int _length;

        public string Description => "Удалить";

        public DeleteCommand(MainWindow window, int clipIndex, int startSample, int length)
        {
            _mainWindow = window;
            _clipIndex = clipIndex;
            _startSample = startSample;
            _length = length;
        }

        public void Execute()
        {
            var clip = _mainWindow.Tracks[_clipIndex];
            if (_length <= 0) return;

            // Save removed data for undo
            _removedData = new float[_length];
            Array.Copy(clip.Samples, _startSample, _removedData, 0, _length);

            // Delete data
            var newSamples = new float[clip.Samples.Length - _length];
            Array.Copy(clip.Samples, 0, newSamples, 0, _startSample);
            Array.Copy(clip.Samples, _startSample + _length, newSamples, _startSample, 
                clip.Samples.Length - _startSample - _length);
            
            // Crossfade 5ms на стыке
            int sr = clip.SampleRate, ch = clip.Channels;
            int fadeLen = (int)(0.005f * sr * ch);
            if (fadeLen > 0 && _startSample >= fadeLen && _startSample + fadeLen < newSamples.Length)
            {
                for (int i = 0; i < fadeLen; i++)
                {
                    float gain = (float)i / fadeLen;
                    newSamples[_startSample - fadeLen + i] *= (1 - gain);
                    newSamples[_startSample + i] *= gain;
                }
            }
            
            clip.Samples = newSamples;
            
            _mainWindow.ClearSelection();
            _mainWindow.RebuildMixer();
            _mainWindow.DrawTimeline();
        }

        public void Undo()
        {
            var clip = _mainWindow.Tracks[_clipIndex];
            var newSamples = new float[clip.Samples.Length + _length];
            
            Array.Copy(clip.Samples, 0, newSamples, 0, _startSample);
            Array.Copy(_removedData, 0, newSamples, _startSample, _length);
            Array.Copy(clip.Samples, _startSample, newSamples, _startSample + _length, 
                clip.Samples.Length - _startSample);
            
            clip.Samples = newSamples;
            
            _mainWindow.RebuildMixer();
            _mainWindow.DrawTimeline();
        }
    }

    /// <summary>
    /// Команда загрузки файла на трек
    /// </summary>
    public class LoadFileCommand : IAudioCommand
    {
        private readonly MainWindow _mainWindow;
        private readonly string _filePath;
        private readonly int _trackIndex;
        private float[]? _previousSamples;
        private int _previousSampleRate;
        private int _previousChannels;
        private string? _previousSourceFile;
        private string? _previousName;
        private float[]? _newSamples;
        private int _newSampleRate;
        private int _newChannels;

        public string Description => $"Загрузить {Path.GetFileName(_filePath)}";

        public LoadFileCommand(MainWindow window, string filePath, int trackIndex)
        {
            _mainWindow = window;
            _filePath = filePath;
            _trackIndex = trackIndex;
        }

        public void Execute()
        {
            // Save current state for undo
            var track = _mainWindow.Tracks[_trackIndex];
            if (_previousSamples == null && track.Samples.Length > 0)
            {
                _previousSamples = (float[])track.Samples.Clone();
                _previousSampleRate = track.SampleRate;
                _previousChannels = track.Channels;
                _previousSourceFile = track.SourceFile;
                _previousName = track.Name;
            }

            // If redo and we have saved new state
            if (_newSamples != null)
            {
                track.Samples = (float[])_newSamples.Clone();
                track.SampleRate = _newSampleRate;
                track.Channels = _newChannels;
                _mainWindow.RebuildMixer();
                _mainWindow.DrawTimeline();
                return;
            }

            // First execute - load file
            _mainWindow.LoadFileToTrackSync(_filePath, _trackIndex);
            
            // Save new state for redo
            var newTrack = _mainWindow.Tracks[_trackIndex];
            _newSamples = (float[])newTrack.Samples.Clone();
            _newSampleRate = newTrack.SampleRate;
            _newChannels = newTrack.Channels;
        }

        public void Undo()
        {
            if (_previousSamples != null)
            {
                var track = _mainWindow.Tracks[_trackIndex];
                
                // Save current for redo
                _newSamples = (float[])track.Samples.Clone();
                _newSampleRate = track.SampleRate;
                _newChannels = track.Channels;
                
                // Restore previous
                track.Samples = (float[])_previousSamples.Clone();
                track.SampleRate = _previousSampleRate;
                track.Channels = _previousChannels;
                track.SourceFile = _previousSourceFile;
                track.Name = _previousName ?? "Клип";
                
                _mainWindow.RebuildMixer();
                _mainWindow.DrawTimeline();
            }
        }
        
        /// <summary>
        /// Set previous state from external capture (for async loading)
        /// </summary>
        public void SetPreviousState(float[]? samples, int sampleRate, int channels, string? sourceFile, string? name)
        {
            if (samples != null && samples.Length > 0)
            {
                _previousSamples = (float[])samples.Clone();
                _previousSampleRate = sampleRate;
                _previousChannels = channels;
                _previousSourceFile = sourceFile;
                _previousName = name;
            }
        }
    }

    /// <summary>
    /// Команда добавления трека
    /// </summary>
    public class AddTrackCommand : IAudioCommand
    {
        private readonly MainWindow _mainWindow;
        private AudioStudio.AudioClip? _addedTrack;

        public string Description => "Добавить трек";

        public AddTrackCommand(MainWindow window)
        {
            _mainWindow = window;
        }

        public void Execute()
        {
            _addedTrack = _mainWindow.CreateEmptyTrackInternal(_mainWindow.Tracks.Count);
            _mainWindow.TracksInternal.Add(_addedTrack);
            _mainWindow.DrawTimeline();
            _mainWindow.UpdateTrackLabels();
        }

        public void Undo()
        {
            if (_addedTrack != null)
            {
                _mainWindow.TracksInternal.Remove(_addedTrack);
                _addedTrack.Samples = Array.Empty<float>();
                _mainWindow.DrawTimeline();
                _mainWindow.UpdateTrackLabels();
            }
        }
    }

    /// <summary>
    /// Команда удаления трека
    /// </summary>
    public class RemoveTrackCommand : IAudioCommand
    {
        private readonly MainWindow _mainWindow;
        private readonly int _trackIndex;
        private AudioStudio.AudioClip? _removedTrack;
        private int _removedIndex;

        public string Description => "Удалить трек";

        public RemoveTrackCommand(MainWindow window, int trackIndex)
        {
            _mainWindow = window;
            _trackIndex = trackIndex;
        }

        public void Execute()
        {
            if (_trackIndex >= 0 && _trackIndex < _mainWindow.TracksInternal.Count)
            {
                _removedTrack = _mainWindow.TracksInternal[_trackIndex];
                _removedIndex = _trackIndex;
                _mainWindow.TracksInternal.RemoveAt(_trackIndex);
                _mainWindow.DrawTimeline();
                _mainWindow.UpdateTrackLabels();
            }
        }

        public void Undo()
        {
            if (_removedTrack != null && _removedIndex >= 0)
            {
                _mainWindow.TracksInternal.Insert(_removedIndex, _removedTrack);
                _mainWindow.DrawTimeline();
                _mainWindow.UpdateTrackLabels();
            }
        }
    }
}
