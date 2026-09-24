using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace FPSOverlay
{
    public enum GamePlatform
    {
        Steam,
        Epic,
        Gog,
        Ea,
        Ubisoft,
        Custom
    }

    /// <summary>Single library entry — launcher-discovered or manually added.</summary>
    public sealed class GameItem : INotifyPropertyChanged
    {
        private ImageSource? _coverImage;
        private string? _coverUrl;
        private bool _coverLoadFailed;
        private bool _isIconFallback;
        private bool _coverLookupDone;

        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Title { get; set; } = "";

        public string? ExePath { get; set; }

        public string? LaunchUri { get; set; }

        public string? LaunchArgs { get; set; }

        public string? InstallDir { get; set; }

        public string? CoverUrl
        {
            get => _coverUrl;
            set
            {
                if (_coverUrl == value) return;
                _coverUrl = value;
                OnPropertyChanged();
            }
        }

        /// <summary>True after a web cover search finished (hit or miss) — skip re-query.</summary>
        public bool CoverLookupDone
        {
            get => _coverLookupDone;
            set
            {
                if (_coverLookupDone == value) return;
                _coverLookupDone = value;
                OnPropertyChanged();
            }
        }

        public GamePlatform PlatformSource { get; set; } = GamePlatform.Custom;

        public bool IsCustom { get; set; }

        public string? PlatformAppId { get; set; }

        [JsonIgnore]
        public string PlatformBadge => PlatformSource switch
        {
            GamePlatform.Steam => "Steam",
            GamePlatform.Epic => "Epic",
            GamePlatform.Gog => "GOG",
            GamePlatform.Ea => "EA",
            GamePlatform.Ubisoft => "Ubisoft",
            _ => "Custom"
        };

        [JsonIgnore]
        public ImageSource? CoverImage
        {
            get => _coverImage;
            set
            {
                if (ReferenceEquals(_coverImage, value)) return;
                _coverImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCoverImage));
            }
        }

        [JsonIgnore]
        public bool HasCoverImage => _coverImage != null;

        /// <summary>EXE icon shown centered (64×64) when no vertical poster was found.</summary>
        [JsonIgnore]
        public bool IsIconFallback
        {
            get => _isIconFallback;
            set
            {
                if (_isIconFallback == value) return;
                _isIconFallback = value;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public bool CoverLoadFailed
        {
            get => _coverLoadFailed;
            set
            {
                if (_coverLoadFailed == value) return;
                _coverLoadFailed = value;
                OnPropertyChanged();
            }
        }

        private string _statsFpsValue = "—";
        private string _statsLowLabel = "1%";
        private string _statsLowValue = "—";
        private string _statsGpuMax = "—";
        private string _statsGpuAvg = "";
        private string _statsCpuMax = "—";
        private string _statsCpuAvg = "";

        [JsonIgnore]
        public string StatsFpsValue { get => _statsFpsValue; set => SetStat(ref _statsFpsValue, value); }

        [JsonIgnore]
        public string StatsLowLabel { get => _statsLowLabel; set => SetStat(ref _statsLowLabel, value); }

        [JsonIgnore]
        public string StatsLowValue { get => _statsLowValue; set => SetStat(ref _statsLowValue, value); }

        [JsonIgnore]
        public string StatsGpuMax { get => _statsGpuMax; set => SetStat(ref _statsGpuMax, value); }

        [JsonIgnore]
        public string StatsGpuAvg { get => _statsGpuAvg; set => SetStat(ref _statsGpuAvg, value); }

        [JsonIgnore]
        public string StatsCpuMax { get => _statsCpuMax; set => SetStat(ref _statsCpuMax, value); }

        [JsonIgnore]
        public string StatsCpuAvg { get => _statsCpuAvg; set => SetStat(ref _statsCpuAvg, value); }

        private void SetStat(ref string field, string value, [CallerMemberName] string? name = null)
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged(name);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public GameItem Clone() => new()
        {
            Id = Id,
            Title = Title,
            ExePath = ExePath,
            LaunchUri = LaunchUri,
            LaunchArgs = LaunchArgs,
            InstallDir = InstallDir,
            CoverUrl = CoverUrl,
            CoverLookupDone = CoverLookupDone,
            PlatformSource = PlatformSource,
            IsCustom = IsCustom,
            PlatformAppId = PlatformAppId
        };
    }
}
