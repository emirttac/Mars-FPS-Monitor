using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;

namespace FPSOverlay
{
    public partial class App : System.Windows.Application
    {
        private Forms.NotifyIcon _notifyIcon = null!;
        private OverlayConfig _config = null!;
        private HardwareMonitorManager _hardwareManager = null!;
        private OverclockManager _overclockManager = null!;

        private OverlayWindow _overlayWindow = null!;
        private ControlPanelWindow _controlPanelWindow = null!;
        private Forms.ToolStripItem _menuItemSettings = null!;
        private Forms.ToolStripItem _menuItemExit = null!;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _config = OverlayConfig.Load();
            var strings = UiStrings.For(_config.Language);

            var splash = new SplashWindow();
            splash.ApplyStrings(strings);
            splash.Show();

            var sw = Stopwatch.StartNew();
            AiOcAssistResult? aiPrefetch = null;

            try
            {
                splash.SetStatus(strings.SplashBoot);
                await Task.Delay(200);

                splash.SetStatus(strings.SplashSensors);
                _hardwareManager = new HardwareMonitorManager();
                if (_hardwareManager.EnsureSelectedGpu(_config))
                    _config.Save();

                _overclockManager = new OverclockManager(_config, _hardwareManager, _hardwareManager.Computer);
                _hardwareManager.OverclockStatusProvider = () => _overclockManager.GetOverlaySummary(_config.Language);

                InitializeNotifyIcon();

                _overlayWindow = new OverlayWindow(_config, _hardwareManager, _overclockManager);
                _overlayWindow.Show();

                _controlPanelWindow = new ControlPanelWindow(
                    _config,
                    _hardwareManager,
                    OnConfigChanged,
                    ToggleOverlay,
                    _overclockManager);

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

                // sneak AI prefetch while splash is still flexing
                splash.SetStatus(strings.SplashAi);
                try
                {
                    using var client = new AiOcAssistantClient(_config);
                    aiPrefetch = await client.RequestSuggestionsAsync(
                        _hardwareManager,
                        _overclockManager.Status.GpuVendor).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    OcDebugLog.Write("Splash AI prefetch failed: " + ex.Message);
                }

                splash.SetStatus(strings.SplashReady);

                // hold splash a sec so it feels premium not crashy
                int remaining = 2800 - (int)sw.ElapsedMilliseconds;
                if (remaining > 0)
                    await Task.Delay(remaining);
            }
            catch (Exception ex)
            {
                OcDebugLog.Write("Startup failed: " + ex);
                System.Windows.MessageBox.Show(ex.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            }

            if (aiPrefetch != null)
                _controlPanelWindow.ApplyAiAssistResult(aiPrefetch, fromSplash: true);

            _controlPanelWindow.Show();
            _controlPanelWindow.Activate();

            try
            {
                splash.Close();
            }
            catch { }
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

            _notifyIcon.DoubleClick += (s, args) =>
            {
                _controlPanelWindow.Show();
                _controlPanelWindow.WindowState = WindowState.Normal;
                _controlPanelWindow.Activate();
            };

            var contextMenu = new Forms.ContextMenuStrip();
            _menuItemSettings = contextMenu.Items.Add("Ayarlar", null, (s, args) => _controlPanelWindow.Show());
            _menuItemExit = contextMenu.Items.Add("Çıkış", null, (s, args) => ExitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;

            UpdateTrayLanguage();
        }

        private void UpdateTrayLanguage()
        {
            if (_menuItemSettings == null || _menuItemExit == null || _config == null) return;
            string lang = _config.Language ?? "EN";
            switch (lang)
            {
                case "TR": _menuItemSettings.Text = "Ayarlar"; _menuItemExit.Text = "Çıkış"; break;
                case "DE": _menuItemSettings.Text = "Einstellungen"; _menuItemExit.Text = "Beenden"; break;
                case "ES": _menuItemSettings.Text = "Ajustes"; _menuItemExit.Text = "Salir"; break;
                case "FR": _menuItemSettings.Text = "Paramètres"; _menuItemExit.Text = "Quitter"; break;
                case "PT": _menuItemSettings.Text = "Definições"; _menuItemExit.Text = "Sair"; break;
                case "BR": _menuItemSettings.Text = "Configurações"; _menuItemExit.Text = "Sair"; break;
                case "RU": _menuItemSettings.Text = "Настройки"; _menuItemExit.Text = "Выход"; break;
                default: _menuItemSettings.Text = "Settings"; _menuItemExit.Text = "Exit"; break;
            }
        }

        private void OnConfigChanged()
        {
            _overlayWindow.ApplyConfig();
            UpdateTrayLanguage();
        }

        private void ToggleOverlay(bool isActive)
        {
            if (isActive)
                _overlayWindow.Show();
            else
                _overlayWindow.Hide();
        }

        private void ExitApplication()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();

            try { _overclockManager?.RestoreAll(); } catch { }
            _overclockManager?.Dispose();

            _overlayWindow?.Close();
            _controlPanelWindow?.Close();
            _hardwareManager?.Dispose();

            Current.Shutdown();
        }
    }
}
