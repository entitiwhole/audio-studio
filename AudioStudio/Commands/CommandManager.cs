using System;
using System.Collections.Generic;

namespace AudioStudio.Commands
{
    public interface IAudioCommand
    {
        void Execute();
        void Undo();
        string Description { get; }
    }

    public class CommandManager
    {
        private readonly Stack<IAudioCommand> _undoStack = new();
        private readonly Stack<IAudioCommand> _redoStack = new();
        private const int MaxHistory = 50;

        public event Action? HistoryChanged;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        public string? LastUndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;
        public string? LastRedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;

        public void Execute(IAudioCommand command)
        {
            command.Execute();
            _undoStack.Push(command);
            _redoStack.Clear();

            // Limit history size
            while (_undoStack.Count > MaxHistory)
            {
                var tempList = new List<IAudioCommand>(_undoStack);
                tempList.RemoveAt(0);
                _undoStack.Clear();
                foreach (var cmd in tempList)
                    _undoStack.Push(cmd);
            }

            HistoryChanged?.Invoke();
        }

        public void Undo()
        {
            if (!CanUndo) return;
            
            var command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);
            
            HistoryChanged?.Invoke();
        }

        public void Redo()
        {
            if (!CanRedo) return;
            
            var command = _redoStack.Pop();
            command.Execute();
            _undoStack.Push(command);
            
            HistoryChanged?.Invoke();
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            HistoryChanged?.Invoke();
        }
    }
}
