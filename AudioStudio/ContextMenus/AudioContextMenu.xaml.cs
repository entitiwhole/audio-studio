using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AudioStudio.ContextMenus
{
    public class AudioContextMenu
    {
        private readonly Popup _popup;
        private readonly StackPanel _panel;
        private readonly TextBlock _hintsHeader;
        private readonly TextBlock _emptyTrackHeader;
        private MainWindow? _mainWindow;
        private bool _isOpen;

        private const string ClipHints = "Ctrl+ЛКМ — выделить область на записи";
        private const string EmptyTrackHints = "Вставить вырезанную или скопированную запись";

        public AudioContextMenu()
        {
            ResourceDictionary rd = new();
            try
            {
                rd.Source = new Uri("/ContextMenus/AudioContextMenu.xaml", UriKind.Relative);
            }
            catch { }

            var itemStyle = rd["ContextMenuItemStyle"] as Style;
            var sepStyle = rd["ContextMenuSepStyle"] as Style;
            var hintStyle = rd["ContextMenuHintStyle"] as Style;

            _panel = new StackPanel { MinWidth = 200, Background = null };

            _hintsHeader = new TextBlock
            {
                Style = hintStyle,
                Visibility = Visibility.Collapsed
            };
            _panel.Children.Add(_hintsHeader);

            _emptyTrackHeader = new TextBlock
            {
                Style = hintStyle,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 129, 255)),
                Visibility = Visibility.Collapsed
            };
            _panel.Children.Add(_emptyTrackHeader);

            _panel.Children.Add(CreateItem("Вырезать", "Cut", "Ctrl+X", itemStyle));
            _panel.Children.Add(CreateItem("Копировать", "Copy", "Ctrl+C", itemStyle));
            _panel.Children.Add(CreateItem("Вставить", "Paste", "Ctrl+V", itemStyle));
            _panel.Children.Add(new Separator { Style = sepStyle });
            _panel.Children.Add(CreateItem("Удалить", "Delete", "Del", itemStyle));
            _panel.Children.Add(CreateItem("Очистить трек", "ClearTrack", null, itemStyle));
            _panel.Children.Add(new Separator { Style = sepStyle });
            _panel.Children.Add(CreateItem("Выделить всё", "SelectAll", "Ctrl+D", itemStyle));
            _panel.Children.Add(CreateItem("Снять выделение", "ClearSelection", null, itemStyle));
            _panel.Children.Add(new Separator { Style = sepStyle });
            _panel.Children.Add(CreateItem("Отменить", "Undo", "Ctrl+Z", itemStyle));
            _panel.Children.Add(CreateItem("Повторить", "Redo", "Ctrl+Y", itemStyle));

            var border = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(2),
                Child = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(0, 4, 0, 4),
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

        private Button CreateItem(string text, string tag, string? hotkey, Style? style)
        {
            var grid = new Grid { Margin = new Thickness(0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (hotkey != null)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            if (hotkey != null)
            {
                var hk = new TextBlock
                {
                    Text = hotkey,
                    Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 130)),
                    FontSize = 10,
                    Margin = new Thickness(16, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(hk, 1);
                grid.Children.Add(hk);
            }

            var btn = new Button
            {
                Content = grid,
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

        public void SetClipHintsVisible(bool visible)
        {
            _hintsHeader.Text = ClipHints;
            _hintsHeader.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetEmptyTrackMode(bool visible)
        {
            _emptyTrackHeader.Text = EmptyTrackHints;
            _emptyTrackHeader.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
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
                        "Paste" => _mainWindow.HasClipboard(), // always when clipboard has data
                        "Delete" => _mainWindow.HasSelection()
                            || _mainWindow.HasSelectedPlaylistClip(),
                        "ClearTrack" => _mainWindow.HasSelectedPlaylistClip()
                            || _mainWindow.FocusedClipIndex >= 0,
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
