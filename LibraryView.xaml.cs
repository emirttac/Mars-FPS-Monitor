using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;

namespace FPSOverlay
{
    public partial class LibraryView : UserControl
    {
        private readonly GameLibraryStore _store = new();
        private readonly GameLibraryScanner _scanner = new();
        private readonly ObservableCollection<GameItem> _visible = new();
        private List<GameItem> _all = new();
        private CancellationTokenSource? _scanCts;
        private CancellationTokenSource? _coverCts;
        private UiStrings _strings = UiStrings.En();
        private bool _isScanning;
        private bool _initialized;
        private string? _steamGridDbApiKey;
        private GameSessionTracker? _stats;

        public LibraryView()
        {
            InitializeComponent();
            GamesItems.ItemsSource = _visible;
            try
            {
                var cfg = OverlayConfig.Load();
                _steamGridDbApiKey = string.IsNullOrWhiteSpace(cfg.SteamGridDbApiKey) ? null : cfg.SteamGridDbApiKey.Trim();
                GameCoverLoader.SteamGridDbApiKey = _steamGridDbApiKey;
                _scanner.SteamGridDbApiKey = _steamGridDbApiKey;
            }
            catch { }

            Loaded += (_, __) =>
            {
                if (_initialized) return;
                _initialized = true;
                LoadCacheThenBackgroundScan();
            };
            Unloaded += (_, __) =>
            {
                _scanCts?.Cancel();
                _coverCts?.Cancel();
            };
        }

        public void ApplyStrings(UiStrings s)
        {
            _strings = s;
            if (LblLibraryTitle != null) LblLibraryTitle.Text = s.LibraryTitle;
            if (LblLibraryDesc != null) LblLibraryDesc.Text = s.LibraryDesc;
            if (LblSearchPlaceholder != null) LblSearchPlaceholder.Text = s.LibrarySearchPlaceholder;
            if (BtnRefresh != null) BtnRefresh.Content = s.LibraryRefresh;
            if (BtnAddGame != null) BtnAddGame.Content = s.LibraryAddGame;
            if (LblScanning != null) LblScanning.Text = s.LibraryScanning;
            if (LblEmpty != null) LblEmpty.Text = s.LibraryEmpty;
            UpdateCountLabel();
            UpdatePlayButtonLabels();
            ApplyStatsToItems();
        }

        public void AttachSessionTracker(GameSessionTracker tracker)
        {
            if (ReferenceEquals(_stats, tracker))
                return;
            if (_stats != null)
                _stats.SessionRecorded -= OnSessionRecorded;
            _stats = tracker;
            tracker.SessionRecorded += OnSessionRecorded;
            if (_initialized)
                PublishLibrary();
        }

        private void LoadCacheThenBackgroundScan()
        {
            _all = _store.GetAll().ToList();
            ApplyFilter();
            PublishLibrary();
            _ = RefreshCoversAsync(_all);
            _ = ScanSilentAsync();
        }

        private async Task ScanSilentAsync()
        {
            await ScanInternalAsync(showBusy: _all.Count == 0).ConfigureAwait(true);
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await ScanInternalAsync(showBusy: true).ConfigureAwait(true);
        }

        private async Task ScanInternalAsync(bool showBusy)
        {
            if (_isScanning) return;
            _isScanning = true;
            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();
            var ct = _scanCts.Token;

            if (showBusy && BusyOverlay != null)
                BusyOverlay.Visibility = Visibility.Visible;
            if (BtnRefresh != null) BtnRefresh.IsEnabled = false;

            try
            {
                var scanned = await _scanner.ScanAsync(enrichCovers: true, ct).ConfigureAwait(true);
                if (ct.IsCancellationRequested) return;

                _store.ReplaceScanned(scanned, DateTime.UtcNow);
                // Persist any covers resolved during scan enrichment
                foreach (var g in scanned.Where(x => !string.IsNullOrWhiteSpace(x.CoverUrl) || x.CoverLookupDone))
                    _store.UpdateCover(g.Id, g.CoverUrl, g.CoverLookupDone);

                _all = _store.GetAll().ToList();
                ApplyFilter();
                PublishLibrary();
                _ = RefreshCoversAsync(_all);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                OcDebugLog.Write("library scan: " + ex.Message);
            }
            finally
            {
                _isScanning = false;
                if (BusyOverlay != null) BusyOverlay.Visibility = Visibility.Collapsed;
                if (BtnRefresh != null) BtnRefresh.IsEnabled = true;
            }
        }

