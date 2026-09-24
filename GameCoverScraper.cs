using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FPSOverlay
{
    public sealed class GameCoverLookupResult
    {
        public string? CoverUrl { get; init; }
        public double Similarity { get; init; }
        public string Source { get; init; } = "";
        public bool Success => !string.IsNullOrWhiteSpace(CoverUrl);
    }

    /// <summary>
    /// Finds vertical (600×900) covers for non-Steam titles via Steam Store search
    /// and optional SteamGridDB. Never invents a wrong cover below 75% name similarity.
    /// </summary>
    public static class GameCoverScraper
    {
        public const double MinSimilarity = 0.75;

        private static readonly HttpClient Http = CreateClient();
        private static readonly SemaphoreSlim Gate = new(3);

        private static readonly Regex Parens = new(@"\([^)]*\)|\[[^\]]*\]", RegexOptions.Compiled);
        private static readonly Regex VersionToken = new(@"\bv\d+(\.\d+)*\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BuildToken = new(@"\bBuild\s*\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JunkTokens = new(
            @"\b(x64|x86|GOTY|Repack|Setup|Edition|GOG|DirectX|DX\d+|Win\d+|Windows|Update|Hotfix|DLC|Demo|Beta|Alpha|Trial|Launcher|Client)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MultiSpace = new(@"\s{2,}", RegexOptions.Compiled);
        private static readonly Regex NonWord = new(@"[^\w\s\-:']+", RegexOptions.Compiled);

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            try { c.DefaultRequestHeaders.UserAgent.ParseAdd("MarsFPSMonitor/1.0"); } catch { }
            return c;
        }

        /// <summary>Strip version / architecture / edition noise before web search.</summary>
        public static string SanitizeTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";

            string t = title.Trim();
            t = Parens.Replace(t, " ");
            t = VersionToken.Replace(t, " ");
            t = BuildToken.Replace(t, " ");
            t = JunkTokens.Replace(t, " ");
            t = NonWord.Replace(t, " ");
            t = MultiSpace.Replace(t, " ").Trim();
            // Drop trailing edition leftovers like " - "
            t = t.Trim(' ', '-', ':', '_');
            return t;
        }

        public static bool NeedsWebCoverLookup(GameItem game)
        {
            if (game.PlatformSource == GamePlatform.Steam && !string.IsNullOrWhiteSpace(game.CoverUrl))
                return false;
            if (!string.IsNullOrWhiteSpace(game.CoverUrl))
                return false;
            if (game.CoverLookupDone)
                return false;
            return game.PlatformSource is GamePlatform.Epic or GamePlatform.Custom
                or GamePlatform.Gog or GamePlatform.Ea or GamePlatform.Ubisoft;
        }

        public static Task<GameCoverLookupResult> FindCoverAsync(
            string title,
            string? steamGridDbApiKey = null,
            CancellationToken ct = default)
            => Task.Run(() => FindCoverCoreAsync(title, steamGridDbApiKey, ct), ct);

        private static async Task<GameCoverLookupResult> FindCoverCoreAsync(
            string title,
            string? steamGridDbApiKey,
            CancellationToken ct)
        {
            string clean = SanitizeTitle(title);
            if (string.IsNullOrWhiteSpace(clean))
                return new GameCoverLookupResult { Similarity = 0 };

            // Stage A — Steam Store Search (no API key)
            var steam = await TrySteamStoreSearchAsync(clean, ct).ConfigureAwait(false);
            if (steam.Success)
                return steam;

            // Stage B — SteamGridDB (optional key)
            if (!string.IsNullOrWhiteSpace(steamGridDbApiKey))
            {
                var sgdb = await TrySteamGridDbAsync(clean, steamGridDbApiKey!, ct).ConfigureAwait(false);
                if (sgdb.Success)
                    return sgdb;
            }

            // Stage C — caller falls back to EXE icon
            return new GameCoverLookupResult { Similarity = steam.Similarity, Source = "fallback" };
        }

        /// <summary>Fill CoverUrl for games that still need a web lookup. Mutates list in place.</summary>
        public static async Task EnrichMissingCoversAsync(
            IList<GameItem> games,
            string? steamGridDbApiKey = null,
            CancellationToken ct = default,
            Action<GameItem>? onResolved = null)
        {
            var targets = games.Where(NeedsWebCoverLookup).ToList();
            if (targets.Count == 0) return;

            var tasks = targets.Select(async g =>
            {
                try
                {
                    var result = await FindCoverAsync(g.Title, steamGridDbApiKey, ct).ConfigureAwait(false);
                    g.CoverLookupDone = true;
                    if (result.Success)
                    {
                        g.CoverUrl = result.CoverUrl;
                        onResolved?.Invoke(g);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    g.CoverLookupDone = true;
                    OcDebugLog.Write($"cover scrape ({g.Title}): {ex.Message}");
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static async Task<GameCoverLookupResult> TrySteamStoreSearchAsync(string cleanTitle, CancellationToken ct)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string url =
                    "https://store.steampowered.com/api/storesearch/?term="
                    + Uri.EscapeDataString(cleanTitle)
                    + "&l=turkish&cc=TR";

                using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return new GameCoverLookupResult { Similarity = 0, Source = "steam-http-fail" };

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                if (!doc.RootElement.TryGetProperty("items", out var items) ||
                    items.ValueKind != JsonValueKind.Array ||
                    items.GetArrayLength() == 0)
                {
                    return new GameCoverLookupResult { Similarity = 0, Source = "steam-empty" };
                }

                double bestScore = 0;
                int? bestId = null;
                string? bestName = null;

                foreach (var item in items.EnumerateArray().Take(8))
                {
                    if (!item.TryGetProperty("id", out var idEl)) continue;
                    int id = idEl.ValueKind == JsonValueKind.Number
                        ? idEl.GetInt32()
                        : (int.TryParse(idEl.GetString(), out int parsed) ? parsed : 0);
                    if (id <= 0) continue;

                    string name = item.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                    double score = TitleSimilarity(cleanTitle, SanitizeTitle(name));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestId = id;
                        bestName = name;
                    }
                }

                if (bestId is int appId && bestScore >= MinSimilarity)
                {
                    return new GameCoverLookupResult
                    {
                        CoverUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
                        Similarity = bestScore,
                        Source = $"steam:{appId}:{bestName}"
                    };
                }

                return new GameCoverLookupResult { Similarity = bestScore, Source = "steam-low-sim" };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                OcDebugLog.Write("steam store search: " + ex.Message);
                return new GameCoverLookupResult { Similarity = 0, Source = "steam-error" };
            }
            finally
            {
                Gate.Release();
            }
        }

        private static async Task<GameCoverLookupResult> TrySteamGridDbAsync(
            string cleanTitle,
            string apiKey,
            CancellationToken ct)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var req = new HttpRequestMessage(
                    HttpMethod.Get,
                    "https://www.steamgriddb.com/api/v2/search/autocomplete/" + Uri.EscapeDataString(cleanTitle));
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return new GameCoverLookupResult { Similarity = 0, Source = "sgdb-search-fail" };

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Array ||
                    data.GetArrayLength() == 0)
                {
                    return new GameCoverLookupResult { Similarity = 0, Source = "sgdb-empty" };
                }

                double bestScore = 0;
                int bestGameId = 0;
                foreach (var item in data.EnumerateArray().Take(6))
                {
                    string name = item.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                    int id = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                        ? idEl.GetInt32() : 0;
                    if (id <= 0) continue;
                    double score = TitleSimilarity(cleanTitle, SanitizeTitle(name));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestGameId = id;
                    }
                }

                if (bestGameId <= 0 || bestScore < MinSimilarity)
                    return new GameCoverLookupResult { Similarity = bestScore, Source = "sgdb-low-sim" };

                string? gridUrl = await FetchSteamGridDbVerticalAsync(bestGameId, apiKey, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(gridUrl))
                    return new GameCoverLookupResult { Similarity = bestScore, Source = "sgdb-no-grid" };

                return new GameCoverLookupResult
                {
                    CoverUrl = gridUrl,
                    Similarity = bestScore,
                    Source = $"sgdb:{bestGameId}"
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                OcDebugLog.Write("steamgriddb: " + ex.Message);
                return new GameCoverLookupResult { Similarity = 0, Source = "sgdb-error" };
            }
            finally
            {
                Gate.Release();
            }
        }

        private static async Task<string?> FetchSteamGridDbVerticalAsync(int gameId, string apiKey, CancellationToken ct)
        {
            // Prefer true vertical library posters (600x900).
            string[] urls =
            {
                $"https://www.steamgriddb.com/api/v2/grids/game/{gameId}?dimensions=600x900&types=static",
                $"https://www.steamgriddb.com/api/v2/grids/game/{gameId}?dimensions=600x900"
            };

            foreach (string url in urls)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var grid in data.EnumerateArray())
                {
                    if (grid.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                    {
                        string? s = u.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                }
            }

            return null;
        }

        /// <summary>0–1 similarity: containment boost + Levenshtein ratio on normalized titles.</summary>
        public static double TitleSimilarity(string a, string b)
        {
            a = NormalizeForCompare(a);
            b = NormalizeForCompare(b);
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
            if (string.Equals(a, b, StringComparison.Ordinal)) return 1.0;

            if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
            {
                double ratio = Math.Min(a.Length, b.Length) / (double)Math.Max(a.Length, b.Length);
                return Math.Max(0.82, ratio);
            }

            // Token Jaccard for multi-word titles
            var ta = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            var tb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            if (ta.Count > 0 && tb.Count > 0)
            {
                int inter = ta.Count(t => tb.Contains(t));
                int union = ta.Count + tb.Count - inter;
                double jaccard = union > 0 ? inter / (double)union : 0;
                double lev = LevenshteinRatio(a, b);
                return Math.Max(jaccard, lev);
            }

            return LevenshteinRatio(a, b);
        }

        private static string NormalizeForCompare(string s)
        {
            s = SanitizeTitle(s).ToLowerInvariant();
            s = s.Replace(":", " ").Replace("-", " ").Replace("_", " ");
            s = MultiSpace.Replace(s, " ").Trim();
            // strip diacritics lightly
            try
            {
                string formD = s.Normalize(NormalizationForm.FormD);
                var sb = new System.Text.StringBuilder(formD.Length);
                foreach (char ch in formD)
                {
                    if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                        sb.Append(ch);
                }
                return MultiSpace.Replace(sb.ToString().Normalize(NormalizationForm.FormC), " ").Trim();
            }
            catch
            {
                return s;
            }
        }

        private static double LevenshteinRatio(string a, string b)
        {
            int dist = Levenshtein(a, b);
            int max = Math.Max(a.Length, b.Length);
            return max == 0 ? 1 : 1.0 - (dist / (double)max);
        }

        private static int Levenshtein(string a, string b)
        {
            int n = a.Length, m = b.Length;
            if (n == 0) return m;
            if (m == 0) return n;

            var prev = new int[m + 1];
            var curr = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;

            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(curr[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }
            return prev[m];
        }
    }
}
