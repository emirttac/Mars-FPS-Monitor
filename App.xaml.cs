using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace FPSOverlay
{
    public partial class App : System.Windows.Application
    {
        private Forms.NotifyIcon _notifyIcon = null!;
        private OverlayConfig _config = null!;
        private HardwareMonitorManager _hardwareManager = null!;
        private OverclockManager _overclockManager = null!;
        private FanControlManager _fanControlManager = null!;
        private GameSessionTracker? _sessionTracker;
        private string _boundGpu = "";

        private OverlayWindow _overlayWindow = null!;
        private ControlPanelWindow _controlPanelWindow = null!;
        private Forms.ToolStripItem _menuItemSettings = null!;
        private Forms.ToolStripItem _menuItemExit = null!;

        public App()
        {
            CrashReporter.Register(this);
            SessionEnding += (_, _) => HardwareRelease.ReleaseOnce();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            if (!SingleInstance.TryBecomePrimary(() =>
                {
                    try { Dispatcher.BeginInvoke(new Action(BringUiToFront)); }
                    catch { }
                }))
            {
                try { SingleInstance.ActivateExisting(); }
                catch { }
                Environment.Exit(0);
                return;
            }

            base.OnStartup(e);

            AppPaths.MigrateLegacyFiles();
            _config = OverlayConfig.Load();

            // Once per version, and once after Setup drops the pending-notes flag.
            // Later launches skip this and open the app directly.
            bool showWhatsNew =
                AppPaths.HasPendingReleaseNotes() ||
                !string.Equals(_config.ReleaseNotesSeenVersion, AppInfo.Version, StringComparison.OrdinalIgnoreCase);

            if (showWhatsNew)
            {
                // Closing this dialog must not end the process; the main window opens after it.
                ShutdownMode previousMode = ShutdownMode;
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                bool? accepted;
                try
                {
                    var notes = new ReleaseNotesWindow(_config);
                    accepted = notes.ShowDialog();
                }
                finally
                {
                    ShutdownMode = previousMode;
                }

                if (accepted != true)
                {
                    Shutdown();
                    return;
                }
            }

            var strings = UiStrings.For(_config.Language);

            var splash = new SplashWindow();
            splash.ApplyStrings(strings);
            splash.Show();

            var sw = Stopwatch.StartNew();

            try
            {
                splash.SetStatus(strings.SplashBoot);
                await Task.Delay(200);

                splash.SetStatus(strings.SplashSensors);

                #region Composition root — manager wiring order
                // OverlayConfig → HardwareMonitor → Overclock → Fans → Overlay/ControlPanel UI
                _hardwareManager = new HardwareMonitorManager();
                if (_hardwareManager.EnsureSelectedGpu(_config))
                    _config.Save();

                // First LHM Open() often yields empty CPU temps until a few Update() passes.
                splash.SetStatus(strings.SplashSensors);
                await _hardwareManager.WarmUpSensorsAsync(_config.SelectedGpuName).ConfigureAwait(true);

                // If CPU is still empty (ACPI lag / PawnIO), keep priming in the background
                // while the rest of startup continues — Home gauges pick it up immediately.
                if (_hardwareManager.GetCpuTemperature() <= 0)
                {
                    _ = Task.Run(async () =>
                    {
                        for (int i = 0; i < 12; i++)
                        {
                            await Task.Delay(200).ConfigureAwait(false);
                            _hardwareManager.WarmUpSensors(_config.SelectedGpuName, maxPasses: 2);
                            if (_hardwareManager.GetCpuTemperature() > 0)
                            {
                                OcDebugLog.Log(OcLogCategory.Sensor, $"background CPU prime succeeded on retry {i + 1}");
                                break;
                            }
                        }
                    });
                }

                string gpuAtCreate = _config.SelectedGpuName ?? "";
                _overclockManager = new OverclockManager(_config, _hardwareManager, _hardwareManager.Computer);
                _hardwareManager.OverclockStatusProvider = () => _overclockManager.GetOverlaySummary(_config.Language);

                _fanControlManager = new FanControlManager(_config, _hardwareManager);
                _hardwareManager.FanStatusProvider = () => _fanControlManager.GetOverlaySummary();

                InitializeNotifyIcon();

                _overlayWindow = new OverlayWindow(_config, _hardwareManager, _overclockManager);
                _overlayWindow.Show();

                _controlPanelWindow = new ControlPanelWindow(
                    _config,
                    _hardwareManager,
                    OnConfigChanged,
                    ToggleOverlay,
                    _overclockManager,
                    _fanControlManager);
                _sessionTracker = new GameSessionTracker(_hardwareManager, () => _config.SelectedGpuName);
                _controlPanelWindow.AttachLibraryStats(_sessionTracker);
                #endregion

                _overlayWindow.OnPositionChanged += (x, y) =>
                {
                    _controlPanelWindow.NotifyCustomDrag();
                };

                // always Refresh — ACTIVE NOW temps even if OC is Off
                _overclockManager.Refresh();

                // LHM sometimes enumerates late — one more GPU pass before UI settles
                await Task.Delay(350);
                if (_hardwareManager.EnsureSelectedGpu(_config))
                {
                    _config.Save();
                    _controlPanelWindow.RefreshGpuSelector();
                }
                if (!string.Equals(gpuAtCreate, _config.SelectedGpuName ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    _overclockManager.RebindSelectedGpu();
                    _fanControlManager.RebindSelectedGpu();
                }
                _boundGpu = _config.SelectedGpuName ?? "";

                splash.SetStatus(strings.SplashReady);

                // hold splash a sec so it feels premium not crashy
                int remaining = 2800 - (int)sw.ElapsedMilliseconds;
                if (remaining > 0)
                    await Task.Delay(remaining);
            }
            catch (Exception ex)
            {
                try { splash.Close(); } catch { }
                try { _sessionTracker?.Dispose(); } catch { }
                CrashReporter.OfferReportAndExit(ex, "Startup");
                return;
            }

            _controlPanelWindow.Show();
            _controlPanelWindow.Activate();

            try
            {
                splash.Close();
            }
            catch { }
        }

        private void BringUiToFront()
        {
            try
            {
                if (_controlPanelWindow != null)
                {
                    _controlPanelWindow.BringToFrontFromSecondInstance();
                    return;
                }

                foreach (Window w in Windows)
                {
                    if (w is OverlayWindow)
                        continue;
                    try
                    {
                        w.Show();
                        w.Activate();
                        var hwnd = new WindowInteropHelper(w).Handle;
                        if (hwnd != IntPtr.Zero)
                            Win32Api.ForceForeground(hwnd);
                    }
                    catch { }
                    break;
                }
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            HardwareRelease.ReleaseOnce();
            SingleInstance.Release();
            try { _sessionTracker?.Dispose(); } catch { }
            base.OnExit(e);
        }

        private void InitializeNotifyIcon()
        {
            _notifyIcon = new Forms.NotifyIcon();

            var streamInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
            if (streamInfo != null)
                _notifyIcon.Icon = new Icon(streamInfo.Stream);
            else
                _notifyIcon.Icon = SystemIcons.Application;

            _notifyIcon.Visible = true;
            _notifyIcon.Text = AppInfo.ProductName;

            _notifyIcon.DoubleClick += (_, __) => ShowControlPanel();

            var contextMenu = new Forms.ContextMenuStrip();
            _menuItemSettings = contextMenu.Items.Add("Ayarlar", null, (_, __) => ShowControlPanel());
            _menuItemExit = contextMenu.Items.Add("Çıkış", null, (s, args) => ExitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;

            UpdateTrayLanguage();
        }

        private void ShowControlPanel()
        {
            var window = _controlPanelWindow;
            if (window == null) return;

            try
            {
                window.BringToFrontFromSecondInstance();
            }
            catch (InvalidOperationException)
            {
                // Window was already closed. Keep the tray icon alive without a crash dialog.
            }
        }

        private void UpdateTrayLanguage()
        {
            if (_menuItemSettings == null || _menuItemExit == null || _config == null) return;
            string lang = _config.Language ?? "EN";
            var strings = UiStrings.For(lang);
            _menuItemSettings.Text = strings.TraySettings;
            _menuItemExit.Text = strings.TrayExit;
        }

        private void OnConfigChanged()
        {
            string gpu = _config.SelectedGpuName ?? "";
            if (!string.Equals(_boundGpu, gpu, StringComparison.OrdinalIgnoreCase))
            {
                _boundGpu = gpu;
                try { _overclockManager?.RebindSelectedGpu(); }
                catch (Exception ex) { OcDebugLog.LogError(OcLogCategory.Oc, "GPU rebind failed", ex); }
                try { _fanControlManager?.RebindSelectedGpu(); }
                catch (Exception ex) { OcDebugLog.LogError(OcLogCategory.Fan, "GPU fan rebind failed", ex); }
            }
            _overlayWindow.ApplyConfig();
            UpdateTrayLanguage();
        }

        private void ToggleOverlay(bool isActive)
        {
            if (isActive)
                _overlayWindow.SetOverlayEnabled(true);
            else
                _overlayWindow.SetOverlayEnabled(false);
        }

        private void ExitApplication()
        {
            SingleInstance.Release();
            try { _sessionTracker?.Dispose(); } catch { }

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();

            HardwareRelease.ReleaseOnce();
            _overclockManager?.Dispose();
            _fanControlManager?.Dispose();

            _overlayWindow?.Close();
            if (_controlPanelWindow != null)
            {
                _controlPanelWindow.AllowClose = true;
                _controlPanelWindow.Close();
            }
            _hardwareManager?.Dispose();

            Current.Shutdown();
        }
    }
}
