using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AudioStudio.ContextMenus
{
    public class AudioContextMenu
    {
        private readonly Popup _popup;
        private readonly StackPanel _panel;
        private MainWindow? _mainWindow;
        private bool _isOpen;

        public AudioContextMenu()
        {
            ResourceDictionary rd = new();
            try
            {
                rd.Source = new Uri("/ContextMenus/AudioContextMenu.xaml", UriKind.Relative);
            }
            catch { }

            var itemStyle = rd["MenuItemStyle"] as Style;
            var sepStyle = rd["MenuSepStyle"] as Style;

            _panel = new StackPanel { MinWidth = 180, Background = null };

            _panel.Children.Add(MakeItem("✂  Вырезать", "Cut", itemStyle));
            _panel.Children.Add(MakeItem("📄  Копировать", "Copy", itemStyle));
            _panel.Children.Add(MakeItem("📋  Вставить", "Paste", itemStyle));
            _panel.Children.Add(new Separator { Style = sepStyle });
            _panel.Children.Add(MakeItem("🗑  Удалить", "Delete", itemStyle));
            _panel.Children.Add(MakeItem("🧹  Очистить трек", "ClearTrack", itemStyle));
            _panel.Children.Add(new Separator { Style = sepStyle });
            _panel.Children.Add(MakeItem("Выделить всё", "SelectAll", itemStyle));
            _panel.Children.Add(MakeItem("Снять выделение", "ClearSelection", itemStyle));
            _panel.Children.Add(new Separator { Style = sepStyle });
            _panel.Children.Add(MakeItem("↶  Отменить", "Undo", itemStyle));
            _panel.Children.Add(MakeItem("↷  Повторить", "Redo", itemStyle));

            var border = new Border
            {
                Background = System.Windows.Media.Brushes.Transparent,
                Child = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(62, 62, 66)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Child = _panel
                }
            };

            _popup = new Popup
            {
                Child = border,
                Placement = PlacementMode.MousePoint,
                AllowsTransparency = true,
                StaysOpen = false,
                PopupAnimation = PopupAnimation.Fade
            };
        }

        private Button MakeItem(string text, string tag, Style? style)
        {
            var btn = new Button
            {
                Content = text,
                Tag = tag,
                Style = style
            };
            btn.Click += MenuItem_Click;
            return btn;
        }

        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                _isOpen = value;
                _popup.IsOpen = value;
            }
        }

        public void SetMainWindow(MainWindow window)
        {
            _mainWindow = window;
            UpdateMenuState();
        }

        public void UpdateMenuState()
        {
            if (_mainWindow == null) return;
            foreach (var child in _panel.Children)
            {
                if (child is Button btn)
                {
                    btn.IsEnabled = btn.Tag switch
                    {
                        "Cut" => _mainWindow.HasSelection(),
                        "Copy" => _mainWindow.HasSelection(),
                        "Paste" => _mainWindow.HasClipboard(),
                        "Delete" => _mainWindow.HasSelection(),
                        "ClearTrack" => true,
                        "Undo" => _mainWindow.CommandManager.CanUndo,
                        "Redo" => _mainWindow.CommandManager.CanRedo,
                        _ => true
                    };
                }
            }
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && _mainWindow != null)
            {
                _popup.IsOpen = false;
                _isOpen = false;
                switch (btn.Tag)
                {
                    case "Cut": _mainWindow.Cut_Click(btn, e); break;
                    case "Copy": _mainWindow.Copy_Click(btn, e); break;
                    case "Paste": _mainWindow.Paste_Click(btn, e); break;
                    case "Delete": _mainWindow.Delete_Click(btn, e); break;
                    case "ClearTrack": _mainWindow.ClearTrack_Click(btn, e); break;
                    case "SelectAll": _mainWindow.SelectAll(); break;
                    case "ClearSelection": _mainWindow.ClearSelection(); break;
                    case "Undo": _mainWindow.Undo_Click(btn, e); break;
                    case "Redo": _mainWindow.Redo_Click(btn, e); break;
                }
            }
        }
    }
}
