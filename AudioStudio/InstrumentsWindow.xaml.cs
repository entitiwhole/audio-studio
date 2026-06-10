using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AudioStudio
{
    public partial class InstrumentsWindow : Window
    {
        public event Action? ApplyRequested;
        public event Action? PreviewRequested;

        public float Volume { get; private set; } = 100f;
        public float Pan { get; private set; } = 0f;

        public bool LowPassEnabled { get; private set; }
        public float LowPassCutoff { get; private set; } = 5000f;

        public bool HighPassEnabled { get; private set; }
        public float HighPassCutoff { get; private set; } = 200f;

        public bool GainEnabled { get; private set; }
        public float GainDb { get; private set; } = 0f;

        public bool EchoEnabled { get; private set; }
        public float EchoDelay { get; private set; } = 300f;
        public float EchoFeedback { get; private set; } = 30f;
        public float EchoMix { get; private set; } = 30f;

        public bool ReverbEnabled { get; private set; }
        public float ReverbWet { get; private set; } = 30f;
        public float ReverbRoom { get; private set; } = 50f;

        public bool ChangesApplied { get; private set; }
        public AudioClip? CurrentTrack { get; private set; }

        private bool _isInitializing;
        private bool _effectsAvailable = true;
        private bool _suppressPresetChange;
        private Button? _activeTab;

        public InstrumentsWindow()
        {
            _isInitializing = true;
            _suppressPresetChange = true;
            InitializeComponent();
            _activeTab = TabMixer;
            SelectTab(TabMixer, "mixer");
            PresetCombo.SelectedIndex = 0;
            _suppressPresetChange = false;
            _isInitializing = false;

            try
            {
                var fx = NativeAudio.CreateEffectChain(44100, 2);
                if (fx != IntPtr.Zero)
                    NativeAudio.DeleteEffectChain(fx);
                _effectsAvailable = true;
            }
            catch
            {
                _effectsAvailable = false;
                EffectsStatusText.Text = "FX недоступны";
                EffectsStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 120, 100));
            }
        }

        public void LoadTrack(AudioClip track)
        {
            CurrentTrack = track;
            UpdateTrackInfo(track);

            _isInitializing = true;
            VolumeSlider.Value = track.Volume * 100;
            PanSlider.Value = track.Pan * 100;
            _isInitializing = false;

            Volume = (float)VolumeSlider.Value;
            UpdatePanText();
            UpdateVolumeText();
        }

        private void UpdateTrackInfo(AudioClip track)
        {
            TrackNameText.Text = track.Name;
            string duration = FormatDuration(track.Duration);
            TrackMetaText.Text = $"Дорожка {track.TrackIndex + 1}  •  {duration}  •  {track.SampleRate} Hz";
            EffectsStatusText.Text = _effectsAvailable ? "FX готовы" : "DLL не найден";
            EffectsStatusText.Foreground = new SolidColorBrush(
                _effectsAvailable ? Color.FromRgb(120, 129, 255) : Color.FromRgb(220, 120, 100));
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int h = (int)(seconds / 3600);
            int m = (int)((seconds % 3600) / 60);
            int s = (int)(seconds % 60);
            return h > 0 ? $"{h:D2}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
        }

        private void SafeApply()
        {
            if (!_effectsAvailable) return;
            if (LivePreviewCheck.IsChecked == true)
                ApplyRequested?.Invoke();
        }

        private void SafePreview() => PreviewRequested?.Invoke();

        private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            Volume = (float)VolumeSlider.Value;
            UpdateVolumeText();
            SafeApply();
        }

        private void PanChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            Pan = (float)PanSlider.Value;
            UpdatePanText();
            SafeApply();
        }

        private void UpdateVolumeText()
        {
            if (VolumeText == null) return;
            VolumeText.Text = Volume.ToString("F0") + "%";
        }

        private void UpdatePanText()
        {
            if (PanText == null) return;
            if (Math.Abs(Pan) < 1)
                PanText.Text = "ЦЕНТР";
            else if (Pan < 0)
                PanText.Text = "Л " + Math.Abs(Pan).ToString("F0");
            else
                PanText.Text = "П " + Pan.ToString("F0");
        }

        private void LowPassClick(object sender, RoutedEventArgs e)
        {
            LowPassEnabled = LowPassCheck.IsChecked == true;
            LowPassSlider.IsEnabled = LowPassEnabled;
            SafeApply();
        }

        private void LowPassChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            LowPassCutoff = (float)LowPassSlider.Value;
            LowPassText.Text = LowPassCutoff.ToString("F0") + " Hz";
            SafeApply();
        }

        private void HighPassClick(object sender, RoutedEventArgs e)
        {
            HighPassEnabled = HighPassCheck.IsChecked == true;
            HighPassSlider.IsEnabled = HighPassEnabled;
            SafeApply();
        }

        private void HighPassChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            HighPassCutoff = (float)HighPassSlider.Value;
            HighPassText.Text = HighPassCutoff.ToString("F0") + " Hz";
            SafeApply();
        }

        private void GainClick(object sender, RoutedEventArgs e)
        {
            GainEnabled = GainCheck.IsChecked == true;
            GainSlider.IsEnabled = GainEnabled;
            SafeApply();
        }

        private void GainChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            GainDb = (float)GainSlider.Value;
            GainText.Text = GainDb.ToString("F1") + " dB";
            SafeApply();
        }

        private void EchoClick(object sender, RoutedEventArgs e)
        {
            EchoEnabled = EchoCheck.IsChecked == true;
            EchoPanel.Visibility = EchoEnabled ? Visibility.Visible : Visibility.Collapsed;
            SafeApply();
        }

        private void EchoChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            EchoDelay = (float)EchoDelaySlider.Value;
            EchoFeedback = (float)EchoFeedbackSlider.Value;
            EchoMix = (float)EchoMixSlider.Value;
            EchoDelayText.Text = EchoDelay.ToString("F0") + " ms";
            EchoFeedbackText.Text = EchoFeedback.ToString("F0") + "%";
            EchoMixText.Text = EchoMix.ToString("F0") + "%";
            SafeApply();
        }

        private void ReverbClick(object sender, RoutedEventArgs e)
        {
            ReverbEnabled = ReverbCheck.IsChecked == true;
            ReverbPanel.Visibility = ReverbEnabled ? Visibility.Visible : Visibility.Collapsed;
            SafeApply();
        }

        private void ReverbChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            ReverbWet = (float)ReverbWetSlider.Value;
            ReverbRoom = (float)ReverbRoomSlider.Value;
            ReverbWetText.Text = ReverbWet.ToString("F0") + "%";
            ReverbRoomText.Text = ReverbRoom.ToString("F0") + "%";
            SafeApply();
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag) return;
            SelectTab(btn, tag);
        }

        private void SelectTab(Button tab, string tag)
        {
            _activeTab = tab;
            UpdateTabVisuals();

            PanelMixer.Visibility = tag == "mixer" ? Visibility.Visible : Visibility.Collapsed;
            PanelFilters.Visibility = tag == "filters" ? Visibility.Visible : Visibility.Collapsed;
            PanelEffects.Visibility = tag == "effects" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateTabVisuals()
        {
            foreach (var tab in new[] { TabMixer, TabFilters, TabEffects })
            {
                bool active = tab == _activeTab;
                tab.Foreground = new SolidColorBrush(active ? Color.FromRgb(232, 232, 236) : Color.FromRgb(136, 136, 136));
                tab.Background = active
                    ? new SolidColorBrush(Color.FromRgb(45, 45, 48))
                    : Brushes.Transparent;
                tab.BorderBrush = active
                    ? new SolidColorBrush(Color.FromRgb(120, 129, 255))
                    : Brushes.Transparent;
            }
        }

        private void ResetMixer_Click(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            VolumeSlider.Value = 100;
            PanSlider.Value = 0;
            _isInitializing = false;
            Volume = 100;
            Pan = 0;
            UpdateVolumeText();
            UpdatePanText();
            SafeApply();
        }

        private void ResetFilters_Click(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            LowPassCheck.IsChecked = false;
            HighPassCheck.IsChecked = false;
            LowPassSlider.Value = 5000;
            HighPassSlider.Value = 200;
            LowPassSlider.IsEnabled = false;
            HighPassSlider.IsEnabled = false;
            _isInitializing = false;
            LowPassEnabled = false;
            HighPassEnabled = false;
            LowPassCutoff = 5000;
            HighPassCutoff = 200;
            LowPassText.Text = "5000 Hz";
            HighPassText.Text = "200 Hz";
            SafeApply();
        }

        private void ResetEffects_Click(object sender, RoutedEventArgs e) => ApplyEffectsPreset(cleanOnly: true);

        private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressPresetChange || _isInitializing) return;
            if (PresetCombo.SelectedItem is not ComboBoxItem item) return;
            ApplyNamedPreset(item.Content?.ToString() ?? "Чистый");
        }

        private void ApplyNamedPreset(string name)
        {
            switch (name)
            {
                case "Чистый":
                    ResetMixer_Click(this, new RoutedEventArgs());
                    ResetFilters_Click(this, new RoutedEventArgs());
                    ApplyEffectsPreset(cleanOnly: true);
                    break;
                case "Вокал":
                    ResetFilters_Click(this, new RoutedEventArgs());
                    ApplyEffectsPreset(cleanOnly: true);
                    _isInitializing = true;
                    HighPassCheck.IsChecked = true;
                    HighPassSlider.Value = 120;
                    HighPassSlider.IsEnabled = true;
                    GainCheck.IsChecked = true;
                    GainSlider.Value = 2;
                    GainSlider.IsEnabled = true;
                    ReverbCheck.IsChecked = true;
                    ReverbWetSlider.Value = 18;
                    ReverbRoomSlider.Value = 35;
                    ReverbPanel.Visibility = Visibility.Visible;
                    _isInitializing = false;
                    SyncEffectStateFromUi();
                    SelectTab(TabEffects, "effects");
                    break;
                case "Бас":
                    ResetFilters_Click(this, new RoutedEventArgs());
                    ApplyEffectsPreset(cleanOnly: true);
                    _isInitializing = true;
                    LowPassCheck.IsChecked = true;
                    LowPassSlider.Value = 9000;
                    LowPassSlider.IsEnabled = true;
                    GainCheck.IsChecked = true;
                    GainSlider.Value = 3;
                    GainSlider.IsEnabled = true;
                    _isInitializing = false;
                    SyncEffectStateFromUi();
                    SelectTab(TabFilters, "filters");
                    break;
                case "Пространство":
                    ApplyEffectsPreset(cleanOnly: true);
                    _isInitializing = true;
                    EchoCheck.IsChecked = true;
                    EchoDelaySlider.Value = 280;
                    EchoFeedbackSlider.Value = 25;
                    EchoMixSlider.Value = 22;
                    EchoPanel.Visibility = Visibility.Visible;
                    ReverbCheck.IsChecked = true;
                    ReverbWetSlider.Value = 45;
                    ReverbRoomSlider.Value = 65;
                    ReverbPanel.Visibility = Visibility.Visible;
                    _isInitializing = false;
                    SyncEffectStateFromUi();
                    SelectTab(TabEffects, "effects");
                    break;
            }
            SafeApply();
        }

        private void ApplyEffectsPreset(bool cleanOnly)
        {
            _isInitializing = true;
            GainCheck.IsChecked = false;
            GainSlider.Value = 0;
            GainSlider.IsEnabled = false;
            EchoCheck.IsChecked = false;
            EchoPanel.Visibility = Visibility.Collapsed;
            EchoDelaySlider.Value = 300;
            EchoFeedbackSlider.Value = 30;
            EchoMixSlider.Value = 30;
            ReverbCheck.IsChecked = false;
            ReverbPanel.Visibility = Visibility.Collapsed;
            ReverbWetSlider.Value = 30;
            ReverbRoomSlider.Value = 50;
            _isInitializing = false;
            SyncEffectStateFromUi();
            if (!cleanOnly) return;
            GainText.Text = "0 dB";
            EchoDelayText.Text = "300 ms";
            EchoFeedbackText.Text = "30%";
            EchoMixText.Text = "30%";
            ReverbWetText.Text = "30%";
            ReverbRoomText.Text = "50%";
        }

        private void SyncEffectStateFromUi()
        {
            GainEnabled = GainCheck.IsChecked == true;
            EchoEnabled = EchoCheck.IsChecked == true;
            ReverbEnabled = ReverbCheck.IsChecked == true;
            LowPassEnabled = LowPassCheck.IsChecked == true;
            HighPassEnabled = HighPassCheck.IsChecked == true;
            GainDb = (float)GainSlider.Value;
            LowPassCutoff = (float)LowPassSlider.Value;
            HighPassCutoff = (float)HighPassSlider.Value;
            EchoDelay = (float)EchoDelaySlider.Value;
            EchoFeedback = (float)EchoFeedbackSlider.Value;
            EchoMix = (float)EchoMixSlider.Value;
            ReverbWet = (float)ReverbWetSlider.Value;
            ReverbRoom = (float)ReverbRoomSlider.Value;
            GainText.Text = GainDb.ToString("F1") + " dB";
            LowPassText.Text = LowPassCutoff.ToString("F0") + " Hz";
            HighPassText.Text = HighPassCutoff.ToString("F0") + " Hz";
            EchoDelayText.Text = EchoDelay.ToString("F0") + " ms";
            EchoFeedbackText.Text = EchoFeedback.ToString("F0") + "%";
            EchoMixText.Text = EchoMix.ToString("F0") + "%";
            ReverbWetText.Text = ReverbWet.ToString("F0") + "%";
            ReverbRoomText.Text = ReverbRoom.ToString("F0") + "%";
        }

        private void CancelClick(object sender, RoutedEventArgs e) => Close();

        private void ApplyClick(object sender, RoutedEventArgs e)
        {
            ChangesApplied = true;
            ApplyRequested?.Invoke();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            if (WindowState == WindowState.Maximized) return;
            DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void PreviewClick(object sender, RoutedEventArgs e) => SafePreview();
    }
}
