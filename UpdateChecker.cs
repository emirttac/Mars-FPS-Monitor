using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    /// <summary>Checks GitHub Releases for a newer Mars FPS Monitor version. update hunt!</summary>
    public static class UpdateChecker
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mars-FPS-Monitor/" + AppInfo.Version);
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        public static async Task<UpdateCheckResult> CheckAsync()
        {
            try
            {
                using var resp = await Http.GetAsync(AppInfo.GitHubReleasesApi).ConfigureAwait(false);
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
                    return new UpdateCheckResult
                    {
                        Success = false,
                        Message = $"http_{(int)resp.StatusCode}"
                    };
                }

                await using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
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
                return new UpdateCheckResult
                {
                    Success = true,
                    UpdateAvailable = newer,
                    LatestVersion = latest,
                    ReleaseUrl = htmlUrl,
                    Message = newer ? "update" : "latest"
                };
            }
            catch (Exception ex)
            {
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

        /// <summary>Returns &gt;0 if a is newer than b. semver duel.</summary>
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
