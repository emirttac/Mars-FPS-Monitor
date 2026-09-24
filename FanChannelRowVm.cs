using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FPSOverlay
{
    /// <summary>One Fans-tab channel row: live RPM / PWM text (no spinner).</summary>
    public sealed class FanChannelRowVm : INotifyPropertyChanged
    {
        private string _name = "";
        private string _rpmText = "—";
        private string _pwmText = "—";
        private string _badge = "";
        private string _backend = "";
        private string _id = "";

        public string Id
        {
            get => _id;
            set { if (_id == value) return; _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; OnPropertyChanged(); }
        }

        public string RpmText
        {
            get => _rpmText;
            set { if (_rpmText == value) return; _rpmText = value; OnPropertyChanged(); }
        }

        public string PwmText
        {
            get => _pwmText;
            set { if (_pwmText == value) return; _pwmText = value; OnPropertyChanged(); }
        }

        public string Badge
        {
            get => _badge;
            set { if (_badge == value) return; _badge = value; OnPropertyChanged(); }
        }

        public string Backend
        {
            get => _backend;
            set { if (_backend == value) return; _backend = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
