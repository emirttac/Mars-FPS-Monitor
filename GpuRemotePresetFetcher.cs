using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FPSOverlay
{
    /// <summary>
    /// Pulls GPU OC presets from a remote JSON URL + fuzzy-matches your card.
    /// Never throws at callers — fail soft so we can chill-fall back to local-conservative-v1.
    /// </summary>
    public sealed class GpuRemotePresetFetcher
    {
        public const string DefaultUrl =
            "https://raw.githubusercontent.com/emirttac/gpu-presets/refs/heads/main/gpu_presets.json";

        public const string RetiredUrl =
            "https://raw.githubusercontent.com/emx17/gpu-presets/refs/heads/main/gpu_presets.json";

        /// <summary>Previous third-party catalog. Replaced by <see cref="DefaultUrl"/>.</summary>
        public static bool IsRetiredDefaultUrl(string? url)
            => !string.IsNullOrWhiteSpace(url)
               && string.Equals(url.Trim(), RetiredUrl, StringComparison.OrdinalIgnoreCase);

        /// <summary>Empty and the retired catalog both adopt the official Mars catalog.</summary>
        public static bool ShouldUseOfficialCatalog(string? url)
            => string.IsNullOrWhiteSpace(url) || IsRetiredDefaultUrl(url);

        private static readonly HttpClient Http = CreateClient();
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        // tiny session cache so we don't hammer GitHub
        private static GpuPresetsDocument? _cachedDoc;
        private static string? _cachedUrl;
        private static DateTime _cachedUtc = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
        private static readonly object CacheSync = new();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mars-FPS-Monitor/" + AppInfo.Version);
            c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return c;
        }

        /// <summary>
        /// Download (or reuse cache), match GPU, cook Eco/Perf/Extreme recs.
        /// </summary>
        public async Task<GpuRemotePresetResult> TryResolveAsync(
            string detectedGpuName,
            string? presetsUrl = null,
            int timeoutSeconds = 8,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(presetsUrl))
                return Fail("no_presets_url");

            string url = presetsUrl.Trim();

            try
            {
                var doc = await GetDocumentAsync(url, timeoutSeconds, ct).ConfigureAwait(false);
                if (doc?.Gpus == null || doc.Gpus.Count == 0)
                {
                    OcDebugLog.Write($"Remote presets empty/invalid url={url}");
                    return Fail("empty_or_invalid_json");
                }

                var match = FindBestMatch(detectedGpuName, doc.Gpus.Keys);
                if (match == null)
                {
                    OcDebugLog.Write($"Remote presets: no match for GPU '{detectedGpuName}'");
                    return Fail("gpu_not_found");
                }

                if (!doc.Gpus.TryGetValue(match, out var tiers) || tiers == null)
                    return Fail("gpu_not_found");

                var response = ToAiOcResponse(match, tiers, doc.Gpus.Values);
                OcDebugLog.Write(
                    $"Remote presets matched '{match}' for GPU '{detectedGpuName}' · " +
                    $"raw perf +{tiers.Performance?.Core ?? 0}/+{tiers.Performance?.Mem ?? 0}, " +
                    $"raw extreme +{tiers.Extreme?.Core ?? 0}/+{tiers.Extreme?.Mem ?? 0}");
                return new GpuRemotePresetResult
                {
                    Success = true,
                    Source = "remote_presets",
                    MatchedKey = match,
                    Reason = "matched",
                    Response = response
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // timeout/net/HTTP/parse? hush — UI stays chill
                OcDebugLog.Write($"Remote presets fetch failed: {ex.GetType().Name}: {ex.Message}");
                return Fail("fetch_failed");
            }
        }

        private static async Task<GpuPresetsDocument?> GetDocumentAsync(string url, int timeoutSeconds, CancellationToken ct)
        {
            lock (CacheSync)
            {
                if (_cachedDoc != null &&
                    string.Equals(_cachedUrl, url, StringComparison.OrdinalIgnoreCase) &&
                    DateTime.UtcNow - _cachedUtc < CacheTtl)
                {
                    return _cachedDoc;
                }
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 2, 30)));

            using var resp = await Http.GetAsync(url, linked.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}");

            string json = await resp.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            var doc = JsonSerializer.Deserialize<GpuPresetsDocument>(json, JsonOpts)
                      ?? throw new InvalidOperationException("deserialize_null");

            if (doc.Gpus == null || doc.Gpus.Count == 0)
                throw new InvalidOperationException("missing_gpus");

            lock (CacheSync)
            {
                _cachedDoc = doc;
                _cachedUrl = url;
                _cachedUtc = DateTime.UtcNow;
            }

            return doc;
        }

        /// <summary>
        /// Longest catalog key contained in the detected GPU name wins.
        /// A desktop "RTX 3060" does not match "RTX 3060 Laptop GPU".
        /// A laptop-only key such as "RTX 2050 Laptop GPU" still matches "RTX 2050"
        /// when the catalog has no desktop twin.
        /// </summary>
        public static string? FindBestMatch(string? detectedGpuName, IEnumerable<string> keys)
        {
            if (string.IsNullOrWhiteSpace(detectedGpuName))
                return null;

            var catalog = keys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .ToList();
            if (catalog.Count == 0)
                return null;

            var desktopBases = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in catalog)
            {
                if (!IsLaptopKey(key))
                    desktopBases.Add(Normalize(key));
            }

            string gpu = detectedGpuName.Trim();
            string gpuNorm = Normalize(gpu);
            string gpuCoreNorm = Normalize(StripVendorNoise(gpu));

            string? best = null;
            int bestScore = 0;
            foreach (var key in catalog)
            {
                if (!TryScoreKey(gpu, gpuNorm, gpuCoreNorm, key, desktopBases, out int score))
                    continue;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = key;
                }
            }

            return best;
        }

        private static bool TryScoreKey(
            string gpu,
            string gpuNorm,
            string gpuCoreNorm,
            string key,
            HashSet<string> desktopBases,
            out int score)
        {
            score = 0;
            string keyNorm = Normalize(key);
            if (keyNorm.Length < 4)
                return false;

            bool contained =
                gpu.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(keyNorm) && gpuNorm.Contains(keyNorm, StringComparison.Ordinal)) ||
                (!string.IsNullOrEmpty(keyNorm) && gpuCoreNorm.Contains(keyNorm, StringComparison.Ordinal));
            if (contained)
            {
                score = keyNorm.Length * 10 + 50;
                return true;
            }

            if (!IsLaptopKey(key))
                return false;

            string baseNorm = Normalize(StripLaptopSuffix(key));
            if (baseNorm.Length < 4 || desktopBases.Contains(baseNorm))
                return false;

            bool baseHit =
                gpuNorm.Contains(baseNorm, StringComparison.Ordinal) ||
                gpuCoreNorm.Contains(baseNorm, StringComparison.Ordinal);
            if (!baseHit)
                return false;

            score = baseNorm.Length * 10;
            return true;
        }

        private static bool IsLaptopKey(string key)
            => key.Contains("Laptop", StringComparison.OrdinalIgnoreCase);

        private static string StripLaptopSuffix(string key)
        {
            const string suffix = " Laptop GPU";
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return key[..^suffix.Length];
            return key;
        }

        public static AiOcResponse ToAiOcResponse(
            string matchedKey,
            GpuPresetTiers tiers,
            IEnumerable<GpuPresetTiers>? population = null)
        {
            var safe = GpuPresetSafetyScaler.Scale(tiers, population);
            var list = new List<AiOcRecommendation>();

            void Add(string mode, string profileName, int minT, int maxT, GpuPresetOffsets? o)
            {
                if (o == null) return;
                list.Add(new AiOcRecommendation
                {
                    Mode = mode,
                    ProfileName = profileName,
                    MinTemp = minT,
                    MaxTemp = maxT,
                    CoreOffsetMhz = o.Core,
                    MemoryOffsetMhz = o.Mem,
                    PowerLimitPercent = o.Power,
                    Rationale = $"Safe catalog preset for {matchedKey} ({mode})."
                });
            }

            Add("Eco", "AI Eco", 82, 100, safe.Eco);
            Add("Performance", "AI Performance", 75, 81, safe.Performance);
            Add("Extreme", "AI Extreme", 0, 74, safe.Extreme);

            return new AiOcResponse
            {
                SchemaVersion = 1,
                Model = "remote-gpu-presets",
                Notes = $"Matched remote preset key: {matchedKey}. Offsets were scaled into Mars safety limits.",
                Recommendations = list
            };
        }

        private static GpuRemotePresetResult Fail(string reason) => new()
        {
            Success = false,
            Source = "none",
            Reason = reason,
            Response = null
        };

        private static string StripVendorNoise(string name)
        {
            string s = name;
            string[] junk =
            {
                "NVIDIA", "GeForce", "AMD", "Radeon", "Graphics", "Desktop",
                "Intel", "Arc", "(TM)", "(R)", "™", "®"
            };
            foreach (var j in junk)
                s = Regex.Replace(s, Regex.Escape(j), " ", RegexOptions.IgnoreCase);
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        private static string Normalize(string s)
            => Regex.Replace(s ?? "", @"[\s\-_]+", "", RegexOptions.None).ToUpperInvariant();
    }
}
