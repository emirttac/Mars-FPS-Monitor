using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FPSOverlay
{
    public partial class ControlPanelWindow : Window
    {
        private OverlayConfig _config;
        private HardwareMonitorManager _hwManager;
        private OverclockManager? _ocManager;
        private Action _onConfigChanged;
        private Action<bool> _onOverlayToggle;
        private string _selectedColorHex;
        private List<AiOcRecommendation> _aiSuggestions = new();
        private bool _aiBusy;
        private bool _aiStatusFromSplash;
        private string? _updateReleaseUrl;
        private UiStrings _s = UiStrings.En();

        public ControlPanelWindow(OverlayConfig config, HardwareMonitorManager hwManager, Action onConfigChanged, Action<bool> onOverlayToggle, OverclockManager? ocManager = null)
        {
            InitializeComponent();
            _config = config;
            _hwManager = hwManager;
            _ocManager = ocManager;
            _onConfigChanged = onConfigChanged;
            _onOverlayToggle = onOverlayToggle;
            _selectedColorHex = _config.TextColorHex;

            try { this.Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/app.ico")); } catch { }

            Opacity = 0;
            Loaded += (_, __) => PlayWindowIntro();

            PopulateGpuSelector();
            LoadSettingsToUI();
            ApplyLanguage();
            ShowPanel("Overlay");
            RefreshOverclockStatusUi();

            if (_ocManager != null)
                _ocManager.StatusChanged += () => Dispatcher.BeginInvoke(new Action(RefreshOverclockStatusUi));
        }

        private void PlayWindowIntro()
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fade);
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton rb && rb.Tag != null)
                ShowPanel(rb.Tag.ToString() ?? "Overlay");
        }

        private void ShowPanel(string name)
        {
            if (PanelOverlay == null) return;

            ScrollViewer? target = name switch
            {
                "Sensors" => PanelSensors,
                "Display" => PanelDisplay,
                "Overclock" => PanelOverclock,
                "About" => PanelAbout,
                _ => PanelOverlay
            };

            PanelOverlay.Visibility = Visibility.Collapsed;
            PanelSensors.Visibility = Visibility.Collapsed;
            PanelDisplay.Visibility = Visibility.Collapsed;
            PanelOverclock.Visibility = Visibility.Collapsed;
            PanelAbout.Visibility = Visibility.Collapsed;

            if (target != null)
            {
                target.Visibility = Visibility.Visible;
                AnimatePanelIn(target);
            }

            SpinBrandLogoSoft();

            if (name == "Overclock")
                RefreshOverclockStatusUi();
        }

        private void SpinBrandLogoSoft()
        {
            if (ImgBrandLogo?.RenderTransform is not RotateTransform rot) return;
            double from = rot.Angle % 360;
            var spin = new DoubleAnimation(from, from + 360, TimeSpan.FromMilliseconds(900))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            rot.BeginAnimation(RotateTransform.AngleProperty, spin);
        }

        private static void AnimatePanelIn(UIElement panel)
        {
            panel.Opacity = 0;
            var transform = panel.RenderTransform as TranslateTransform;
            if (transform == null)
            {
                transform = new TranslateTransform();
                panel.RenderTransform = transform;
            }
            transform.Y = 14;

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var slide = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            panel.BeginAnimation(UIElement.OpacityProperty, fade);
            transform.BeginAnimation(TranslateTransform.YProperty, slide);
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
            NavOverlay.Content = s.NavOverlay;
            NavSensors.Content = s.NavSensors;
            NavDisplay.Content = s.NavDisplay;
            NavOverclock.Content = s.NavOverclock;
            NavAbout.Content = s.NavAbout;

            LblPageOverlay.Text = s.PageOverlay;
            LblPageOverlayDesc.Text = s.PageOverlayDesc;
            LblPageSensors.Text = s.PageSensors;
            LblPageSensorsDesc.Text = s.PageSensorsDesc;
            LblPageDisplay.Text = s.PageDisplay;
            LblPageDisplayDesc.Text = s.PageDisplayDesc;
            LblPageOverclock.Text = s.PageOverclock;
            LblPageOverclockDesc.Text = s.PageOverclockDesc;
            LblPageAbout.Text = s.PageAbout;
            LblPageAboutDesc.Text = s.PageAboutDesc;
            LblAboutBody.Text = s.AboutBody;
            if (LblAboutBrand != null) LblAboutBrand.Text = s.BrandName;
            LblAboutVersion.Text = s.AboutVersion;
            if (BtnCheckUpdates != null) BtnCheckUpdates.Content = s.CheckUpdates;
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

        private void SaveAndApply()
        {
            if (_config == null) return;

            if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag != null)
                _config.Language = item.Tag.ToString() ?? "EN";
            else
                _config.Language = "EN";

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

            _config.Save();
            ApplyLanguage();
            UpdateColorPreview();
            _onConfigChanged?.Invoke();
            _hwManager.TriggerUpdate();

            _onOverlayToggle?.Invoke(ChkOverlayActive.IsChecked == true);
        }

        private void InteractiveElement_Changed(object sender, RoutedEventArgs e)
        {
            if (this.IsLoaded) SaveAndApply();
        }

        private void InteractiveElement_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (this.IsLoaded) SaveAndApply();
        }

        private void SliderFontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!this.IsLoaded) return;
            if (TxtFontSizeVal != null) TxtFontSizeVal.Text = $"{(int)e.NewValue} px";
            // preview stays cute & fixed — real overlay does the scaling
            SaveAndApply();
        }

        private void SliderPadding_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!this.IsLoaded) return;
            if (TxtPaddingVal != null) TxtPaddingVal.Text = $"{(int)e.NewValue} px";
            SaveAndApply();
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

        private void OcMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded || _config == null) return;

            if (RadOcAuto.IsChecked == true)
                _config.OcControlMode = OcControlMode.AutoThermal;
            else if (RadOcManual.IsChecked == true)
                _config.OcControlMode = OcControlMode.ManualFixed;
            else
                _config.OcControlMode = OcControlMode.Off;

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
            try
            {
                if (store.GetById(profile.Id) == null)
                    store.Add(profile);
                else
                    store.Update(profile);
                ReloadOcProfileLists(profile.Id);
                _ocManager?.Refresh();
                TxtOcProfileMsg.Text = _s.MsgSaved;
                TxtOcProfileMsg.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0xC0, 0x80));
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
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
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
            _updateReleaseUrl = null;

            try
            {
                var result = await UpdateChecker.CheckAsync().ConfigureAwait(true);
                if (!result.Success)
                {
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
                    _updateReleaseUrl = result.ReleaseUrl ?? AppInfo.GitHubRepoUrl;
                    TxtUpdateStatus.Text = string.Format(_s.UpdateAvailable, result.LatestVersion) + "  ·  " + _s.UpdateOpenRelease;
                    TxtUpdateStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF2, 0x4C, 0x1D));
                    TxtUpdateStatus.Cursor = System.Windows.Input.Cursors.Hand;
                    TxtUpdateStatus.MouseLeftButtonDown -= UpdateStatus_Click;
                    TxtUpdateStatus.MouseLeftButtonDown += UpdateStatus_Click;
                }
                else
                {
                    TxtUpdateStatus.Text = _s.UpdateLatest;
                    TxtUpdateStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0xC0, 0x80));
                    TxtUpdateStatus.Cursor = System.Windows.Input.Cursors.Arrow;
                    TxtUpdateStatus.MouseLeftButtonDown -= UpdateStatus_Click;
                }
            }
            catch
            {
                TxtUpdateStatus.Text = _s.UpdateFailed;
                TxtUpdateStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x80, 0x80));
            }
            finally
            {
                BtnCheckUpdates.IsEnabled = true;
            }
        }

        private void UpdateStatus_Click(object sender, MouseButtonEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_updateReleaseUrl))
                OpenUrl(_updateReleaseUrl);
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
