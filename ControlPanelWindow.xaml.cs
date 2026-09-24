using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FPSOverlay
{
    public partial class ControlPanelWindow : Window
    {
        private OverlayConfig _config;
        private HardwareMonitorManager _hwManager;
        private OverclockManager? _ocManager;
        private FanControlManager? _fanManager;
        private Action _onConfigChanged;
        private Action<bool> _onOverlayToggle;
        private string _selectedColorHex;
        private List<AiOcRecommendation> _aiSuggestions = new();
        private bool _aiBusy;
        private bool _aiStatusFromSplash;
        private string? _updateReleaseUrl;
        private UiStrings _s = UiStrings.En();
        private DispatcherTimer? _saveDebounce;
        private string? _lastLanguageApplied;
        private DispatcherTimer? _homeGaugeTimer;
        private DispatcherTimer? _homeIntroDelayTimer;
        private DispatcherTimer? _homeIntroWatchdog;
        private readonly FrameAnimator _homeIntroClock = new();
        private System.Threading.Timer? _autoUpdateTimer;
        private bool _updateBadgeShown;
        private int _updateToastShown; // 0 = not yet, 1 = already toasted this session

        private bool _homeIntroPlayed;
        private bool _homeIntroRunning;
        private bool _ocStatusQueued;
        private bool _fanStatusQueued;
        private bool _homeLiveUpdating;
        private int _homeIntroGen;

        private readonly ObservableCollection<FanChannelRowVm> _fanRows = new();

        public ControlPanelWindow(OverlayConfig config, HardwareMonitorManager hwManager, Action onConfigChanged, Action<bool> onOverlayToggle, OverclockManager? ocManager = null, FanControlManager? fanManager = null)
        {
            InitializeComponent();
            _config = config;
            _hwManager = hwManager;
            _ocManager = ocManager;
            _fanManager = fanManager;
            _onConfigChanged = onConfigChanged;
            _onOverlayToggle = onOverlayToggle;
            _selectedColorHex = _config.TextColorHex;

            try { this.Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/app.ico")); } catch { }

            Opacity = 0;
            Loaded += (_, __) =>
            {
                PlayWindowIntro();
                ScheduleHomeGaugeIntro();
            };

            PopulateGpuSelector();
            LoadSettingsToUI();
            ApplyLanguage();
            _lastLanguageApplied = _config.Language;
            if (LstFanChannels != null)
                LstFanChannels.ItemsSource = _fanRows;
            ShowPanel("Home");
            RefreshOverclockStatusUi();
            RefreshFanStatusUi();

            if (_ocManager != null)
                _ocManager.StatusChanged += QueueOverclockStatusRefresh;
            if (_fanManager != null)
                _fanManager.StatusChanged += QueueFanStatusRefresh;

            StartAutoUpdateChecker();
            OverlayConfig.SecretSaveFailed += OnSecretSaveFailed;
        }

        private void OnSecretSaveFailed()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(OnSecretSaveFailed));
                return;
            }

            System.Windows.MessageBox.Show(
                _s.SecretSaveFailed,
                AppInfo.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        public void AttachLibraryStats(GameSessionTracker tracker)
        {
            PanelLibrary.AttachSessionTracker(tracker);
        }

        private void PlayWindowIntro()
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fade);
        }

        private string? _currentPanel;
        private bool _panelSwitchQueued;

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton rb && rb.Tag != null)
                ShowPanel(rb.Tag.ToString() ?? "Home");
        }

        private void ShowPanel(string name)
        {
            if (PanelHome == null) return;
            if (string.Equals(_currentPanel, name, StringComparison.Ordinal))
                return;

            _currentPanel = name;
            // Coalesce onto the render pass so the click itself isn't blocked by layout,
            // without waiting a full extra frame (Background) which reads as lag.
            if (_panelSwitchQueued) return;
            _panelSwitchQueued = true;
            Dispatcher.BeginInvoke(new Action(ApplyPanelSwitch), DispatcherPriority.Render);
        }

        private void ApplyPanelSwitch()
        {
            _panelSwitchQueued = false;
            string name = _currentPanel ?? "Home";

            UIElement? target = name switch
            {
                "Home" => PanelHome,
                "Library" => PanelLibrary,
                "Sensors" => PanelSensors,
                "Display" => PanelDisplay,
                "Overclock" => PanelOverclock,
                "Fans" => PanelFans,
                "About" => PanelAbout,
                "Overlay" => PanelOverlay,
                _ => PanelHome
            };

            SetPanelShown(PanelHome, ReferenceEquals(PanelHome, target));
            SetPanelShown(PanelLibrary, ReferenceEquals(PanelLibrary, target));
            SetPanelShown(PanelOverlay, ReferenceEquals(PanelOverlay, target));
            SetPanelShown(PanelSensors, ReferenceEquals(PanelSensors, target));
            SetPanelShown(PanelDisplay, ReferenceEquals(PanelDisplay, target));
            SetPanelShown(PanelOverclock, ReferenceEquals(PanelOverclock, target));
            SetPanelShown(PanelFans, ReferenceEquals(PanelFans, target));
            SetPanelShown(PanelAbout, ReferenceEquals(PanelAbout, target));

            if (target == null) return;

            if (ReferenceEquals(target, PanelHome))
                ResetPanelMotion(target);
            else
                AnimatePanelIn(target);

            if (name == "Home")
            {
                // Returning to Home snaps the dials. A fresh sweep on every visit
                // redraws three gauges at display rate and hitches the UI thread.
                if (_homeIntroPlayed && !_homeIntroRunning)
                    StartHomeGaugeLiveUpdates(animateSample: false);
            }
            else
            {
                AbortHomeGaugeIntro();
                StopHomeGaugeLiveUpdates();
            }

            if (name == "Overclock")
                QueueOverclockStatusRefresh();
            if (name == "Fans")
                QueueFanStatusRefresh();
        }

        private void ScheduleHomeGaugeIntro()
        {
            if (_homeIntroPlayed || _homeIntroRunning) return;
            if (_homeIntroDelayTimer != null) return;

            // Full sweep starts exactly 1s after app/control panel load.
            if (GaugeCpu != null)
            {
                GaugeCpu.ShowValue = false;
                GaugeCpu.Value = 0;
            }
            if (GaugeGpu != null)
            {
                GaugeGpu.ShowValue = false;
                GaugeGpu.Value = 0;
            }
            if (GaugeRam != null)
            {
                GaugeRam.ShowValue = false;
                GaugeRam.Value = 0;
                GaugeRam.UsageCaption = "";
            }

            _homeIntroDelayTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(650)
            };
            _homeIntroDelayTimer.Tick += (_, __) =>
            {
                _homeIntroDelayTimer?.Stop();
                _homeIntroDelayTimer = null;
                if (!string.Equals(_currentPanel, "Home", StringComparison.Ordinal) && _currentPanel != null)
                {
                    AbortHomeGaugeIntro();
                    return;
                }
                PlayHomeGaugeIntro();
            };
            _homeIntroDelayTimer.Start();
        }

        private void PlayHomeGaugeIntro()
        {
            if (GaugeCpu == null || GaugeGpu == null) return;
            if (_homeIntroPlayed || _homeIntroRunning) return;

            _homeIntroRunning = true;
            _homeIntroPlayed = true;
            int gen = ++_homeIntroGen;
            ArmHomeIntroWatchdog(gen);

            StopHomeGaugeLiveUpdates();
            GaugeCpu.StopAnimation();
            GaugeGpu.StopAnimation();
            GaugeRam?.StopAnimation();
            GaugeCpu.ShowValue = false;
            GaugeGpu.ShowValue = false;
            if (GaugeRam != null)
            {
                GaugeRam.ShowValue = false;
                GaugeRam.Value = 0;
                GaugeRam.UsageCaption = "";
            }
            GaugeCpu.Value = 0;
            GaugeGpu.Value = 0;

            double max = GaugeCpu.Maximum;
            double ramMax = GaugeRam?.Maximum ?? 100;

            // One display-synced clock for all three dials. Chained timers made the
            // sweep step between gauges and hitch when each animation restarted.
            const double stagger = 80;
            const double up = 1600;
            const double holdTop = 160;
            const double down = 1400;
            const double holdBot = 140;
            const double live = 1600;
            double total = stagger * 2 + up + holdTop + down + holdBot + live;
            double liveStart = stagger * 2 + up + holdTop + down + holdBot;

            bool valuesOn = false;
            bool latched = false;
            double cpuT = 0, gpuT = 0, ramT = 0;
            string ramCap = "";

            _homeIntroClock.Animate(TimeSpan.FromMilliseconds(total), p =>
            {
                if (gen != _homeIntroGen || GaugeCpu == null || GaugeGpu == null) return;
                double ms = p * total;

                if (!latched && ms >= liveStart)
                {
                    latched = true;
                    cpuT = Math.Max(0, _hwManager.GetCpuTemperature());
                    gpuT = Math.Max(0, _hwManager.GetGpuTemperature(_config.SelectedGpuName));
                    var (ramLoad, ramUsed, ramTotal) = _hwManager.GetRamSnapshot();
                    ramT = Math.Clamp(ramLoad, 0, 100);
                    ramCap = ramTotal > 0.1f ? $"{ramUsed:F1}/{ramTotal:F1} GB" : "";
                }

                if (!valuesOn && ms >= liveStart)
                {
                    valuesOn = true;
                    GaugeCpu.ShowValue = true;
                    GaugeGpu.ShowValue = true;
                    if (GaugeRam != null)
                    {
                        GaugeRam.UsageCaption = ramCap;
                        GaugeRam.ShowValue = true;
                    }
                }

                GaugeCpu.Value = GaugeIntroPhase(ms, 0, up, holdTop, down, holdBot, live, max, cpuT);
                if (GaugeRam != null)
                    GaugeRam.Value = GaugeIntroPhase(ms, stagger, up, holdTop, down, holdBot, live, ramMax, ramT);
                GaugeGpu.Value = GaugeIntroPhase(ms, stagger * 2, up, holdTop, down, holdBot, live, max, gpuT);
            }, () =>
            {
                if (gen != _homeIntroGen) return;
                CompleteHomeIntro(gen);
            });
        }

        private static double GaugeIntroPhase(
            double ms, double delay, double up, double holdTop, double down, double holdBot, double live,
            double peak, double liveTarget)
        {
            double t = ms - delay;
            if (t <= 0) return 0;
            if (t < up) return peak * UiMotion.SmootherStep(t / up);
            t -= up;
            if (t < holdTop) return peak;
            t -= holdTop;
            if (t < down) return peak * (1 - UiMotion.SmootherStep(t / down));
            t -= down;
            if (t < holdBot) return 0;
            t -= holdBot;
            if (t < live) return liveTarget * UiMotion.SmootherStep(t / live);
            return liveTarget;
        }

        /// <summary>
        /// User left Home before the sweep finished (or before it started).
        /// Invalidate in-flight callbacks so a later return can start live updates.
        /// </summary>
        private void AbortHomeGaugeIntro()
        {
            _homeIntroDelayTimer?.Stop();
            _homeIntroDelayTimer = null;
            _homeIntroWatchdog?.Stop();
            _homeIntroWatchdog = null;

            if (_homeIntroRunning)
            {
                _homeIntroGen++;
                _homeIntroRunning = false;
                _homeIntroClock.Stop();
            }

            GaugeCpu?.StopAnimation();
            GaugeGpu?.StopAnimation();
            GaugeRam?.StopAnimation();
            _homeIntroPlayed = true;
        }

        private void StartHomeGaugeLiveUpdates(bool animateSample = true)
        {
            if (_homeIntroRunning) return;
            if (_homeLiveUpdating)
            {
                if (!animateSample)
                    ApplyHomeGaugeSample(animate: false);
                return;
            }
            _homeLiveUpdating = true;
            _homeGaugeTimer ??= new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            _homeGaugeTimer.Interval = TimeSpan.FromMilliseconds(1000);
            _homeGaugeTimer.Tick -= HomeGaugeTimer_Tick;
            _homeGaugeTimer.Tick += HomeGaugeTimer_Tick;
            _homeGaugeTimer.Start();
            ApplyHomeGaugeSample(animateSample);
        }

        private void StopHomeGaugeLiveUpdates()
        {
            _homeLiveUpdating = false;
            if (_homeGaugeTimer != null)
                _homeGaugeTimer.Stop();
            GaugeCpu?.StopAnimation();
            GaugeGpu?.StopAnimation();
            GaugeRam?.StopAnimation();
        }

        private void ArmHomeIntroWatchdog(int gen)
        {
            _homeIntroWatchdog?.Stop();
            _homeIntroWatchdog = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _homeIntroWatchdog.Tick += (_, __) =>
            {
                _homeIntroWatchdog?.Stop();
                _homeIntroWatchdog = null;
                CompleteHomeIntro(gen);
            };
            _homeIntroWatchdog.Start();
        }

        private void CompleteHomeIntro(int gen)
        {
            if (gen != _homeIntroGen) return;
            _homeIntroWatchdog?.Stop();
            _homeIntroWatchdog = null;
            if (!_homeIntroRunning) return;
            _homeIntroRunning = false;
            _homeIntroClock.Stop();
            if (string.Equals(_currentPanel, "Home", StringComparison.Ordinal))
                StartHomeGaugeLiveUpdates();
        }

        private void HomeGaugeTimer_Tick(object? sender, EventArgs e)
        {
            if (_homeIntroRunning) return;
            if (!_homeLiveUpdating) return;
            if (!string.Equals(_currentPanel, "Home", StringComparison.Ordinal)) return;
            ApplyHomeGaugeSample(animate: true);
        }

        private void ApplyHomeGaugeSample(bool animate)
        {
            if (GaugeCpu == null || GaugeGpu == null) return;

            double cpu = Math.Max(0, _hwManager.GetCpuTemperature());
            double gpu = Math.Max(0, _hwManager.GetGpuTemperature(_config.SelectedGpuName));
            var (ramLoad, ramUsed, ramTotal) = _hwManager.GetRamSnapshot();
            double ram = Math.Clamp(ramLoad, 0, 100);
            string ramCaption = ramTotal > 0.1f ? $"{ramUsed:F1}/{ramTotal:F1} GB" : "";

            if (!GaugeCpu.ShowValue) GaugeCpu.ShowValue = true;
            if (!GaugeGpu.ShowValue) GaugeGpu.ShowValue = true;

            if (animate)
            {
                AnimateGaugeLiveValue(GaugeCpu, cpu);
                AnimateGaugeLiveValue(GaugeGpu, gpu);
            }
            else
            {
                GaugeCpu.StopAnimation();
                GaugeGpu.StopAnimation();
                GaugeCpu.Value = cpu;
                GaugeGpu.Value = gpu;
            }

            if (GaugeRam != null)
            {
                if (!GaugeRam.ShowValue) GaugeRam.ShowValue = true;
                GaugeRam.UsageCaption = ramCaption;
                if (animate)
                    AnimateRamLiveValue(GaugeRam, ram);
                else
                {
                    GaugeRam.StopAnimation();
                    GaugeRam.Value = ram;
                }
            }
        }

        private void QueueOverclockStatusRefresh()
        {
            if (_ocStatusQueued) return;
            _ocStatusQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _ocStatusQueued = false;
                if (!string.Equals(_currentPanel, "Overclock", StringComparison.Ordinal))
                    return;
                RefreshOverclockStatusUi();
            }), DispatcherPriority.Background);
        }

        private void QueueFanStatusRefresh()
        {
            if (_fanStatusQueued) return;
            _fanStatusQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _fanStatusQueued = false;
                if (!string.Equals(_currentPanel, "Fans", StringComparison.Ordinal))
                    return;
                RefreshFanStatusUi();
            }), DispatcherPriority.Background);
        }

        private static void ResetPanelMotion(UIElement panel)
        {
            panel.BeginAnimation(UIElement.OpacityProperty, null);
            panel.Opacity = 1;
            if (panel.RenderTransform is TranslateTransform slide)
            {
                slide.BeginAnimation(TranslateTransform.YProperty, null);
                slide.Y = 0;
            }
        }

        private static void AnimateGaugeLiveValue(TempGaugeControl gauge, double target)
        {
            double current = gauge.Value;
            double delta = Math.Abs(target - current);
            if (delta < 0.25)
            {
                gauge.Value = target;
                return;
            }

            int durationMs = delta switch
            {
                < 2 => 380,
                < 6 => 520,
                < 12 => 680,
                _ => 860
            };

            gauge.AnimateTo(target, TimeSpan.FromMilliseconds(durationMs), easing: TempGaugeControl.EaseOutCubic);
        }

        private static void AnimateRamLiveValue(RamGaugeControl gauge, double target)
        {
            double current = gauge.Value;
            double delta = Math.Abs(target - current);
            if (delta < 0.25)
            {
                gauge.Value = target;
                return;
            }

            int durationMs = delta switch
            {
                < 2 => 380,
                < 6 => 520,
                < 12 => 680,
                _ => 860
            };

            gauge.AnimateTo(target, TimeSpan.FromMilliseconds(durationMs), easing: RamGaugeControl.EaseOutCubic);
        }

        private static void SetPanelShown(UIElement? panel, bool shown)
        {
            if (panel == null) return;
            var desired = shown ? Visibility.Visible : Visibility.Collapsed;
            if (panel.Visibility != desired)
                panel.Visibility = desired;
        }

        private static void AnimatePanelIn(UIElement panel)
        {
            panel.BeginAnimation(UIElement.OpacityProperty, null);

            if (panel.RenderTransform is not TranslateTransform slide)
            {
                slide = new TranslateTransform();
                panel.RenderTransform = slide;
            }
            else
            {
                slide.BeginAnimation(TranslateTransform.YProperty, null);
            }

            // Start partly visible so the tab doesn't blank for a frame, then ease in.
            panel.Opacity = 0.72;
            slide.Y = 6;

            var fade = new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            var rise = new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            fade.Completed += (_, __) =>
            {
                panel.Opacity = 1;
                slide.Y = 0;
                panel.BeginAnimation(UIElement.OpacityProperty, null);
                slide.BeginAnimation(TranslateTransform.YProperty, null);
            };
            panel.BeginAnimation(UIElement.OpacityProperty, fade);
            slide.BeginAnimation(TranslateTransform.YProperty, rise);
        }

        private void PopulateGpuSelector()
        {
            _hwManager.RefreshAvailableGpus();
            _hwManager.EnsureSelectedGpu(_config);

            CmbGpuSelector.Items.Clear();
            List<string> gpus = new List<string>(_hwManager.AvailableGpus);
            if (gpus.Count == 0)
                gpus.Add("Bilinmeyen GPU / Unknown GPU");

            foreach (var gpu in gpus)
                CmbGpuSelector.Items.Add(gpu);

            if (!string.IsNullOrEmpty(_config.SelectedGpuName))
            {
                int idx = gpus.FindIndex(g =>
                    g.Equals(_config.SelectedGpuName, StringComparison.OrdinalIgnoreCase));
                if (idx < 0)
                {
                    idx = gpus.FindIndex(g =>
                        g.Contains(_config.SelectedGpuName, StringComparison.OrdinalIgnoreCase) ||
                        _config.SelectedGpuName.Contains(g, StringComparison.OrdinalIgnoreCase));
                }
                CmbGpuSelector.SelectedIndex = idx >= 0 ? idx : 0;
            }
            else if (gpus.Count > 0)
            {
                CmbGpuSelector.SelectedIndex = 0;
            }

            // Combo may not fire SelectionChanged on first bind — sync config explicitly
            if (CmbGpuSelector.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
            {
                if (!string.Equals(_config.SelectedGpuName, selected, StringComparison.Ordinal))
                {
                    _config.SelectedGpuName = selected;
                    _config.Save();
                }
            }
        }

        public void RefreshGpuSelector()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshGpuSelector));
                return;
            }
            PopulateGpuSelector();
        }

        private void LoadSettingsToUI()
        {
            ChkShowFps.IsChecked = _config.ShowFps;
            ChkShowFrametime.IsChecked = _config.ShowFrametime;
            ChkShowOnePercent.IsChecked = _config.ShowOnePercentLow;
            ChkShowGpuName.IsChecked = _config.ShowGpuName;
            ChkShowCpu.IsChecked = _config.ShowCpuTemp;
            ChkShowCpuLoad.IsChecked = _config.ShowCpuLoad;
            ChkShowGpu.IsChecked = _config.ShowGpuTemp;
            ChkShowGpuLoad.IsChecked = _config.ShowGpuLoad;
            ChkShowRam.IsChecked = _config.ShowRamUsage;
            ChkShowVram.IsChecked = _config.ShowVramUsage;
            ChkShowOc.IsChecked = _config.ShowOverclockStatus;
            if (ChkShowFan != null) ChkShowFan.IsChecked = _config.ShowFanSpeed;
            ChkShowClock.IsChecked = _config.ShowClock;
            ChkPositionUnlock.IsChecked = !_config.PositionLocked;

            switch (_config.OcControlMode)
            {
                case OcControlMode.AutoThermal: RadOcAuto.IsChecked = true; break;
                case OcControlMode.ManualFixed: RadOcManual.IsChecked = true; break;
                default: RadOcOff.IsChecked = true; break;
            }

            ReloadOcProfileLists();
            CmbManualProfile.IsEnabled = _config.OcControlMode == OcControlMode.ManualFixed;
            if (PanelManualProfile != null)
                PanelManualProfile.Visibility = _config.OcControlMode == OcControlMode.ManualFixed
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            LoadFanSettingsToUi();

            SliderFontSize.Value = _config.FontSize;
            SliderPadding.Value = _config.PositionPadding;

            for (int i = 0; i < CmbLanguage.Items.Count; i++)
            {
                if (CmbLanguage.Items[i] is ComboBoxItem item && item.Tag?.ToString() == _config.Language)
                {
                    CmbLanguage.SelectedIndex = i;
                    break;
                }
            }

            for (int i = 0; i < CmbProfile.Items.Count; i++)
            {
                if (CmbProfile.Items[i] is ComboBoxItem item && item.Tag?.ToString() == _config.OverlayProfileIndex.ToString())
                {
                    CmbProfile.SelectedIndex = i;
                    break;
                }
            }

            switch (_config.PositionPreset)
            {
                case OverlayPositionPreset.TopLeft: PosTL.IsChecked = true; break;
                case OverlayPositionPreset.TopCenter: PosTC.IsChecked = true; break;
                case OverlayPositionPreset.TopRight: PosTR.IsChecked = true; break;
                case OverlayPositionPreset.MiddleLeft: PosML.IsChecked = true; break;
                case OverlayPositionPreset.Center: PosMC.IsChecked = true; break;
                case OverlayPositionPreset.MiddleRight: PosMR.IsChecked = true; break;
                case OverlayPositionPreset.BottomLeft: PosBL.IsChecked = true; break;
                case OverlayPositionPreset.BottomCenter: PosBC.IsChecked = true; break;
                case OverlayPositionPreset.BottomRight: PosBR.IsChecked = true; break;
            }

            UpdateColorPreview();
        }

        private void ApplyLanguage()
        {
            _s = UiStrings.For(_config.Language);
            var s = _s;

            Title = s.Title;
            if (LblBrandTitle != null) LblBrandTitle.Text = s.SplashTitle;
            LblBrandSubtitle.Text = s.SplashSubtitle;
            if (LblTitleVersion != null) LblTitleVersion.Text = s.AboutVersion;
            LblNavHeader.Text = s.NavHeader;
            if (NavHomeLabel != null) NavHomeLabel.Text = s.NavHome;
            if (NavLibraryLabel != null) NavLibraryLabel.Text = s.NavLibrary;
            if (NavOverlayLabel != null) NavOverlayLabel.Text = s.NavOverlay;
            if (NavSensorsLabel != null) NavSensorsLabel.Text = s.NavSensors;
            if (NavDisplayLabel != null) NavDisplayLabel.Text = s.NavDisplay;
            if (NavOverclockLabel != null) NavOverclockLabel.Text = s.NavOverclock;
            if (NavFansLabel != null) NavFansLabel.Text = s.NavFans;
            if (NavAboutLabel != null) NavAboutLabel.Text = s.NavAbout;

            if (PanelLibrary != null)
                PanelLibrary.ApplyStrings(s);

            if (LblPageHome != null) LblPageHome.Text = s.PageHome;
            if (LblPageHomeDesc != null) LblPageHomeDesc.Text = s.PageHomeDesc;
            if (LblHomeGaugesHeader != null) LblHomeGaugesHeader.Text = s.HomeGaugesHeader;
            if (GaugeCpu != null)
            {
                GaugeCpu.Title = s.HomeCpuGauge;
                GaugeCpu.Subtitle = s.HomeTempSubtitle;
            }
            if (GaugeGpu != null)
            {
                GaugeGpu.Title = s.HomeGpuGauge;
                GaugeGpu.Subtitle = s.HomeTempSubtitle;
            }
            if (GaugeRam != null)
            {
                GaugeRam.Title = s.HomeRamGauge;
                GaugeRam.Subtitle = s.HomeRamSubtitle;
            }

            LblPageOverlay.Text = s.PageOverlay;
            LblPageOverlayDesc.Text = s.PageOverlayDesc;
            LblPageSensors.Text = s.PageSensors;
            LblPageSensorsDesc.Text = s.PageSensorsDesc;
            LblPageDisplay.Text = s.PageDisplay;
            LblPageDisplayDesc.Text = s.PageDisplayDesc;
            LblPageOverclock.Text = s.PageOverclock;
            LblPageOverclockDesc.Text = s.PageOverclockDesc;
            if (LblPageFans != null) LblPageFans.Text = s.PageFans;
            if (LblPageFansDesc != null) LblPageFansDesc.Text = s.PageFansDesc;
            if (BtnFanBetaInfo != null) BtnFanBetaInfo.ToolTip = s.FanBetaInfoTooltip;
            LblPageAbout.Text = s.PageAbout;
            LblPageAboutDesc.Text = s.PageAboutDesc;
            LblAboutBody.Text = s.AboutBody;
            if (LblAboutBrand != null) LblAboutBrand.Text = s.BrandName;
            LblAboutVersion.Text = s.AboutVersion;
            if (BtnCheckUpdates != null) BtnCheckUpdates.Content = s.CheckUpdates;
            if (BtnOpenDebugLog != null) BtnOpenDebugLog.Content = s.OpenDebugLog;
            if (BtnOpenLogFolder != null) BtnOpenLogFolder.Content = s.OpenLogFolder;
            LblPreview.Text = s.Preview;

            LblLanguage.Text = s.Lang;
            LblProfile.Text = s.Profile;
            LblGpuSelect.Text = s.GpuSelect;
            LblAppearance.Text = s.Appearance;
            LblOverlayColor.Text = s.OverlayColor;
            BtnCustomColor.Content = s.CustomColor;
            LblFontSize.Text = s.FontSize;
            LblPosition.Text = s.Position;
            LblPadding.Text = s.Padding;
            LblSensors.Text = s.Sensors;
            LblOverlayCtrl.Text = s.OverlayCtrl;
            LblOverlayToggle.Text = s.OverlayToggle;
            ChkShowGpuName.Content = s.ShowGpuName;
            ChkShowFps.Content = s.ShowFps;
            ChkShowFrametime.Content = s.ShowFrametime;
            ChkShowOnePercent.Content = s.ShowOnePercent;
            ChkShowCpu.Content = s.ShowCpu;
            ChkShowCpuLoad.Content = s.ShowCpuLoad;
            ChkShowGpu.Content = s.ShowGpu;
            ChkShowGpuLoad.Content = s.ShowGpuLoad;
            ChkShowRam.Content = s.ShowRam;
            ChkShowVram.Content = s.ShowVram;
            ChkShowOc.Content = s.ShowOc;
            if (ChkShowFan != null) ChkShowFan.Content = s.ShowFanSpeed;
            ChkShowClock.Content = s.ShowClock;
            ChkPositionUnlock.Content = s.PosUnlock;

            LblOcModeHeader.Text = s.OcModeHeader;
            RadOcOff.Content = s.OcOff;
            RadOcAuto.Content = s.OcAuto;
            RadOcManual.Content = s.OcManual;
            LblOcManualLevel.Text = s.OcManualProfile;
            LblOcActiveHeader.Text = s.OcActiveHeader;
            LblOcTempLabel.Text = s.OcGpuTemp;
            LblOcHotspotLabel.Text = s.OcHotspot;
            LblOcAppliedHeader.Text = s.OcApplied;
            LblOcCoreTag.Text = s.OcCore;
            LblOcMemTag.Text = s.OcMem;
            LblOcPowerTag.Text = s.OcPower;
            BtnOcRestore.Content = s.OcRestore;

            if (LblFanModeHeader != null) LblFanModeHeader.Text = s.FanModeHeader;
            if (RadFanOff != null) RadFanOff.Content = s.FanOff;
            if (RadFanAuto != null) RadFanAuto.Content = s.FanAuto;
            if (RadFanManual != null) RadFanManual.Content = s.FanManual;
            if (LblFanManualPwm != null) LblFanManualPwm.Text = s.FanManualPwm;
            if (LblFanCurvePick != null) LblFanCurvePick.Text = s.FanCurvePick;
            if (LblFanActiveHeader != null) LblFanActiveHeader.Text = s.FanActiveHeader;
            if (LblFanCpuTemp != null) LblFanCpuTemp.Text = s.FanCpuTemp;
            if (LblFanGpuTemp != null) LblFanGpuTemp.Text = s.FanGpuTemp;
            if (BtnFanRestore != null) BtnFanRestore.Content = s.FanRestore;
            if (LblFanChannelsHeader != null) LblFanChannelsHeader.Text = s.FanChannelsHeader;
            if (LblFanEmpty != null) LblFanEmpty.Text = s.FanEmpty;
            if (LblFanCurvesHeader != null) LblFanCurvesHeader.Text = s.FanCurvesHeader;
            if (BtnFanPresetSilent != null) BtnFanPresetSilent.Content = s.FanPresetSilent;
            if (BtnFanPresetBalanced != null) BtnFanPresetBalanced.Content = s.FanPresetBalanced;
            if (BtnFanPresetPerformance != null) BtnFanPresetPerformance.Content = s.FanPresetPerformance;

            LblAiOcHeader.Text = s.AiHeader;
            LblAiOcDesc.Text = s.AiDesc;
            BtnAiOcAsk.Content = s.AiAsk;
            BtnAiOcSaveAll.Content = s.AiSaveAll;
            BtnAiEcoSave.Content = s.AiSaveOne;
            BtnAiPerfSave.Content = s.AiSaveOne;
            BtnAiExtSave.Content = s.AiSaveOne;
            if (_aiSuggestions.Count > 0 && !BtnAiOcAsk.IsEnabled)
                TxtAiOcStatus.Text = _aiStatusFromSplash ? s.AiPrefetched : s.AiReady;
            else if (string.IsNullOrWhiteSpace(TxtAiOcStatus.Text))
                TxtAiOcStatus.Text = s.AiIdle;

            LblOcProfilesHeader.Text = s.ProfilesHeader;
            LblOcEditHeader.Text = s.EditHeader;
            LblOcEditName.Text = s.FieldName;
            LblOcEditMin.Text = s.FieldMin;
            LblOcEditMax.Text = s.FieldMax;
            LblOcEditCore.Text = s.FieldCore;
            LblOcEditMem.Text = s.FieldMem;
            LblOcEditPower.Text = s.FieldPower;
            LblOcEditPowerHint.Text = s.FieldPowerHint;
            TxtOcEditPower.ToolTip = s.FieldPowerHint;
            BtnOcProfileAdd.Content = s.BtnAdd;
            BtnOcProfileSave.Content = s.BtnSave;
            BtnOcProfileDelete.Content = s.BtnDelete;
            BtnOcProfileImport.Content = s.BtnImport;
            BtnOcProfileExport.Content = s.BtnExport;
            BtnOcProfileDefaults.Content = s.BtnDefaults;

            ApplyOverlayProfileNames(s);
            RefreshOverclockStatusUi();
            RefreshFanStatusUi();
        }

        private void ApplyOverlayProfileNames(UiStrings s)
        {
            if (CmbProfile == null) return;
            int selected = CmbProfile.SelectedIndex;
            string[] names =
            {
                s.ProfileClassic, s.ProfileGamer, s.ProfileSteam, s.ProfileAdvanced,
                s.ProfilePill, s.ProfileNeon, s.ProfileTower
            };
            for (int i = 0; i < CmbProfile.Items.Count && i < names.Length; i++)
            {
                if (CmbProfile.Items[i] is ComboBoxItem item)
                    item.Content = names[i];
            }
            if (selected >= 0 && selected < CmbProfile.Items.Count)
                CmbProfile.SelectedIndex = selected;
        }

        public void ApplyAiAssistResult(AiOcAssistResult result, bool fromSplash = false)
        {
            _aiSuggestions = result.Clamped?.Response.Recommendations?.ToList() ?? new List<AiOcRecommendation>();
            RenderAiSuggestionCards();
            _aiStatusFromSplash = fromSplash;
            if (fromSplash)
                TxtAiOcStatus.Text = _s.AiPrefetched;
            else if (result.Success)
                TxtAiOcStatus.Text = _s.AiReady;
            else
                TxtAiOcStatus.Text = result.Message;
            TxtAiOcStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0xC0, 0x80));

            // one AI win per session — button naps after that
            if (result.Success || _aiSuggestions.Count > 0)
                BtnAiOcAsk.IsEnabled = false;

            if (result.Clamped?.AnyClamped == true)
                OcDebugLog.Write("AI clamp: " + string.Join("; ", result.Clamped.ClampLog));
            OcDebugLog.Write($"AI assist source={result.Source} ok={result.Success} msg={result.Message}");
        }

        private void SaveAndApply(bool refreshLanguage = true)
        {
            if (_config == null) return;

            string newLang;
            if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag != null)
                newLang = item.Tag.ToString() ?? "EN";
            else
                newLang = "EN";

            if (CmbProfile.SelectedItem is ComboBoxItem profileItem && profileItem.Tag != null)
                _config.OverlayProfileIndex = int.Parse(profileItem.Tag.ToString() ?? "0");

            _config.SelectedGpuName = CmbGpuSelector.SelectedItem?.ToString() ?? "";

            _config.ShowFps = ChkShowFps.IsChecked == true;
            _config.ShowFrametime = ChkShowFrametime.IsChecked == true;
            _config.ShowOnePercentLow = ChkShowOnePercent.IsChecked == true;
            _config.ShowGpuName = ChkShowGpuName.IsChecked == true;
            _config.ShowCpuTemp = ChkShowCpu.IsChecked == true;
            _config.ShowCpuLoad = ChkShowCpuLoad.IsChecked == true;
            _config.ShowGpuTemp = ChkShowGpu.IsChecked == true;
            _config.ShowGpuLoad = ChkShowGpuLoad.IsChecked == true;
            _config.ShowRamUsage = ChkShowRam.IsChecked == true;
            _config.ShowVramUsage = ChkShowVram.IsChecked == true;
            _config.ShowOverclockStatus = ChkShowOc.IsChecked == true;
            if (ChkShowFan != null) _config.ShowFanSpeed = ChkShowFan.IsChecked == true;
            _config.ShowClock = ChkShowClock.IsChecked == true;
            _config.PositionLocked = ChkPositionUnlock.IsChecked != true;

            _config.FontSize = (int)SliderFontSize.Value;
            _config.PositionPadding = SliderPadding.Value;
            _config.TextColorHex = _selectedColorHex;

            if (PosTL.IsChecked == true) _config.PositionPreset = OverlayPositionPreset.TopLeft;
            else if (PosTC.IsChecked == true) _config.PositionPreset = OverlayPositionPreset.TopCenter;
            else if (PosTR.IsChecked == true) _config.PositionPreset = OverlayPositionPreset.TopRight;
            else if (PosML.IsChecked == true) _config.PositionPreset = OverlayPositionPreset.MiddleLeft;
            else if (PosMC.IsChecked == true) _config.PositionPreset = OverlayPositionPreset.Center;
            else if (PosMR.IsChecked == true) _config.PositionPreset = OverlayPositionPreset.MiddleRight;
            else if (PosBL.IsChecked == true) _config.PositionPreset = OverlayPositionPreset.BottomLeft;
            else if (PosBC.IsChecked == true) _config.PositionPreset = OverlayPositionPreset.BottomCenter;
            else if (PosBR.IsChecked == true) _config.PositionPreset = OverlayPositionPreset.BottomRight;
            else _config.PositionPreset = OverlayPositionPreset.Custom;

            bool languageChanged = !string.Equals(_config.Language, newLang, StringComparison.Ordinal);
            _config.Language = newLang;

            _config.Save();

            if (refreshLanguage && (languageChanged || _lastLanguageApplied != newLang))
            {
                ApplyLanguage();
                _lastLanguageApplied = newLang;
            }

            UpdateColorPreview();
            _onConfigChanged?.Invoke();
            _hwManager.TriggerUpdate();

            _onOverlayToggle?.Invoke(ChkOverlayActive.IsChecked == true);
        }

        private void ScheduleSaveAndApply(bool refreshLanguage = false)
        {
            if (!IsLoaded) return;
            _saveDebounce ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _saveDebounce.Tick -= SaveDebounce_Tick;
            _saveDebounce.Tag = refreshLanguage;
            _saveDebounce.Tick += SaveDebounce_Tick;
            _saveDebounce.Stop();
            _saveDebounce.Start();
        }

        private void SaveDebounce_Tick(object? sender, EventArgs e)
        {
            if (_saveDebounce == null) return;
            _saveDebounce.Stop();
            bool refreshLang = _saveDebounce.Tag is true;
            SaveAndApply(refreshLang);
        }

        private void InteractiveElement_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            // Language combo needs immediate full refresh; everything else can debounce.
            bool isLanguage = ReferenceEquals(sender, CmbLanguage);
            if (isLanguage)
                SaveAndApply(refreshLanguage: true);
            else
                ScheduleSaveAndApply(refreshLanguage: false);
        }

        private void InteractiveElement_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            bool isLanguage = ReferenceEquals(sender, CmbLanguage);
            if (isLanguage)
                SaveAndApply(refreshLanguage: true);
            else
                ScheduleSaveAndApply(refreshLanguage: false);
        }

        private void SliderFontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            if (TxtFontSizeVal != null) TxtFontSizeVal.Text = $"{(int)e.NewValue} px";
            ScheduleSaveAndApply(refreshLanguage: false);
        }

        private void SliderPadding_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            if (TxtPaddingVal != null) TxtPaddingVal.Text = $"{(int)e.NewValue} px";
            ScheduleSaveAndApply(refreshLanguage: false);
        }

        private void BtnCustomColor_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ColorPickerWindow(_config);
            picker.Owner = this;

            if (picker.ShowDialog() == true)
            {
                string newColor = picker.SelectedColorHex;
                _config.TextColorHex = newColor;
                _selectedColorHex = newColor;
                UpdateColorPreview();
                _config.Save();
                SaveAndApply();
            }
        }

        private void UpdateColorPreview()
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_selectedColorHex);
                var brush = new SolidColorBrush(color);
                if (ColorPreview != null) ColorPreview.Background = brush;
                if (PreviewSampleText != null) PreviewSampleText.Foreground = brush;
            }
            catch
            {
                if (ColorPreview != null) ColorPreview.Background = new SolidColorBrush(Colors.Red);
                if (PreviewSampleText != null) PreviewSampleText.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        public void NotifyCustomDrag()
        {
            PosTL.IsChecked = false;
            PosTC.IsChecked = false;
            PosTR.IsChecked = false;
            PosML.IsChecked = false;
            PosMC.IsChecked = false;
            PosMR.IsChecked = false;
            PosBL.IsChecked = false;
            PosBC.IsChecked = false;
            PosBR.IsChecked = false;
            _config.PositionPreset = OverlayPositionPreset.Custom;
            _config.Save();
        }

        private bool _suppressOcProfileUi;
        private bool _suppressOcMode;

        private void OcMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded || _config == null || _suppressOcMode) return;

            OcControlMode next = RadOcAuto.IsChecked == true
                ? OcControlMode.AutoThermal
                : RadOcManual.IsChecked == true
                    ? OcControlMode.ManualFixed
                    : OcControlMode.Off;

            if (_config.OcControlMode == OcControlMode.Off && next != OcControlMode.Off && !ConfirmEnable(_s.ConfirmEnableOc))
            {
                _suppressOcMode = true;
                RadOcOff.IsChecked = true;
                _suppressOcMode = false;
                return;
            }

            _config.OcControlMode = next;

            CmbManualProfile.IsEnabled = _config.OcControlMode == OcControlMode.ManualFixed;
            if (PanelManualProfile != null)
                PanelManualProfile.Visibility = _config.OcControlMode == OcControlMode.ManualFixed
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            EnsureManualProfileSelected();
            _config.Save();
            _ocManager?.SyncFromConfig();
            RefreshOverclockStatusUi();
        }

        private void OcManualProfile_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressOcProfileUi || !this.IsLoaded || _config == null) return;
            if (CmbManualProfile.SelectedItem is OcProfile p)
            {
                _config.ManualProfileId = p.Id;
                _config.Save();
                if (_config.OcControlMode == OcControlMode.ManualFixed)
                {
                    _ocManager?.SyncFromConfig();
                    RefreshOverclockStatusUi();
                }
            }
        }

        private void BtnOcRefresh_Click(object sender, RoutedEventArgs e)
        {
            _ocManager?.Refresh();
            RefreshOverclockStatusUi();
        }

        private void BtnOcRestore_Click(object sender, RoutedEventArgs e)
        {
            RadOcOff.IsChecked = true;
            _config.OcControlMode = OcControlMode.Off;
            _config.Save();
            _ocManager?.RestoreAll();
            CmbManualProfile.IsEnabled = false;
            RefreshOverclockStatusUi();
        }

        private bool _suppressFanUi;

        private void LoadFanSettingsToUi()
        {
            if (RadFanOff == null) return;
            _suppressFanUi = true;
            try
            {
                switch (_config.FanControlMode)
                {
                    case FanControlMode.AutoCurve: RadFanAuto.IsChecked = true; break;
                    case FanControlMode.ManualFixed: RadFanManual.IsChecked = true; break;
                    default: RadFanOff.IsChecked = true; break;
                }

                if (SldFanManual != null)
                    SldFanManual.Value = Math.Clamp(_config.FanManualPwmPercent, 0, 100);
                if (TxtFanManualValue != null)
                    TxtFanManualValue.Text = $"{_config.FanManualPwmPercent}%";
                if (PanelFanManual != null)
                    PanelFanManual.Visibility = _config.FanControlMode == FanControlMode.ManualFixed
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                if (PanelFanCurvePick != null)
                    PanelFanCurvePick.Visibility = _config.FanControlMode == FanControlMode.AutoCurve
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                ReloadFanCurveCombo();
            }
            finally
            {
                _suppressFanUi = false;
            }
        }

        private void ReloadFanCurveCombo()
        {
            if (CmbFanCurve == null) return;
            var curves = _fanManager?.Store.Curves ?? FanCurveStore.CreateDefaultCurves();
            Guid want = _config.FanActiveCurveId;
            CmbFanCurve.Items.Clear();
            foreach (var c in curves)
                CmbFanCurve.Items.Add(c);

            var pick = curves.FirstOrDefault(c => c.Id == want)
                ?? curves.FirstOrDefault(c => c.Id == FanCurveStore.BalancedId)
                ?? curves.FirstOrDefault();
            if (pick != null)
            {
                CmbFanCurve.SelectedItem = CmbFanCurve.Items.Cast<FanCurve>().FirstOrDefault(x => x.Id == pick.Id);
                if (_config.FanActiveCurveId == Guid.Empty)
                    _config.FanActiveCurveId = pick.Id;
                UpdateFanCurvePointsText(pick);
            }
        }

        private void UpdateFanCurvePointsText(FanCurve? curve)
        {
            if (TxtFanCurvePoints == null) return;
            if (curve == null || curve.Points.Count == 0)
            {
                TxtFanCurvePoints.Text = "—";
                return;
            }
            TxtFanCurvePoints.Text = string.Join("   ·   ",
                curve.Points.OrderBy(p => p.TempC).Select(p => $"{p.TempC}°C → {p.PwmPercent}%"));
        }

        private void FanMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressFanUi || !IsLoaded || _config == null) return;

            FanControlMode next = RadFanAuto.IsChecked == true
                ? FanControlMode.AutoCurve
                : RadFanManual.IsChecked == true
                    ? FanControlMode.ManualFixed
                    : FanControlMode.Off;

            if (_config.FanControlMode == FanControlMode.Off && next != FanControlMode.Off && !ConfirmEnable(_s.ConfirmEnableFan))
            {
                _suppressFanUi = true;
                if (RadFanOff != null)
                    RadFanOff.IsChecked = true;
                _suppressFanUi = false;
                return;
            }

            _config.FanControlMode = next;

            if (PanelFanManual != null)
                PanelFanManual.Visibility = _config.FanControlMode == FanControlMode.ManualFixed
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            if (PanelFanCurvePick != null)
                PanelFanCurvePick.Visibility = _config.FanControlMode == FanControlMode.AutoCurve
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            _config.Save();
            _fanManager?.SyncFromConfig();
            RefreshFanStatusUi();
        }

        private void FanManual_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressFanUi || !IsLoaded || _config == null || SldFanManual == null) return;
            int pwm = (int)Math.Round(SldFanManual.Value);
            _config.FanManualPwmPercent = Math.Clamp(pwm, 0, 100);
            if (TxtFanManualValue != null)
                TxtFanManualValue.Text = $"{_config.FanManualPwmPercent}%";
            _config.Save();
            if (_config.FanControlMode == FanControlMode.ManualFixed)
                _fanManager?.SyncFromConfig();
        }

        private void FanCurve_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFanUi || !IsLoaded || _config == null) return;
            if (CmbFanCurve.SelectedItem is FanCurve curve)
            {
                _config.FanActiveCurveId = curve.Id;
                _config.Save();
                UpdateFanCurvePointsText(curve);
                if (_config.FanControlMode == FanControlMode.AutoCurve)
                    _fanManager?.SyncFromConfig();
            }
        }

        private void BtnFanPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string tag)
                return;
            Guid id = tag.Equals("Silent", StringComparison.OrdinalIgnoreCase) ? FanCurveStore.SilentId
                : tag.Equals("Performance", StringComparison.OrdinalIgnoreCase) ? FanCurveStore.PerformanceId
                : FanCurveStore.BalancedId;
            _config.FanActiveCurveId = id;
            _config.Save();
            _suppressFanUi = true;
            try
            {
                ReloadFanCurveCombo();
                if (RadFanAuto != null)
                    RadFanAuto.IsChecked = true;
            }
            finally
            {
                _suppressFanUi = false;
            }
            _config.FanControlMode = FanControlMode.AutoCurve;
            _config.Save();
            if (PanelFanManual != null) PanelFanManual.Visibility = Visibility.Collapsed;
            if (PanelFanCurvePick != null) PanelFanCurvePick.Visibility = Visibility.Visible;
            _fanManager?.SyncFromConfig();
            RefreshFanStatusUi();
        }

        private void BtnFanRestore_Click(object sender, RoutedEventArgs e)
        {
            _suppressFanUi = true;
            try
            {
                if (RadFanOff != null) RadFanOff.IsChecked = true;
                if (PanelFanManual != null) PanelFanManual.Visibility = Visibility.Collapsed;
                if (PanelFanCurvePick != null) PanelFanCurvePick.Visibility = Visibility.Collapsed;
            }
            finally
            {
                _suppressFanUi = false;
            }
            _config.FanControlMode = FanControlMode.Off;
            _config.Save();
            _fanManager?.RestoreAll();
            RefreshFanStatusUi();
        }

        private void RefreshFanStatusUi()
        {
            if (TxtFanLevel == null) return;
            var status = _fanManager?.Status;
            if (status == null)
            {
                TxtFanLevel.Text = "—";
                return;
            }

            TxtFanLevel.Text = status.FailClosed
                ? _s.FanFailClosed
                : status.ActiveCurveName;

            string cpuTxt = status.LastCpuTempC is float cpu ? $"{cpu:F0}°C" : "—";
            string gpuTxt = status.LastGpuTempC is float gpu ? $"{gpu:F0}°C" : "—";
            if (TxtFanCpuTemp.Text != cpuTxt) TxtFanCpuTemp.Text = cpuTxt;
            if (TxtFanGpuTemp.Text != gpuTxt) TxtFanGpuTemp.Text = gpuTxt;

            if (TxtFanBackend != null)
            {
                string backend = string.IsNullOrWhiteSpace(status.BackendSummary) ? "" : status.BackendSummary;
                if (TxtFanBackend.Text != backend) TxtFanBackend.Text = backend;
            }
            if (TxtFanStatus != null)
            {
                string msg = status.StatusMessage ?? "";
                if (TxtFanStatus.Text != msg) TxtFanStatus.Text = msg;
            }

            bool writable = status.HasWritableChannel;
            if (RadFanAuto != null) RadFanAuto.IsEnabled = writable;
            if (RadFanManual != null) RadFanManual.IsEnabled = writable;
            if (CmbFanCurve != null) CmbFanCurve.IsEnabled = writable;
            if (SldFanManual != null) SldFanManual.IsEnabled = writable;

            if (TxtFanPawnHint != null)
            {
                TxtFanPawnHint.Text = writable ? "" : (status.HasReadableChannel ? _s.FanReadOnlyHint : _s.FanUnsupported);
                TxtFanPawnHint.Visibility = string.IsNullOrWhiteSpace(TxtFanPawnHint.Text)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            if (LblFanEmpty != null)
                LblFanEmpty.Visibility = status.Channels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            SyncFanChannelRows(status);

            if (CmbFanCurve?.SelectedItem is FanCurve selected)
                UpdateFanCurvePointsText(selected);
        }

        private void SyncFanChannelRows(FanControlStatus status)
        {
            var incoming = status.Channels ?? new List<FanChannelStatus>();
            var byId = incoming.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

            for (int i = _fanRows.Count - 1; i >= 0; i--)
            {
                if (!byId.ContainsKey(_fanRows[i].Id))
                    _fanRows.RemoveAt(i);
            }

            foreach (var ch in incoming)
            {
                var row = _fanRows.FirstOrDefault(r =>
                    string.Equals(r.Id, ch.Id, StringComparison.OrdinalIgnoreCase));
                if (row == null)
                {
                    row = new FanChannelRowVm { Id = ch.Id };
                    _fanRows.Add(row);
                }

                row.Name = ch.Name;
                row.Backend = ch.BackendName ?? "";
                row.Badge = ch.CanWritePwm ? _s.FanWritable : _s.FanReadOnly;
                row.RpmText = ch.Rpm is > 0 ? $"{ch.Rpm:F0} RPM" : "—";
                double pwmVal = ch.AppliedPwm is int a ? a
                    : ch.PwmPercent is float p ? p
                    : 0;
                row.PwmText = pwmVal > 0 ? $"{pwmVal:F0}% PWM" : "—";
            }
        }

        private void ReloadOcProfileLists(Guid? selectId = null)
        {
            if (LstOcProfiles == null || CmbManualProfile == null) return;
            var store = _ocManager?.ProfileStore;
            if (store == null) return;

            _suppressOcProfileUi = true;
            try
            {
                var profiles = store.Profiles;
                Guid want = selectId
                    ?? (LstOcProfiles.SelectedItem is OcProfile cur ? cur.Id : _config.ManualProfileId);

                LstOcProfiles.Items.Clear();
                CmbManualProfile.Items.Clear();
                foreach (var p in profiles)
                {
                    LstOcProfiles.Items.Add(p);
                    CmbManualProfile.Items.Add(p);
                }

                OcProfile? pick = profiles.FirstOrDefault(p => p.Id == want)
                    ?? profiles.FirstOrDefault(p => p.ProfileName.Equals("Performance", StringComparison.OrdinalIgnoreCase))
                    ?? profiles.FirstOrDefault();

                if (pick != null)
                {
                    LstOcProfiles.SelectedItem = LstOcProfiles.Items.Cast<OcProfile>().FirstOrDefault(x => x.Id == pick.Id);
                    CmbManualProfile.SelectedItem = CmbManualProfile.Items.Cast<OcProfile>().FirstOrDefault(x => x.Id == pick.Id);
                    if (_config.ManualProfileId == Guid.Empty)
                        _config.ManualProfileId = pick.Id;
                    FillOcEditForm(pick);
                }
            }
            finally
            {
                _suppressOcProfileUi = false;
            }
        }

        private void EnsureManualProfileSelected()
        {
            if (_config.ManualProfileId == Guid.Empty && CmbManualProfile.SelectedItem is OcProfile p)
                _config.ManualProfileId = p.Id;
        }

        private void LstOcProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressOcProfileUi) return;
            if (LstOcProfiles.SelectedItem is OcProfile p)
                FillOcEditForm(p);
        }

        private void FillOcEditForm(OcProfile p)
        {
            TxtOcEditName.Text = p.ProfileName;
            TxtOcEditMin.Text = p.MinTemp.ToString();
            TxtOcEditMax.Text = p.MaxTemp.ToString();
            TxtOcEditCore.Text = p.CoreOffsetMhz.ToString();
            TxtOcEditMem.Text = p.MemoryOffsetMhz.ToString();
            TxtOcEditPower.Text = p.PowerLimitPercent?.ToString() ?? "";
            TxtOcProfileMsg.Text = "";
        }

        private bool TryReadEditForm(out OcProfile profile, out string error)
        {
            profile = new OcProfile();
            error = "";
            if (LstOcProfiles.SelectedItem is OcProfile selected)
                profile.Id = selected.Id;
            else
                profile.Id = Guid.NewGuid();

            profile.ProfileName = (TxtOcEditName.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(profile.ProfileName))
            {
                error = "profile_name required";
                return false;
            }
            if (!int.TryParse(TxtOcEditMin.Text, out int min) || !int.TryParse(TxtOcEditMax.Text, out int max))
            {
                error = "min_temp / max_temp must be integers";
                return false;
            }
            if (!int.TryParse(TxtOcEditCore.Text, out int core) || !int.TryParse(TxtOcEditMem.Text, out int mem))
            {
                error = "core/memory offsets must be integers";
                return false;
            }
            string plRaw = (TxtOcEditPower.Text ?? "").Trim();
            int? pl = null;
            if (!string.IsNullOrEmpty(plRaw))
            {
                if (!int.TryParse(plRaw, out int plVal))
                {
                    error = "power_limit_percent must be empty or integer";
                    return false;
                }
                pl = plVal;
            }

            profile.MinTemp = min;
            profile.MaxTemp = max;
            profile.CoreOffsetMhz = core;
            profile.MemoryOffsetMhz = mem;
            profile.PowerLimitPercent = pl;
            return true;
        }

        private void BtnOcProfileAdd_Click(object sender, RoutedEventArgs e)
        {
            var store = _ocManager?.ProfileStore;
            if (store == null) return;
            try
            {
                var p = new OcProfile
                {
                    ProfileName = "New Profile",
                    MinTemp = 0,
                    MaxTemp = 70,
                    CoreOffsetMhz = 0,
                    MemoryOffsetMhz = 0,
                    PowerLimitPercent = null
                };
                store.Add(p);
                ReloadOcProfileLists(p.Id);
                TxtOcProfileMsg.Text = _s.MsgAdded;
                TxtOcProfileMsg.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0xC0, 0x80));
            }
            catch (Exception ex)
            {
                TxtOcProfileMsg.Text = ex.Message;
                TxtOcProfileMsg.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x80, 0x80));
            }
        }

        private void BtnOcProfileSave_Click(object sender, RoutedEventArgs e)
        {
            var store = _ocManager?.ProfileStore;
            if (store == null) return;
            if (!TryReadEditForm(out var profile, out string error))
            {
                TxtOcProfileMsg.Text = error;
                TxtOcProfileMsg.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x80, 0x80));
                return;
            }
            bool limited = OcHardwareLimits.ClampProfile(profile);
            try
            {
                if (store.GetById(profile.Id) == null)
                    store.Add(profile);
                else
                    store.Update(profile);
                ReloadOcProfileLists(profile.Id);
                _ocManager?.Refresh();
                TxtOcProfileMsg.Text = limited ? _s.OcValuesLimited : _s.MsgSaved;
                TxtOcProfileMsg.Foreground = limited
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF2, 0x4C, 0x1D))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0xC0, 0x80));
            }
            catch (Exception ex)
            {
                TxtOcProfileMsg.Text = ex.Message;
                TxtOcProfileMsg.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x80, 0x80));
            }
        }

        private void BtnOcProfileDelete_Click(object sender, RoutedEventArgs e)
        {
            var store = _ocManager?.ProfileStore;
            if (store == null || LstOcProfiles.SelectedItem is not OcProfile p) return;
            if (System.Windows.MessageBox.Show(_s.ConfirmDelete, "Overclock", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            store.Remove(p.Id);
            if (_config.ManualProfileId == p.Id)
                _config.ManualProfileId = Guid.Empty;
            ReloadOcProfileLists();
            EnsureManualProfileSelected();
            _config.Save();
            _ocManager?.SyncFromConfig();
        }

        private void BtnOcProfileImport_Click(object sender, RoutedEventArgs e)
        {
            var store = _ocManager?.ProfileStore;
            if (store == null) return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "OC Profiles (*.json)|*.json|All files|*.*",
                Title = "Import OC profiles"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var merge = System.Windows.MessageBox.Show(
                    _s.ConfirmImport,
                    "Import",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                if (merge == MessageBoxResult.Cancel) return;
                store.ImportFromFile(dlg.FileName, replaceExisting: merge == MessageBoxResult.Yes);
                ReloadOcProfileLists();
                TxtOcProfileMsg.Text = _s.MsgImported;
                TxtOcProfileMsg.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0xC0, 0x80));
                _ocManager?.Refresh();
            }
            catch (Exception ex)
            {
                TxtOcProfileMsg.Text = ex.Message;
                TxtOcProfileMsg.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x80, 0x80));
            }
        }

        private void BtnOcProfileExport_Click(object sender, RoutedEventArgs e)
        {
            var store = _ocManager?.ProfileStore;
            if (store == null) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "OC Profiles (*.json)|*.json",
                FileName = "oc_profiles.json",
                Title = "Export OC profiles"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                store.ExportToFile(dlg.FileName);
                TxtOcProfileMsg.Text = _s.MsgExported;
                TxtOcProfileMsg.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0xC0, 0x80));
            }
            catch (Exception ex)
            {
                TxtOcProfileMsg.Text = ex.Message;
                TxtOcProfileMsg.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x80, 0x80));
            }
        }

        private void BtnOcProfileDefaults_Click(object sender, RoutedEventArgs e)
        {
            var store = _ocManager?.ProfileStore;
            if (store == null) return;
            if (System.Windows.MessageBox.Show(_s.ConfirmDefaults, "Overclock",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            store.ResetToDefaults();
            ReloadOcProfileLists();
            _ocManager?.Refresh();
        }

        private async void BtnAiOcAsk_Click(object sender, RoutedEventArgs e)
        {
            if (_aiBusy || _ocManager == null || !BtnAiOcAsk.IsEnabled) return;
            _aiBusy = true;
            BtnAiOcAsk.IsEnabled = false;
            TxtAiOcStatus.Text = _s.AiLoading;
            TxtAiOcStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x93, 0xA3));

            bool keepDisabled = false;
            try
            {
                using var client = new AiOcAssistantClient(_config);
                string vendor = _ocManager.Status.GpuVendor;
                var result = await client.RequestSuggestionsAsync(_hwManager, vendor).ConfigureAwait(true);
                ApplyAiAssistResult(result);
                keepDisabled = result.Success || _aiSuggestions.Count > 0;
            }
            catch (Exception ex)
            {
                TxtAiOcStatus.Text = ex.Message;
                TxtAiOcStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x80, 0x80));
                OcDebugLog.Write("AI ask failed: " + ex);
            }
            finally
            {
                _aiBusy = false;
                if (!keepDisabled)
                    BtnAiOcAsk.IsEnabled = true;
            }
        }

        private void RenderAiSuggestionCards()
        {
            void Fill(
                AiOcRecommendation? r,
                Border card,
                TextBlock offsets,
                TextBlock band,
                TextBlock pl,
                TextBlock why,
                System.Windows.Controls.Button saveBtn)
            {
                bool ok = r != null;
                card.Opacity = ok ? 1 : 0.45;
                saveBtn.IsEnabled = ok;
                if (!ok)
                {
                    offsets.Text = "— / —";
                    band.Text = "°C —";
                    pl.Text = "PL —";
                    why.Text = "";
                    return;
                }

                offsets.Text = $"+{r!.CoreOffsetMhz} / +{r.MemoryOffsetMhz}";
                band.Text = $"{r.MinTemp}–{r.MaxTemp} °C";
                pl.Text = r.PowerLimitPercent is int p ? $"PL {p}%" : $"PL {_s.OcPowerStock}";
                why.Text = r.Rationale ?? "";
            }

            var eco = _aiSuggestions.FirstOrDefault(x => x.Mode == "Eco");
            var perf = _aiSuggestions.FirstOrDefault(x => x.Mode == "Performance");
            var ext = _aiSuggestions.FirstOrDefault(x => x.Mode == "Extreme");

            Fill(eco, CardAiEco, TxtAiEcoOffsets, TxtAiEcoBand, TxtAiEcoPl, TxtAiEcoWhy, BtnAiEcoSave);
            Fill(perf, CardAiPerf, TxtAiPerfOffsets, TxtAiPerfBand, TxtAiPerfPl, TxtAiPerfWhy, BtnAiPerfSave);
            Fill(ext, CardAiExt, TxtAiExtOffsets, TxtAiExtBand, TxtAiExtPl, TxtAiExtWhy, BtnAiExtSave);

            BtnAiOcSaveAll.IsEnabled = _aiSuggestions.Count > 0;

            foreach (var card in new[] { CardAiEco, CardAiPerf, CardAiExt })
            {
                if (card.Opacity < 1) continue;
                card.Opacity = 1.0;
                card.RenderTransform = new ScaleTransform(0.96, 0.96);
                card.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                var sx = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };
                ((ScaleTransform)card.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, sx);
                ((ScaleTransform)card.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, sx);
            }
        }

        private void BtnAiOcSaveOne_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string mode) return;
            var store = _ocManager?.ProfileStore;
            if (store == null) return;

            var rec = _aiSuggestions.FirstOrDefault(x => x.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase));
            if (rec == null) return;

            int hwPl = _config.AiOcMaxPowerLimitPercent > 0
                ? _config.AiOcMaxPowerLimitPercent
                : AiOcSafetyClamp.MaxPowerLimitPercent;

            // clamp AGAIN on save — trust issues with AI numbers fr
            var clamped = AiOcSafetyClamp.ClampOne(rec, hwPl);
            if (clamped == null) return;

            try
            {
                var profile = clamped.ToProfile();
                store.Add(profile);
                ReloadOcProfileLists(profile.Id);
                TxtAiOcStatus.Text = _s.AiSavedOne;
                TxtAiOcStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0xC0, 0x80));
            }
            catch (Exception ex)
            {
                TxtAiOcStatus.Text = ex.Message;
                TxtAiOcStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x80, 0x80));
            }
        }

        private void BtnAiOcSaveAll_Click(object sender, RoutedEventArgs e)
        {
            var store = _ocManager?.ProfileStore;
            if (store == null || _aiSuggestions.Count == 0) return;

            int hwPl = _config.AiOcMaxPowerLimitPercent > 0 ? _config.AiOcMaxPowerLimitPercent : AiOcSafetyClamp.MaxPowerLimitPercent;
            int saved = 0;
            Guid? lastId = null;
            try
            {
                foreach (var rec in _aiSuggestions)
                {
                    var clamped = AiOcSafetyClamp.ClampOne(rec, hwPl);
                    if (clamped == null) continue;
                    var profile = clamped.ToProfile();
                    store.Add(profile);
                    lastId = profile.Id;
                    saved++;
                }
                ReloadOcProfileLists(lastId);
                TxtAiOcStatus.Text = _s.AiSavedAll;
                TxtAiOcStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0xC0, 0x80));
            }
            catch (Exception ex)
            {
                TxtAiOcStatus.Text = ex.Message;
                TxtAiOcStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x80, 0x80));
            }
        }

        private void RefreshOverclockStatusUi()
        {
            if (TxtOcLevel == null) return;

            var status = _ocManager?.Status;
            if (status == null)
            {
                TxtOcLevel.Text = "—";
                TxtOcTemp.Text = "—";
                TxtOcHotspot.Text = "—";
                return;
            }

            float? core = status.LastCoreTempC;
            float? hotspot = status.LastHotspotTempC;
            if (core == null || hotspot == null)
            {
                try
                {
                    var sample = _hwManager.GetGpuThermalSample(_config.SelectedGpuName);
                    core ??= sample.CoreTempC;
                    hotspot ??= sample.HotspotTempC;
                    status.LastCoreTempC = core;
                    status.LastHotspotTempC = hotspot;
                }
                catch { }
            }

            if (status.ControlMode == OcControlMode.AutoThermal && !status.GameActive)
                TxtOcLevel.Text = _s.LanguageCode switch
                {
                    "TR" => "Auto · oyun bekleniyor",
                    "DE" => "Auto · warte auf Spiel",
                    "RU" => "Auto · ожидание игры",
                    "AZ" => "Auto · oyun gözlənilir",
                    "ZH" => "Auto · 等待游戏",
                    _ => "Auto · waiting for game"
                };
            else if (status.GameActive && !string.IsNullOrEmpty(status.DetectedGameExe))
                TxtOcLevel.Text = $"{status.ActiveProfileName} · {status.DetectedGameExe}.exe";
            else
                TxtOcLevel.Text = status.ActiveProfileName;
            TxtOcTemp.Text = core is float c ? $"{c:F0}°" : "—";
            TxtOcHotspot.Text = hotspot is float h ? $"{h:F0}°" : "—";

            var gpu = status.LastGpuTarget ?? OcProfileStore.SafeStock.ToTarget();
            TxtOcCore.Text = $"+{gpu.GpuCoreOffsetMhz}";
            TxtOcMem.Text = $"+{gpu.GpuMemoryOffsetMhz}";
            TxtOcPower.Text = gpu.GpuPowerLimitPercent is int pl ? $"{pl}%" : _s.OcPowerStock;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;
            if (e.ButtonState != MouseButtonState.Pressed)
                return;
            if (Mouse.LeftButton != MouseButtonState.Pressed)
                return;
            if (e.OriginalSource is DependencyObject src && IsTitleBarButton(src))
                return;

            e.Handled = true;
            BeginChromeMove();
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero || !Win32Api.StartNativeMove(hwnd))
                {
                    try { DragMove(); }
                    catch (InvalidOperationException) { }
                }
            }
            finally
            {
                EndChromeMove();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook(SingleInstanceActivateHook);
        }

        private IntPtr SingleInstanceActivateHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (SingleInstance.ActivateMessageId != 0 && msg == SingleInstance.ActivateMessageId)
            {
                handled = true;
                BringToFrontFromSecondInstance();
            }
            return IntPtr.Zero;
        }

        internal bool AllowClose { get; set; }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!AllowClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnClosing(e);
        }

        internal void BringToFrontFromSecondInstance()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(BringToFrontFromSecondInstance));
                return;
            }

            try
            {
                Show();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            bool wasTopmost = Topmost;
            Topmost = true;
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                    Win32Api.ForceForeground(hwnd);
                else
                    Activate();
            }
            finally
            {
                Topmost = wasTopmost;
            }
        }

        private void BeginChromeMove()
        {
            WindowMoveFreeze.Begin();
        }

        private void EndChromeMove()
        {
            WindowMoveFreeze.End();
        }

        private static bool IsTitleBarButton(DependencyObject source)
        {
            for (DependencyObject? d = source; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is System.Windows.Controls.Button)
                    return true;
            }
            return false;
        }

        private static bool ConfirmEnable(string message)
        {
            return System.Windows.MessageBox.Show(
                message,
                AppInfo.ProductName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        private void GitHub_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl(AppInfo.GitHubUserUrl);
        }

        private void Instagram_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl(AppInfo.InstagramUrl);
        }

        private void YouTube_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl(AppInfo.YouTubeUrl);
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdates.IsEnabled = false;
            TxtUpdateStatus.Text = _s.UpdateChecking;
            TxtUpdateStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x93, 0xA3));
            TxtUpdateStatus.Cursor = System.Windows.Input.Cursors.Arrow;
            TxtUpdateStatus.MouseLeftButtonDown -= UpdateStatus_Click;
            _updateReleaseUrl = null;

            try
            {
                // Hard cancel so proxy/DNS stalls can't leave the About panel stuck.
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
                var result = await UpdateChecker.CheckAsync(cts.Token).ConfigureAwait(true);

                if (!result.Success)
                {
                    OcDebugLog.Log(OcLogCategory.Update, $"update UI fail: {result.Message}");
                    TxtUpdateStatus.Text = _s.UpdateFailed;
                    TxtUpdateStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x80, 0x80));
                    return;
                }

                if (result.Message == "no_release")
                {
                    TxtUpdateStatus.Text = _s.UpdateNoRelease;
                    return;
                }

                if (result.UpdateAvailable)
                {
                    _updateReleaseUrl = result.ReleaseUrl ?? AppInfo.GitHubRepoUrl + "/releases";
                    string current = AppVersionComparer.Normalize(AppInfo.Version);
                    string latest = result.LatestVersion ?? "?";
                    TxtUpdateStatus.Text =
                        string.Format(_s.UpdateAvailable, $"{current} → {latest}") + "  ·  " + _s.UpdateOpenRelease;
                    TxtUpdateStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF2, 0x4C, 0x1D));
                    TxtUpdateStatus.Cursor = System.Windows.Input.Cursors.Hand;
                    TxtUpdateStatus.MouseLeftButtonDown -= UpdateStatus_Click;
                    TxtUpdateStatus.MouseLeftButtonDown += UpdateStatus_Click;
                    ShowUpdateBadge();
                }
                else
                {
                    TxtUpdateStatus.Text = _s.UpdateLatest + $" ({AppInfo.Version})";
                    TxtUpdateStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0xC0, 0x80));
                }
            }
            catch (OperationCanceledException)
            {
                OcDebugLog.Log(OcLogCategory.Update, "update UI timeout");
                TxtUpdateStatus.Text = _s.UpdateFailed;
                TxtUpdateStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x80, 0x80));
            }
            catch (Exception ex)
            {
                OcDebugLog.LogError(OcLogCategory.Update, "update UI error", ex);
                TxtUpdateStatus.Text = _s.UpdateFailed;
                TxtUpdateStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x80, 0x80));
            }
            finally
            {
                BtnCheckUpdates.IsEnabled = true;
            }
        }

        private void BtnOpenDebugLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = OcDebugLog.LogPath;
                if (!System.IO.File.Exists(path))
                    OcDebugLog.Log(OcLogCategory.General, "log opened (created empty)");
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                OcDebugLog.LogError(OcLogCategory.General, "open debug log failed", ex);
                System.Windows.MessageBox.Show(
                    "Could not open the debug log:\n" + OcDebugLog.LogPath,
                    AppInfo.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void BtnOpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = OcDebugLog.LogDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                OcDebugLog.LogError(OcLogCategory.General, "open log folder failed", ex);
            }
        }

        private void UpdateStatus_Click(object sender, MouseButtonEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_updateReleaseUrl))
                OpenUrl(_updateReleaseUrl);
        }

        // ─── Background auto-update checker ───────────────────────────────

        private void StartAutoUpdateChecker()
        {
            // First check after 60s, then every 30 minutes.
            _autoUpdateTimer = new System.Threading.Timer(
                _ => RunAutoUpdateCheck(),
                null,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromMinutes(30));
        }

        private async void RunAutoUpdateCheck()
        {
            try
            {
                // Only notify when no game is active (non-intrusive).
                bool gameActive = _ocManager?.Status.GameActive == true;
                if (gameActive) return;

                var result = await UpdateChecker.CheckAsync().ConfigureAwait(false);
                if (!result.Success || !result.UpdateAvailable) return;

                string releaseUrl = result.ReleaseUrl ?? AppInfo.GitHubRepoUrl;

                // Badge every time we confirm an update (idempotent); toast at most once per session.
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    _updateReleaseUrl = releaseUrl;
                    ShowUpdateBadge();
                }));

                if (System.Threading.Interlocked.Exchange(ref _updateToastShown, 1) != 0)
                    return;

                var s = UiStrings.For(_config.Language);
                new NotificationManager().ShowHighPriority(
                    string.IsNullOrWhiteSpace(s.ToastUpdateTitle) ? "Mars FPS Monitor" : s.ToastUpdateTitle,
                    string.IsNullOrWhiteSpace(s.ToastUpdateBody) ? "A new version update is available." : s.ToastUpdateBody,
                    tag: "mars-update");
            }
            catch (Exception ex)
            {
                OcDebugLog.Write($"auto-update check failed: {ex.Message}");
            }
        }

        private void ShowUpdateBadge()
        {
            if (_updateBadgeShown) return;
            _updateBadgeShown = true;

            if (NavAboutUpdateBadge != null)
                NavAboutUpdateBadge.Visibility = Visibility.Visible;
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