        private void BtnAddGame_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
                Title = _strings.LibraryAddGame,
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var item = GameLibraryScanner.CreateCustomFromExe(dlg.FileName);
                _store.AddCustom(item);
                _all = _store.GetAll().ToList();
                ApplyFilter();
                PublishLibrary();
                _ = GameCoverLoader.LoadIntoAsync(item, Dispatcher, store: _store);
            }
            catch (Exception ex)
            {
                OcDebugLog.Write("library add custom: " + ex.Message);
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (LblSearchPlaceholder != null)
                LblSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearch.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string q = (TxtSearch?.Text ?? "").Trim();
            IEnumerable<GameItem> src = _all;
            if (!string.IsNullOrEmpty(q))
                src = src.Where(g => g.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || g.PlatformBadge.Contains(q, StringComparison.OrdinalIgnoreCase));

            var list = src.ToList();
            _visible.Clear();
            foreach (var g in list)
                _visible.Add(g);

            UpdateCountLabel();
            if (LblEmpty != null)
                LblEmpty.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnSessionRecorded()
        {
            try
            {
                if (Dispatcher.HasShutdownStarted)
                    return;
                if (Dispatcher.CheckAccess())
                    ApplyStatsToItems();
                else
                    Dispatcher.BeginInvoke(new Action(ApplyStatsToItems));
            }
            catch { }
        }

        private void PublishLibrary()
        {
            _stats?.SetLibrary(_all);
            ApplyStatsToItems();
        }

        private void ApplyStatsToItems()
        {
            if (_stats == null)
                return;
            foreach (var game in _all)
                GameStatsFormatter.Apply(game, _stats.TryGet(GameLibraryStore.DedupKey(game)), _strings);
        }

        private void UpdateCountLabel()
        {
            if (LblGamesFound == null) return;
            LblGamesFound.Text = string.Format(_strings.LibraryGamesFound, _visible.Count);
        }

        private void UpdatePlayButtonLabels()
        {
            // DataTemplate buttons pick Content at create time; re-apply via iterating containers is heavy.
            // Hover button text is set in template via code when cards are clicked / launched — also update resource-less.
            // Simpler: store play label and set on click path; for visual, recreate items.
            var snapshot = _visible.ToList();
            _visible.Clear();
            foreach (var g in snapshot)
                _visible.Add(g);
        }

        private async Task RefreshCoversAsync(IReadOnlyList<GameItem> games)
        {
            _coverCts?.Cancel();
            _coverCts = new CancellationTokenSource();
            var ct = _coverCts.Token;

            // Prefer Steam covers first (network), then icons.
            var ordered = games
                .OrderByDescending(g => g.PlatformSource == GamePlatform.Steam)
                .ToList();

            var tasks = new List<Task>();
            const int maxParallel = 4;
            using var gate = new SemaphoreSlim(maxParallel);
            foreach (var game in ordered)
            {
                if (ct.IsCancellationRequested) break;
                await gate.WaitAsync(ct).ConfigureAwait(false);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await GameCoverLoader.LoadIntoAsync(game, Dispatcher, ct, _store).ConfigureAwait(false);
                    }
                    catch { }
                    finally
                    {
                        gate.Release();
                    }
                }, ct));
            }

            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch { }
        }

        private void BtnPlay_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
                btn.Content = _strings.LibraryLaunch;
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.Content = _strings.LibraryLaunch;
                if (btn.Tag is GameItem game)
                    LaunchGame(game);
                e.Handled = true;
            }
        }

        private void GameCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Double-purpose: ignore if play button handled it.
            if (e.Handled) return;
            if (sender is FrameworkElement fe && fe.DataContext is GameItem game)
                LaunchGame(game);
        }

        public static void LaunchGame(GameItem game)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(game.LaunchUri))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = game.LaunchUri,
                        UseShellExecute = true
                    });
                    return;
                }

                if (!string.IsNullOrWhiteSpace(game.ExePath) && System.IO.File.Exists(game.ExePath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = game.ExePath,
                        WorkingDirectory = System.IO.Path.GetDirectoryName(game.ExePath) ?? "",
                        UseShellExecute = true
                    };
                    if (!string.IsNullOrWhiteSpace(game.LaunchArgs))
                        psi.Arguments = game.LaunchArgs;
                    Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                OcDebugLog.Write($"library launch failed ({game.Title}): {ex.Message}");
            }
        }
    }
}
