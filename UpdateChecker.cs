using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FPSOverlay
{
    public sealed class UpdateCheckResult
    {
        public bool Success { get; init; }
        public bool UpdateAvailable { get; init; }
        public string CurrentVersion { get; init; } = AppInfo.Version;
        public string? LatestVersion { get; init; }
        public string? ReleaseUrl { get; init; }
        public string Message { get; init; } = "";
    }

    /// <summary>Checks GitHub Releases for a newer Mars FPS Monitor version.</summary>
    public static class UpdateChecker
    {
        private static readonly object Gate = new();
        private static HttpClient? _http;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

        private static HttpClient Http
        {
            get
            {
                lock (Gate)
                {
                    return _http ??= CreateClient();
                }
            }
        }

        private static HttpClient CreateClient()
        {
            // Prefer SocketsHttpHandler with connect timeout; fall back if unavailable.
            HttpMessageHandler handler;
            try
            {
                handler = new SocketsHttpHandler
                {
                    ConnectTimeout = TimeSpan.FromSeconds(8),
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip
                                           | System.Net.DecompressionMethods.Deflate
                };
            }
            catch
            {
                handler = new HttpClientHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip
                                           | System.Net.DecompressionMethods.Deflate
                };
            }

            var c = new HttpClient(handler) { Timeout = RequestTimeout };
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mars-FPS-Monitor/" + AppInfo.Version);
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            return c;
        }

        public static Task<UpdateCheckResult> CheckAsync()
            => CheckAsync(CancellationToken.None);

        public static async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                if (!ct.CanBeCanceled)
                    linked.CancelAfter(RequestTimeout);

                using var resp = await Http.GetAsync(AppInfo.GitHubReleasesApi, linked.Token)
                    .ConfigureAwait(false);

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new UpdateCheckResult
                    {
                        Success = true,
                        UpdateAvailable = false,
                        Message = "no_release"
                    };
                }

                if (!resp.IsSuccessStatusCode)
                {
                    string fail = $"http_{(int)resp.StatusCode}";
                    OcDebugLog.Write($"update check failed: {fail}");
                    return new UpdateCheckResult
                    {
                        Success = false,
                        Message = fail
                    };
                }

                string json = await resp.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                string htmlUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? AppInfo.GitHubRepoUrl : AppInfo.GitHubRepoUrl;
                string latest = NormalizeVersion(tag);
                string current = NormalizeVersion(AppInfo.Version);

                if (string.IsNullOrWhiteSpace(latest))
                {
                    return new UpdateCheckResult
                    {
                        Success = true,
                        UpdateAvailable = false,
                        LatestVersion = tag,
                        ReleaseUrl = htmlUrl,
                        Message = "no_release"
                    };
                }

                bool newer = CompareVersions(latest, current) > 0;
                OcDebugLog.Write($"update check ok · current={current} latest={latest} newer={newer}");
                return new UpdateCheckResult
                {
                    Success = true,
                    UpdateAvailable = newer,
                    LatestVersion = latest,
                    ReleaseUrl = htmlUrl,
                    Message = newer ? "update" : "latest"
                };
            }
            catch (OperationCanceledException)
            {
                OcDebugLog.Write("update check timed out");
                return new UpdateCheckResult
                {
                    Success = false,
                    Message = "timeout"
                };
            }
            catch (Exception ex)
            {
                OcDebugLog.Write($"update check error: {ex.GetType().Name}: {ex.Message}");
                return new UpdateCheckResult
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        private static string NormalizeVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var m = Regex.Match(raw.Trim(), @"\d+(?:\.\d+)*");
            return m.Success ? m.Value : "";
        }

        /// <summary>Returns &gt;0 if a is newer than b.</summary>
        private static int CompareVersions(string a, string b)
        {
            var pa = a.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var pb = b.Split('.', StringSplitOptions.RemoveEmptyEntries);
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int va = i < pa.Length && int.TryParse(pa[i], out var x) ? x : 0;
                int vb = i < pb.Length && int.TryParse(pb[i], out var y) ? y : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return 0;
        }
    }
}
