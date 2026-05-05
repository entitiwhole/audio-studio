using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AudioStudio
{
    public partial class InstrumentsWindow : Window
    {
        // Событие для применения эффектов
        public event Action? ApplyRequested;
        
        // Mixer
        public float Volume { get; private set; } = 100f;
        public float Pan { get; private set; } = 0f;
        
        // Filters
        public bool LowPassEnabled { get; private set; }
        public float LowPassCutoff { get; private set; } = 5000f;
        
        public bool HighPassEnabled { get; private set; }
        public float HighPassCutoff { get; private set; } = 200f;
        
        // Effects
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
        
        private bool _isInitializing = true;
        private bool _effectsAvailable = true;
        
        // Helper методы для проверки доступности
        private bool CanApplyEffects() => _effectsAvailable;
        private void SafeApply() { if (_effectsAvailable) ApplyRequested?.Invoke(); }
        private void SafePreview() => PreviewRequested?.Invoke();

        public InstrumentsWindow()
        {
            InitializeComponent();
            _isInitializing = false;
            
            // Проверяем доступность NativeAudio при старте
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
                TrackInfoText.Text = "⚠ Effects unavailable (DLL missing)";
            }
        }
        
        public void LoadTrack(AudioClip track)
        {
            CurrentTrack = track;
            
            // Обновляем индикатор доступности эффектов
            string effectStatus = _effectsAvailable ? "Track:" : "⚠ Track (no effects):";
            TrackInfoText.Text = effectStatus + " " + track.Name;
            
            _isInitializing = true;
            VolumeSlider.Value = track.Volume * 100;
            PanSlider.Value = track.Pan * 100;
            _isInitializing = false;
            
            UpdatePanText();
        }
        
        private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            Volume = (float)VolumeSlider.Value;
            VolumeText.Text = Volume.ToString("F0") + "%";
            SafeApply();
        }
        
        private void PanChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            Pan = (float)PanSlider.Value;
            UpdatePanText();
            SafeApply();
        }
        
        private void UpdatePanText()
        {
            if (Math.Abs(Pan) < 1)
                PanText.Text = "CENTER";
            else if (Pan < 0)
                PanText.Text = "L " + Math.Abs(Pan).ToString("F0");
            else
                PanText.Text = "R " + Pan.ToString("F0");
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
        
        private void CancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        private void ApplyClick(object sender, RoutedEventArgs e)
        {
            ChangesApplied = true;
            // Не закрываем окно, просто применяем
            ApplyRequested?.Invoke();
        }

        // Событие для Preview (воспроизведение с эффектами)
        public event Action? PreviewRequested;
        
        // Window drag handling
        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        private void PreviewClick(object sender, RoutedEventArgs e)
        {
            SafePreview();
        }
    }

    // Disabled slider style converter (unused currently)
    // public class SliderToWidthConverter : IMultiValueConverter { ... }
}