using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using NINA.Profile;
using NINA.Profile.Interfaces;
using Newtonsoft.Json;
using NINA.Core.Utility;

namespace NirZonshine.NINA.TwoPointPolarAlignment.Services
{
    public class SettingsManager : INotifyPropertyChanged, IDisposable
    {
        private bool _disposed;
        private readonly IProfileService _profileService;
        private readonly Guid _pluginGuid = Guid.Parse("0e9e3e58-42fc-4553-8e6e-aba061af4f54");

        public event PropertyChangedEventHandler PropertyChanged;

        // Default Values
        private double _exposureTime = 2.0;
        private int _gain = 0;
        private double _rotationAmount = 90.0;
        private RotationMethod _method = RotationMethod.Automatic;
        private RotationDirection _direction = RotationDirection.East;
        private StartingPointMode _startingPoint = StartingPointMode.PreRotateHalfRange;
        private string _filter = "(Current)";
        private string _binning = "1x1";
        private int _offset = 0;
        private double _telescopeMoveRate = 3.0;
        private int _plateSolveRetries = 3;
        private bool _enableOnePointAlignment = false;
        private AltitudeKnobDirection _altKnobDirection = AltitudeKnobDirection.UpArrow;
        private bool _overrideMountHome = false;
        private double _polarHomeRA = 0.0;
        private double _polarHomeDec = 90.0;

