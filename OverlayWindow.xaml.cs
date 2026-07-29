using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FPSOverlay
{
    public partial class OverlayWindow : Window
    {
        private DispatcherTimer _updateTimer;
        private OverlayConfig _config;
        private HardwareMonitorManager _hardwareManager;
        private OverclockManager? _overclockManager;
        private IntPtr _hwnd;
        private Thread _topMostThread;
        private volatile bool _isRunning = true;
        private readonly RtssOsdClient _rtss = new();
        private bool _wpfVisible = true;
        private bool _overlayEnabled = true;

        public Action<double, double>? OnPositionChanged;

        public OverlayWindow(OverlayConfig config, HardwareMonitorManager hardwareManager, OverclockManager? overclockManager = null)
        {
            InitializeComponent();
            _config = config;
            _hardwareManager = hardwareManager;
            _overclockManager = overclockManager;
            
            try { this.Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/app.ico")); } catch { }

            ApplyConfig();

            _updateTimer = new DispatcherTimer(DispatcherPriority.Background);
            _updateTimer.Interval = TimeSpan.FromMilliseconds(250);
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();
            
            // drag the overlay around when unlocked ✋
            this.MouseLeftButtonDown += Window_MouseLeftButtonDown;
        }

        public void ApplyConfig()
        {
            OverlayText.FontFamily = new System.Windows.Media.FontFamily(_config.FontFamily);
            OverlayText.FontSize = _config.FontSize;
            
            System.Windows.Media.SolidColorBrush selectedColorBrush;
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_config.TextColorHex);
                selectedColorBrush = new System.Windows.Media.SolidColorBrush(color);
            }
            catch
            {
                selectedColorBrush = System.Windows.Media.Brushes.Lime;
            }
            OverlayText.Foreground = selectedColorBrush;

            // HUD scale rides FontSize (20 = 1.0, easy)
            double scale = _config.FontSize / 20.0;
            AdvancedHudBorder.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);

            // splash that accent color onto Advanced HUD text
            AdvFpsText.Foreground = selectedColorBrush;
            AdvFrametimeText.Foreground = selectedColorBrush;
            AdvFrametimeGraph.Stroke = selectedColorBrush;
            AdvCpuProgress.Foreground = selectedColorBrush;
            AdvRamProgress.Foreground = selectedColorBrush;

            ApplyProfileStyle();

            // park the window where config says
            if (_hwnd != IntPtr.Zero)
            {
                UpdatePositionAndLockState();
            }
        }

        /// <summary>Master show/hide from control panel — also clears RTSS OSD when off.</summary>
        public void SetOverlayEnabled(bool enabled)
        {
            _overlayEnabled = enabled;
            if (enabled)
            {
                _updateTimer.Start();
                SetWpfOverlayVisible(true);
                Show();
            }
            else
            {
                try { _rtss.ReleaseOsd(); } catch { }
                SetWpfOverlayVisible(false);
                Hide();
                _updateTimer.Stop();
            }
        }

        private void ApplyProfileStyle()
        {
            if (OverlayBorder == null || AdvancedHudBorder == null) return;

            if (_config.OverlayProfileIndex == 3)
            {
                OverlayBorder.Visibility = Visibility.Collapsed;
                AdvancedHudBorder.Visibility = Visibility.Visible;
                return;
            }
            else
            {
                OverlayBorder.Visibility = Visibility.Visible;
                AdvancedHudBorder.Visibility = Visibility.Collapsed;
                OverlayText.TextAlignment = TextAlignment.Left;
                OverlayText.LineHeight = double.NaN;
            }

            switch (_config.OverlayProfileIndex)
            {
                case 1: // gamer panel drip
                    OverlayBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(128, 0, 0, 0));
                    OverlayBorder.CornerRadius = new CornerRadius(4);
                    OverlayBorder.Padding = new Thickness(10, 6, 10, 6);
                    OverlayBorder.BorderThickness = new Thickness(0);
                    break;
                case 2: // steam deck mood
                    OverlayBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 26, 26, 26));
                    OverlayBorder.CornerRadius = new CornerRadius(6);
                    OverlayBorder.Padding = new Thickness(12, 8, 12, 8);
                    OverlayBorder.BorderThickness = new Thickness(0);
                    break;
                case 4: // tiny pill energy
                    OverlayBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(170, 12, 14, 18));
                    OverlayBorder.CornerRadius = new CornerRadius(20);
                    OverlayBorder.Padding = new Thickness(14, 7, 14, 7);
                    OverlayBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255));
                    OverlayBorder.BorderThickness = new Thickness(1);
                    break;
                case 5: // neon glass 🔥
                    OverlayBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 18, 12, 10));
                    OverlayBorder.CornerRadius = new CornerRadius(10);
                    OverlayBorder.Padding = new Thickness(12, 8, 12, 8);
                    OverlayBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 242, 76, 29));
                    OverlayBorder.BorderThickness = new Thickness(1.5);
                    break;
                case 6: // Tower = Afterburner vertical stack, chef's kiss
                    OverlayBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 8, 10, 14));
                    OverlayBorder.CornerRadius = new CornerRadius(8);
                    OverlayBorder.Padding = new Thickness(12, 10, 14, 10);
                    OverlayBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(36, 255, 255, 255));
                    OverlayBorder.BorderThickness = new Thickness(1);
                    OverlayText.LineHeight = Math.Max(18, _config.FontSize * 1.15);
                    break;
                case 0: // classic minimal, no fluff
                default:
                    OverlayBorder.Background = System.Windows.Media.Brushes.Transparent;
                    OverlayBorder.CornerRadius = new CornerRadius(0);
                    OverlayBorder.Padding = new Thickness(0);
                    OverlayBorder.BorderThickness = new Thickness(0);
                    break;
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_config.PositionLocked || e.ChangedButton != MouseButton.Left)
                return;
            if (e.ButtonState != MouseButtonState.Pressed)
                return;
            if (Mouse.LeftButton != MouseButtonState.Pressed)
                return;

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            ClampPosition();
            _config.OverlayX = this.Left;
            _config.OverlayY = this.Top;
            _config.Save();
            OnPositionChanged?.Invoke(this.Left, this.Top);
        }

        private void ClampPosition()
        {
            double maxRight = SystemParameters.PrimaryScreenWidth - 50;
            double maxBottom = SystemParameters.PrimaryScreenHeight - 50;

            if (this.Left < 0) this.Left = 0;
            if (this.Top < 0) this.Top = 0;
            if (this.Left > maxRight) this.Left = maxRight;
            if (this.Top > maxBottom) this.Top = maxBottom;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            _hwnd = new WindowInteropHelper(this).Handle;
            
            // don't yeet the overlay off-screen
            ClampPosition();
            
            if (_config.OverlayX == -1) // first boot / default spot
            {
                this.Left = SystemParameters.PrimaryScreenWidth - this.Width;
                this.Top = _config.OverlayY;
            }
            else
            {
                this.Left = _config.OverlayX;
                this.Top = _config.OverlayY;
            }

            UpdatePositionAndLockState();

            _topMostThread = new Thread(TopMostLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _topMostThread.Start();
        }

        public void UpdatePositionAndLockState()
        {
            if (_hwnd == IntPtr.Zero) return;

            int exStyle = Win32Api.GetWindowLong(_hwnd, Win32Api.GWL_EXSTYLE);
            // NOACTIVATE keeps focus on the game when we reassert TopMost (critical in fullscreen).
            exStyle |= Win32Api.WS_EX_LAYERED | Win32Api.WS_EX_TOOLWINDOW | Win32Api.WS_EX_NOACTIVATE;

            if (_config.PositionLocked)
            {
                // click-through — games eat the clicks
                exStyle |= Win32Api.WS_EX_TRANSPARENT;
                OverlayText.Cursor = System.Windows.Input.Cursors.Arrow;
            }
            else
            {
                // unlocked = grab & move
                exStyle &= ~Win32Api.WS_EX_TRANSPARENT;
                OverlayText.Cursor = System.Windows.Input.Cursors.SizeAll;
            }

            Win32Api.SetWindowLong(_hwnd, Win32Api.GWL_EXSTYLE, exStyle);

            if (_config.OverlayX != -1)
            {
                this.Left = _config.OverlayX;
                this.Top = _config.OverlayY;
                ClampPosition();
            }
        }

        private void TopMostLoop()
        {
            while (_isRunning)
            {
                // Keep WPF HUD above desktop chrome when it's the active path.
                if (_hwnd != IntPtr.Zero && _wpfVisible)
                {
                    Win32Api.SetWindowPos(_hwnd, Win32Api.HWND_TOPMOST, 0, 0, 0, 0,
                        Win32Api.SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_NOACTIVATE);
                }
                Thread.Sleep(100);
            }
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            string formattedText;
            if (_config.OverlayProfileIndex == 3)
            {
                UpdateAdvancedHud();
                formattedText = _hardwareManager.FormatOverlayText(_config);
            }
            else
            {
                formattedText = _hardwareManager.FormatOverlayText(_config);
                if (OverlayText.Text != formattedText)
                {
                    OverlayText.Text = formattedText;
                    OverlayText.UpdateLayout();
                }
            }

            PushRtssOsd(formattedText);
            ApplyPresetPosition();
        }

        private void PushRtssOsd(string text)
        {
            // Overlay master toggle (ChkOverlayActive) gates both Mars HUD and RTSS.
            // Windowed / borderless / FSO → Mars WPF HUD.
            // Exclusive fullscreen → RTSS (auto), styled from Display settings.

            if (!_overlayEnabled)
            {
                if (_rtss.IsAvailable)
                    _rtss.ReleaseOsd();
                return;
            }

            bool exclusive = Win32Api.IsExclusiveD3DFullscreen();
            if (!exclusive)
            {
                if (_rtss.IsAvailable)
                    _rtss.ReleaseOsd();
                SetWpfOverlayVisible(true);
                return;
            }

            if (!_rtss.IsAvailable)
                _rtss.EnsureRunning(allowLaunch: true);

            if (!_rtss.IsAvailable)
            {
                SetWpfOverlayVisible(true);
                return;
            }

            string osd = BuildRtssHypertext(text);
            ResolveRtssPlacement(out int osdX, out int osdY, out int zoom);
            _rtss.UpdateOsd(osd, osdX, osdY, zoom);
            SetWpfOverlayVisible(false);
        }

        private string BuildRtssHypertext(string text)
        {
            string hex = (_config.TextColorHex ?? "#F24C1D").TrimStart('#');
            if (hex.Length == 8)
                hex = hex[^6..];
            if (hex.Length != 6)
                hex = "F24C1D";

            // FontSize 20 → 100% RTSS size; clamp to sprite-friendly range.
            int sizePct = (int)Math.Clamp(Math.Round(_config.FontSize / 20.0 * 100.0), 50, 200);

            return $"<C=FF{hex}><S={sizePct}>{SanitizeForRtss(text)}";
        }

        private void ResolveRtssPlacement(out int osdX, out int osdY, out int zoom)
        {
            // RTSS zoom: 1 = 100%. Map FontSize roughly (20 → 1, 40 → 2).
            zoom = Math.Clamp((int)Math.Round(_config.FontSize / 20.0), 1, 4);

            int pad = (int)Math.Clamp(_config.PositionPadding, 0, 200);
            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;
            // Approximate OSD block size for corner math (chars × zoom).
            int estW = Math.Max(160, (int)(_config.FontSize * 12));
            int estH = Math.Max(40, (int)(_config.FontSize * 4));

            switch (_config.PositionPreset)
            {
                case OverlayPositionPreset.TopLeft:
                    osdX = pad; osdY = pad; break;
                case OverlayPositionPreset.TopCenter:
                    osdX = Math.Max(pad, (int)((screenW - estW) / 2)); osdY = pad; break;
                case OverlayPositionPreset.TopRight:
                    osdX = -pad; osdY = pad; break;
                case OverlayPositionPreset.MiddleLeft:
                    osdX = pad; osdY = Math.Max(pad, (int)((screenH - estH) / 2)); break;
                case OverlayPositionPreset.Center:
                    osdX = Math.Max(pad, (int)((screenW - estW) / 2));
                    osdY = Math.Max(pad, (int)((screenH - estH) / 2));
                    break;
                case OverlayPositionPreset.MiddleRight:
                    osdX = -pad;
                    osdY = Math.Max(pad, (int)((screenH - estH) / 2));
                    break;
                case OverlayPositionPreset.BottomLeft:
                    osdX = pad; osdY = -pad; break;
                case OverlayPositionPreset.BottomCenter:
                    osdX = Math.Max(pad, (int)((screenW - estW) / 2));
                    osdY = -pad;
                    break;
                case OverlayPositionPreset.BottomRight:
                    osdX = -pad;
                    osdY = -pad;
                    break;
                case OverlayPositionPreset.Custom:
                default:
                    osdX = _config.OverlayX >= 0 ? (int)_config.OverlayX : pad;
                    osdY = _config.OverlayY >= 0 ? (int)_config.OverlayY : pad;
                    break;
            }
        }

        private static string SanitizeForRtss(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            // RTSS uses <...> hypertext — escape raw angles from sensor names.
            return text.Replace('<', '(').Replace('>', ')');
        }

        private void SetWpfOverlayVisible(bool visible)
        {
            if (_wpfVisible == visible)
                return;
            _wpfVisible = visible;
            Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        }

        private void UpdateAdvancedHud()
        {
            var data = _hardwareManager.GetAdvancedData(_config.SelectedGpuName);
            var fps = _hardwareManager.FpsMonitor;
            
            // refresh foreground PID for ETW or FPS is a lie
            fps.RefreshFps();

            // block 1: FPS flex
            AdvFpsText.Text = fps.CurrentFps.ToString();
            AdvOnePercentLowText.Text = $"{fps.OnePercentLowFps:F1} FPS";
            AdvFrametimeText.Text = $"{fps.CurrentFrametimeMs:F1} ms";

            // scribble the frametime polyline
            var times = fps.GetFrametimesSnapshot();
            if (times.Length > 0)
            {
                var points = new System.Windows.Media.PointCollection();
                double width = 100.0;
                double height = 25.0;
                double step = width / Math.Max(1, times.Length - 1);
                
                // cap scale ~50ms (~20fps) so graph doesn't explode
                double maxMs = 50.0; 

                for (int i = 0; i < times.Length; i++)
                {
                    double x = i * step;
                    double y = height - (Math.Min(times[i], maxMs) / maxMs * height);
                    points.Add(new System.Windows.Point(x, y));
                }
                AdvFrametimeGraph.Points = points;
            }

            // block 2: CPU gang
            if (_config.ShowCpuTemp)
            {
                AdvCpuBlock.Visibility = Visibility.Visible;
                AdvCpuName.Text = data.CpuName;
                AdvCpuLoad.Text = $"{data.CpuLoad:F0}%";
                AdvCpuFreq.Text = $"{data.CpuFreq:F0} MHz";
                AdvCpuTemp.Text = $"{data.CpuTemp:F0}°C";
                AdvCpuProgress.Value = data.CpuLoad;
            }
            else
            {
                AdvCpuBlock.Visibility = Visibility.Collapsed;
            }

            // block 3: RAM check
            if (_config.ShowRamUsage)
            {
                AdvRamBlock.Visibility = Visibility.Visible;
                AdvRamName.Text = data.RamName;
                AdvRamLoad.Text = $"{data.RamLoad:F0}%";
                AdvRamUsage.Text = $"{data.RamUsedGB:F1} / {data.RamTotalGB:F1} GB";
                AdvRamProgress.Value = data.RamLoad;
            }
            else
            {
                AdvRamBlock.Visibility = Visibility.Collapsed;
            }

            // block 4: GPU time
            if (_config.ShowGpuTemp) // ShowGpuTemp doubles as whole GPU block toggle (lazy but works)
            {
                AdvGpuBlock.Visibility = Visibility.Visible;
                AdvGpuName.Text = data.GpuName;
                AdvGpuLoad.Text = $"{data.GpuLoad:F0}%";
                AdvGpuFreq.Text = $"{data.GpuFreq:F0} MHz";
                AdvGpuTemp.Text = $"{data.GpuTemp:F0}°C";
                AdvGpuProgress.Value = data.GpuLoad;
            }
            else
            {
                AdvGpuBlock.Visibility = Visibility.Collapsed;
            }

            // block 5: VRAM
            if (_config.ShowVramUsage)
            {
                AdvVramBlock.Visibility = Visibility.Visible;
                AdvVramName.Text = data.VramName;
                AdvVramLoad.Text = $"{data.VramLoad:F0}%";
                AdvVramUsage.Text = $"{data.VramUsedGB:F1} / {data.VramTotalGB:F1} GB";
                AdvVramProgress.Value = data.VramLoad;
            }
            else
            {
                AdvVramBlock.Visibility = Visibility.Collapsed;
            }

            // block 6: OC status
            if (_config.ShowOverclockStatus && _overclockManager != null)
            {
                AdvOcBlock.Visibility = Visibility.Visible;
                var st = _overclockManager.Status;
                int pct = st.IntensityPercent;
                var t = st.LastGpuTarget ?? OcProfileStore.SafeStock.ToTarget();
                AdvOcPercent.Text = $"{pct}%";
                AdvOcPower.Text = t.GpuPowerLimitPercent is int pl ? $"PL {pl}%" : "PL stock";
                AdvOcProgress.Value = pct;
                AdvOcName.Text = st.ControlMode == OcControlMode.AutoThermal
                    ? $"OC · {st.ActiveProfileName} AUTO"
                    : st.ControlMode == OcControlMode.ManualFixed
                        ? $"OC · {st.ActiveProfileName} MAN"
                        : "OVERCLOCK";
                AdvOcOffsets.Text = st.LastCoreTempC is float tc
                    ? $"Core +{t.GpuCoreOffsetMhz} / Mem +{t.GpuMemoryOffsetMhz} · {tc:F0}°C"
                    : $"Core +{t.GpuCoreOffsetMhz} / Mem +{t.GpuMemoryOffsetMhz}";
            }
            else
            {
                AdvOcBlock.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyPresetPosition()
        {
            if (_config.PositionPreset == OverlayPositionPreset.Custom)
                return;

            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;
            double pad = _config.PositionPadding;

            double w = this.ActualWidth;
            double h = this.ActualHeight;
            double left = this.Left;
            double top = this.Top;

            switch (_config.PositionPreset)
            {
                case OverlayPositionPreset.TopLeft:
                    left = pad; top = pad; break;
                case OverlayPositionPreset.TopCenter:
                    left = (screenW - w) / 2; top = pad; break;
                case OverlayPositionPreset.TopRight:
                    left = screenW - w - pad; top = pad; break;
                case OverlayPositionPreset.MiddleLeft:
                    left = pad; top = (screenH - h) / 2; break;
                case OverlayPositionPreset.Center:
                    left = (screenW - w) / 2; top = (screenH - h) / 2; break;
                case OverlayPositionPreset.MiddleRight:
                    left = screenW - w - pad; top = (screenH - h) / 2; break;
                case OverlayPositionPreset.BottomLeft:
                    left = pad; top = screenH - h - pad; break;
                case OverlayPositionPreset.BottomCenter:
                    left = (screenW - w) / 2; top = screenH - h - pad; break;
                case OverlayPositionPreset.BottomRight:
                    left = screenW - w - pad; top = screenH - h - pad; break;
            }

            if (Math.Abs(this.Left - left) > 0.5)
                this.Left = left;
            if (Math.Abs(this.Top - top) > 0.5)
                this.Top = top;
        }

        protected override void OnClosed(EventArgs e)
        {
            _isRunning = false;
            _updateTimer.Stop();
            try { _rtss.Dispose(); } catch { }
            base.OnClosed(e);
        }
    }
}

