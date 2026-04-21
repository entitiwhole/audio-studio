using System;
using System.Windows;
using System.Windows.Controls;

namespace AudioStudio
{
    public partial class InstrumentsWindow : Window
    {
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
        
        public InstrumentsWindow()
        {
            InitializeComponent();
            _isInitializing = false;
        }
        
        public void LoadTrack(AudioClip track)
        {
            CurrentTrack = track;
            TrackInfoText.Text = "Track: " + track.Name;
            
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
        }
        
        private void PanChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            Pan = (float)PanSlider.Value;
            UpdatePanText();
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
        }
        
        private void LowPassChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            LowPassCutoff = (float)LowPassSlider.Value;
            LowPassText.Text = LowPassCutoff.ToString("F0") + " Hz";
        }
        
        private void HighPassClick(object sender, RoutedEventArgs e)
        {
            HighPassEnabled = HighPassCheck.IsChecked == true;
            HighPassSlider.IsEnabled = HighPassEnabled;
        }
        
        private void HighPassChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            HighPassCutoff = (float)HighPassSlider.Value;
            HighPassText.Text = HighPassCutoff.ToString("F0") + " Hz";
        }
        
        private void GainClick(object sender, RoutedEventArgs e)
        {
            GainEnabled = GainCheck.IsChecked == true;
            GainSlider.IsEnabled = GainEnabled;
        }
        
        private void GainChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            GainDb = (float)GainSlider.Value;
            GainText.Text = GainDb.ToString("F1") + " dB";
        }
        
        private void EchoClick(object sender, RoutedEventArgs e)
        {
            EchoEnabled = EchoCheck.IsChecked == true;
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
        }
        
        private void ReverbClick(object sender, RoutedEventArgs e)
        {
            ReverbEnabled = ReverbCheck.IsChecked == true;
        }
        
        private void ReverbChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            ReverbWet = (float)ReverbWetSlider.Value;
            ReverbRoom = (float)ReverbRoomSlider.Value;
            
            ReverbWetText.Text = ReverbWet.ToString("F0") + "%";
            ReverbRoomText.Text = ReverbRoom.ToString("F0") + "%";
        }
        
        private void CancelClick(object sender, RoutedEventArgs e)
        {
            ChangesApplied = false;
            DialogResult = false;
            Close();
        }
        
        private void ApplyClick(object sender, RoutedEventArgs e)
        {
            ChangesApplied = true;
            DialogResult = true;
            Close();
        }
    }
}