        public SettingsManager(IProfileService profileService)
        {
            _profileService = profileService;
            if (_profileService != null)
            {
                _profileService.ProfileChanged += ProfileService_ProfileChanged;
            }
            LoadSettings();
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                LoadSettings();
            });
        }

        public double ExposureTime
        {
            get => _exposureTime;
            set { if (_exposureTime != value) { _exposureTime = value; SaveSetting(nameof(ExposureTime), value); OnPropertyChanged(); } }
        }

        public int Gain
        {
            get => _gain;
            set { if (_gain != value) { _gain = value; SaveSetting(nameof(Gain), value); OnPropertyChanged(); } }
        }

        public double RotationAmount
        {
            get => _rotationAmount;
            set { if (_rotationAmount != value) { _rotationAmount = value; SaveSetting(nameof(RotationAmount), value); OnPropertyChanged(); } }
        }

        public RotationMethod Method
        {
            get => _method;
            set { if (_method != value) { _method = value; SaveSetting(nameof(Method), (int)value); OnPropertyChanged(); } }
        }

        public RotationDirection Direction
        {
            get => _direction;
            set { if (_direction != value) { _direction = value; SaveSetting(nameof(Direction), (int)value); OnPropertyChanged(); } }
        }

        public StartingPointMode StartingPoint
        {
            get => _startingPoint;
            set { if (_startingPoint != value) { _startingPoint = value; SaveSetting(nameof(StartingPoint), (int)value); OnPropertyChanged(); } }
        }

        public string Filter
        {
            get => _filter;
            set { if (_filter != value) { _filter = value; SaveSetting(nameof(Filter), value); OnPropertyChanged(); } }
        }

        public string Binning
        {
            get => _binning;
            set { if (_binning != value) { _binning = value; SaveSetting(nameof(Binning), value); OnPropertyChanged(); } }
        }

        public int Offset
        {
            get => _offset;
            set { if (_offset != value) { _offset = value; SaveSetting(nameof(Offset), value); OnPropertyChanged(); } }
        }

        public double TelescopeMoveRate
        {
            get => _telescopeMoveRate;
            set { if (_telescopeMoveRate != value) { _telescopeMoveRate = value; SaveSetting(nameof(TelescopeMoveRate), value); OnPropertyChanged(); } }
        }

        public int PlateSolveRetries
        {
            get => _plateSolveRetries;
            set { if (_plateSolveRetries != value) { _plateSolveRetries = value; SaveSetting(nameof(PlateSolveRetries), value); OnPropertyChanged(); } }
        }

        public bool EnableOnePointAlignment
        {
            get => _enableOnePointAlignment;
            set { if (_enableOnePointAlignment != value) { _enableOnePointAlignment = value; SaveSetting(nameof(EnableOnePointAlignment), value); OnPropertyChanged(); } }
        }

        public AltitudeKnobDirection AltKnobDirection
        {
            get => _altKnobDirection;
            set { if (_altKnobDirection != value) { _altKnobDirection = value; SaveSetting(nameof(AltKnobDirection), (int)value); OnPropertyChanged(); } }
        }

        public bool OverrideMountHome
        {
            get => _overrideMountHome;
            set { if (_overrideMountHome != value) { _overrideMountHome = value; SaveSetting(nameof(OverrideMountHome), value); OnPropertyChanged(); } }
        }

        public double PolarHomeRA
        {
            get => _polarHomeRA;
            set { if (_polarHomeRA != value) { _polarHomeRA = value; SaveSetting(nameof(PolarHomeRA), value); OnPropertyChanged(); } }
        }

        public double PolarHomeDec
        {
            get => _polarHomeDec;
            set { if (_polarHomeDec != value) { _polarHomeDec = value; SaveSetting(nameof(PolarHomeDec), value); OnPropertyChanged(); } }
        }

        private void LoadSettings()
        {
            try
            {
                if (_profileService == null) return;
                var accessor = new PluginOptionsAccessor(_profileService, _pluginGuid);

                bool isMigrated = accessor.GetValueBoolean("SettingsMigrated", false);

                if (!isMigrated)
                {
                    MigrateLegacySettings(accessor);
                    accessor.SetValueBoolean("SettingsMigrated", true);
                }

                _exposureTime = accessor.GetValueDouble(nameof(ExposureTime), 2.0);
                _gain = accessor.GetValueInt32(nameof(Gain), 0);
                _rotationAmount = accessor.GetValueDouble(nameof(RotationAmount), 90.0);
                _method = (RotationMethod)accessor.GetValueInt32(nameof(Method), (int)RotationMethod.Automatic);
                _direction = (RotationDirection)accessor.GetValueInt32(nameof(Direction), (int)RotationDirection.East);
                _startingPoint = (StartingPointMode)accessor.GetValueInt32(nameof(StartingPoint), (int)StartingPointMode.PreRotateHalfRange);
                _filter = accessor.GetValueString(nameof(Filter), "(Current)");
                _binning = accessor.GetValueString(nameof(Binning), "1x1");
                _offset = accessor.GetValueInt32(nameof(Offset), 0);
                _telescopeMoveRate = accessor.GetValueDouble(nameof(TelescopeMoveRate), 3.0);
                _plateSolveRetries = accessor.GetValueInt32(nameof(PlateSolveRetries), 3);
                _enableOnePointAlignment = accessor.GetValueBoolean(nameof(EnableOnePointAlignment), false);
                _altKnobDirection = (AltitudeKnobDirection)accessor.GetValueInt32(nameof(AltKnobDirection), (int)AltitudeKnobDirection.UpArrow);
                _overrideMountHome = accessor.GetValueBoolean(nameof(OverrideMountHome), false);
                _polarHomeRA = accessor.GetValueDouble(nameof(PolarHomeRA), 0.0);
                _polarHomeDec = accessor.GetValueDouble(nameof(PolarHomeDec), 90.0);

                OnPropertyChanged(string.Empty);
            }
            catch (Exception ex)
            {
                Logger.Error($"[2-Point Polar Alignment] Failed to load settings: {ex.Message}");
            }
        }

        private void MigrateLegacySettings(PluginOptionsAccessor accessor)
        {
            string json = accessor.GetValueString("TwoPointPolarAlignment_Settings", string.Empty);

            if (string.IsNullOrEmpty(json))
            {
                string ninaFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA");
                string legacyFile = Path.Combine(ninaFolder, "Plugins", "2-Point Polar Alignment", "settings.json");
                if (File.Exists(legacyFile))
                {
                    try { json = File.ReadAllText(legacyFile); } catch { }
                }
            }

            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var settingsObj = JsonConvert.DeserializeObject<LegacyPluginSettings>(json);
                    if (settingsObj != null)
                    {
                        accessor.SetValueDouble(nameof(ExposureTime), settingsObj.ExposureTime);
                        accessor.SetValueInt32(nameof(Gain), settingsObj.Gain);
                        accessor.SetValueDouble(nameof(RotationAmount), settingsObj.RotationAmount);
                        accessor.SetValueInt32(nameof(Method), (int)settingsObj.Method);
                        accessor.SetValueInt32(nameof(Direction), (int)settingsObj.Direction);
                        accessor.SetValueInt32(nameof(StartingPoint), (int)settingsObj.StartingPoint);
                        accessor.SetValueString(nameof(Filter), settingsObj.Filter ?? "(Current)");
                        accessor.SetValueString(nameof(Binning), settingsObj.Binning ?? "1x1");
                        accessor.SetValueInt32(nameof(Offset), settingsObj.Offset);
                        accessor.SetValueDouble(nameof(TelescopeMoveRate), settingsObj.TelescopeMoveRate);
                        accessor.SetValueInt32(nameof(PlateSolveRetries), settingsObj.PlateSolveRetries);
                        accessor.SetValueBoolean(nameof(EnableOnePointAlignment), settingsObj.EnableOnePointAlignment);
                        accessor.SetValueInt32(nameof(AltKnobDirection), (int)settingsObj.AltKnobDirection);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[2-Point Polar Alignment] Failed to parse legacy JSON settings: {ex.Message}");
                }
            }
        }

        private void SaveSetting(string key, object value)
        {
            try
            {
                if (_profileService == null) return;
                var accessor = new PluginOptionsAccessor(_profileService, _pluginGuid);
                
                if (value is double d) accessor.SetValueDouble(key, d);
                else if (value is int i) accessor.SetValueInt32(key, i);
                else if (value is bool b) accessor.SetValueBoolean(key, b);
                else if (value is string s) accessor.SetValueString(key, s);
            }
            catch (Exception ex)
            {
                Logger.Error($"[2-Point Polar Alignment] Failed to save setting '{key}': {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_profileService != null)
            {
                _profileService.ProfileChanged -= ProfileService_ProfileChanged;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private class LegacyPluginSettings
        {
            public double ExposureTime { get; set; } = 2.0;
            public int Gain { get; set; } = 0;
            public double RotationAmount { get; set; } = 90.0;
            public RotationMethod Method { get; set; } = RotationMethod.Automatic;
            public RotationDirection Direction { get; set; } = RotationDirection.East;
            public StartingPointMode StartingPoint { get; set; } = StartingPointMode.PreRotateHalfRange;
            public string Filter { get; set; } = "(Current)";
            public string Binning { get; set; } = "1x1";
            public int Offset { get; set; } = 0;
            public double TelescopeMoveRate { get; set; } = 3.0;
            public int PlateSolveRetries { get; set; } = 3;
            public bool EnableOnePointAlignment { get; set; } = false;
            public AltitudeKnobDirection AltKnobDirection { get; set; } = AltitudeKnobDirection.UpArrow;
            public bool OverrideMountHome { get; set; } = false;
            public double PolarHomeRA { get; set; } = 0.0;
            public double PolarHomeDec { get; set; } = 90.0;
        }
    }
}
