using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Application = System.Windows.Application;

namespace FPSOverlay
{
    /// <summary>
    /// Loads covers: Steam CDN / scraped URL first, then centered EXE icon fallback.
    /// </summary>
    public static class GameCoverLoader
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim HttpGate = new(4);

        /// <summary>Optional SteamGridDB bearer token (empty = skip stage B).</summary>
        public static string? SteamGridDbApiKey { get; set; }

        static GameCoverLoader()
        {
            try { Http.DefaultRequestHeaders.UserAgent.ParseAdd("MarsFPSMonitor/1.0"); } catch { }
        }

        public static async Task LoadIntoAsync(
            GameItem game,
            Dispatcher dispatcher,
            CancellationToken ct = default,
            GameLibraryStore? store = null)
        {
            if (game.CoverImage != null) return;

            // Resolve missing CoverUrl via Steam Store / SteamGridDB (once).
            if (GameCoverScraper.NeedsWebCoverLookup(game))
            {
                try
                {
                    var result = await GameCoverScraper.FindCoverAsync(
                        game.Title, SteamGridDbApiKey, ct).ConfigureAwait(false);

                    await dispatcher.InvokeAsync(() =>
                    {
                        game.CoverLookupDone = true;
                        if (result.Success)
                        {
                            game.CoverUrl = result.CoverUrl;
                            OcDebugLog.Write($"cover hit [{result.Source}] {game.Title} ({result.Similarity:P0})");
                        }
                    });

                    store?.UpdateCover(game.Id, result.CoverUrl, lookupDone: true);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    await dispatcher.InvokeAsync(() => game.CoverLookupDone = true);
                    store?.UpdateCover(game.Id, null, lookupDone: true);
                    OcDebugLog.Write($"cover lookup ({game.Title}): {ex.Message}");
                }
            }

            string cacheKey = !string.IsNullOrWhiteSpace(game.CoverUrl)
                ? "url:" + game.CoverUrl
                : "exe64:" + (game.ExePath ?? game.Id);

            if (Cache.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                bool icon = cacheKey.StartsWith("exe64:", StringComparison.Ordinal);
                await dispatcher.InvokeAsync(() =>
                {
                    game.IsIconFallback = icon;
                    game.CoverImage = cached;
                    game.CoverLoadFailed = false;
                });
                return;
            }

            ImageSource? image = null;
            bool asIcon = false;

            if (!string.IsNullOrWhiteSpace(game.CoverUrl))
            {
                image = await DownloadCoverAsync(game.CoverUrl!, ct).ConfigureAwait(false);
                // Dead Steam CDN link (404) — try scrape once if not already done for Steam-only urls
                if (image == null && game.PlatformSource != GamePlatform.Steam && !game.CoverLookupDone)
                {
                    // already handled above; fall through to icon
                }
            }

            if (image == null && !string.IsNullOrWhiteSpace(game.ExePath) && File.Exists(game.ExePath))
            {
                image = ExtractExeIcon(game.ExePath!, 64);
                asIcon = image != null;
            }

            if (image != null)
            {
                Cache[cacheKey] = image;
                await dispatcher.InvokeAsync(() =>
                {
                    game.IsIconFallback = asIcon;
                    game.CoverImage = image;
                    game.CoverLoadFailed = false;
                });
            }
            else
            {
                await dispatcher.InvokeAsync(() =>
                {
                    game.IsIconFallback = false;
                    game.CoverLoadFailed = true;
                });
            }
        }

        private static async Task<ImageSource?> DownloadCoverAsync(string url, CancellationToken ct)
        {
            await HttpGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                byte[] bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (bytes.Length < 64) return null;

                return await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var bmp = new BitmapImage();
                    using var ms = new MemoryStream(bytes);
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.DecodePixelWidth = 340;
                    bmp.EndInit();
                    bmp.Freeze();
                    return (ImageSource)bmp;
                });
            }
            catch
            {
                return null;
            }
            finally
            {
                HttpGate.Release();
            }
        }

        public static ImageSource? ExtractExeIcon(string exePath, int size = 64)
        {
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon == null) return null;

                ImageSource src = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(size, size));
                src.Freeze();
                return src;
            }
            catch
            {
                return null;
            }
        }
    }
}
