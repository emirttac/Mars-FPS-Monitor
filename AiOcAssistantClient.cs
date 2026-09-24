using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FPSOverlay
{
    public sealed class AiOcAssistResult
    {
        public bool Success { get; init; }
        public string Source { get; init; } = ""; // "api" | "local_fallback" — where the magic came from
        public string Message { get; init; } = "";
        public AiOcClampResult? Clamped { get; init; }
        public AiOcRequest? Request { get; init; }
    }

    /// <summary>
    /// Yeets hardware snapshot at the AI backend (or local conservative fallback),
    /// then ALWAYS runs SafetyClamp before UI sees anything spicy.
    /// </summary>
    public sealed class AiOcAssistantClient : IDisposable
    {
        private static readonly HttpClient SharedHttp = new()
        {
            Timeout = TimeSpan.FromSeconds(45)
        };

        private readonly OverlayConfig _config;

        public AiOcAssistantClient(OverlayConfig config)
        {
            _config = config;
        }

        public AiOcRequest BuildRequest(HardwareMonitorManager hw, string? vendorHint = null)
        {
            var adv = hw.GetAdvancedData(_config.SelectedGpuName);
            var thermal = hw.GetGpuThermalSample(_config.SelectedGpuName);

            string vendor = vendorHint ?? InferVendor(adv.GpuName);
            int maxPl = _config.AiOcMaxPowerLimitPercent > 0
                ? _config.AiOcMaxPowerLimitPercent
                : 110;

            return new AiOcRequest
            {
                SchemaVersion = 1,
                Client = "mars-fps-monitor",
                Locale = _config.Language,
                Hardware = new AiOcHardwareSnapshot
                {
                    GpuModel = string.IsNullOrWhiteSpace(adv.GpuName) ? (_config.SelectedGpuName ?? "Unknown GPU") : adv.GpuName,
                    Vendor = vendor,
                    VramGb = Math.Round(adv.VramTotalGB, 1),
                    MaxPowerLimitPercent = maxPl,
                    CurrentTempC = thermal.CoreTempC ?? (adv.GpuTemp > 0 ? adv.GpuTemp : null),
                    HotspotTempC = thermal.HotspotTempC
                },
                Constraints = AiOcClientConstraints.Default
            };
        }

        public async Task<AiOcAssistResult> RequestSuggestionsAsync(
            HardwareMonitorManager hw,
            string? vendorHint = null,
            CancellationToken ct = default)
        {
            var request = BuildRequest(hw, vendorHint);
            string requestJson = AiOcJson.SerializeRequest(request);

            AiOcResponse? raw = null;
            string source = "local_fallback";
            string message = "local-conservative-v1";

            // 1) GitHub RAW presets first — main path 🚀
            if (!string.IsNullOrWhiteSpace(_config.GpuPresetsUrl))
            {
                try
                {
                    var fetcher = new GpuRemotePresetFetcher();
                    var remote = await fetcher.TryResolveAsync(
                        request.Hardware.GpuModel,
                        _config.GpuPresetsUrl,
                        _config.GpuPresetsTimeoutSeconds,
                        ct).ConfigureAwait(false);

                    if (remote.Success && remote.Response != null)
                    {
                        raw = remote.Response;
                        source = "remote_presets";
                        message = $"Remote preset: {remote.MatchedKey}";
                    }
                    else
                    {
                        OcDebugLog.Write($"Remote presets skipped ({remote.Reason}) → fallback chain");
                    }
                }
                catch (Exception ex)
                {
                    OcDebugLog.Write("Remote presets unexpected: " + ex.Message);
                }
            }

            // 2) optional AI API if still empty + endpoint exists
            if (raw == null && !string.IsNullOrWhiteSpace(_config.AiOcApiEndpoint))
            {
                try
                {
                    raw = await CallRemoteApiAsync(request, requestJson, ct).ConfigureAwait(false);
                    source = "api";
                    message = "AI suggestions received";
                }
                catch (Exception ex)
                {
                    OcDebugLog.Write("AI API failed: " + ex.Message);
                }
            }

            // 3) local-conservative-v1 backup plan, silent ninja
            if (raw == null)
            {
                raw = BuildLocalConservativeFallback(request);
                source = "local_fallback";
                message = "local-conservative-v1";
            }

            var clamped = AiOcSafetyClamp.Apply(raw, request.Hardware);
            return new AiOcAssistResult
            {
                Success = clamped.Response.Recommendations.Count > 0,
                Source = source,
                Message = message,
                Clamped = clamped,
                Request = request
            };
        }

        private async Task<AiOcResponse> CallRemoteApiAsync(AiOcRequest request, string requestJson, CancellationToken ct)
        {
            // backend deal: POST the endpoint
            // body = AiOcRequest JSON, or chat wrap if AiOcUseChatEnvelope
            string body = _config.AiOcUseChatEnvelope
                ? System.Text.Json.JsonSerializer.Serialize(
                    new
                    {
                        hardware_request = request,
                        chat = AiOcPromptTemplate.BuildChatMessages(requestJson)
                    },
                    AiOcJson.Options)
                : requestJson;

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, _config.AiOcApiEndpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(_config.AiOcApiKey))
                httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.AiOcApiKey);

            httpReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await SharedHttp.SendAsync(httpReq, ct).ConfigureAwait(false);
            string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {Truncate(text, 180)}");

            // take raw AiOcResponse OR nested under "data" — backends are chaotic
            var parsed = AiOcJson.DeserializeResponse(text);
            if (parsed?.Recommendations is { Count: > 0 })
                return parsed;

            // peel { data: {...} } if they nested it
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                parsed = AiOcJson.DeserializeResponse(data.GetRawText());
                if (parsed?.Recommendations is { Count: > 0 })
                    return parsed;
            }

            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == System.Text.Json.JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    parsed = AiOcJson.DeserializeResponse(StripMarkdownFence(content));
                    if (parsed != null) return parsed;
                }
            }

            throw new InvalidOperationException("Response JSON missing recommendations");
        }

        /// <summary>Deterministic offline suggestions — still super shy; always clamped later.</summary>
        public static AiOcResponse BuildLocalConservativeFallback(AiOcRequest request)
        {
            var hw = request.Hardware;
            bool hot = hw.CurrentTempC is >= 80 || hw.HotspotTempC is >= 90;
            bool laptopish = hw.VramGb > 0 && hw.VramGb <= 8;
            bool entry = hw.GpuModel.Contains("1650", StringComparison.OrdinalIgnoreCase)
                         || hw.GpuModel.Contains("3050", StringComparison.OrdinalIgnoreCase)
                         || hw.GpuModel.Contains("Arc A3", StringComparison.OrdinalIgnoreCase);

            int perfCore = hot || laptopish || entry ? 15 : 25;
            int perfMem = hot || laptopish || entry ? 25 : 50;
            int extCore = hot ? 25 : (laptopish || entry ? 35 : 50);
            int extMem = hot ? 50 : (laptopish || entry ? 75 : 100);

            return new AiOcResponse
            {
                SchemaVersion = 1,
                Model = "local-conservative-v1",
                Notes = "Generated offline with conservative heuristics. Review before applying.",
                Warnings = hot ? new() { "elevated_temperature" } : new(),
                Recommendations =
                {
                    new AiOcRecommendation
                    {
                        Mode = "Eco",
                        ProfileName = "AI Eco",
                        MinTemp = 82,
                        MaxTemp = 100,
                        CoreOffsetMhz = 0,
                        MemoryOffsetMhz = 0,
                        PowerLimitPercent = null,
                        Rationale = "Stock clocks at high temps to prioritize thermals."
                    },
                    new AiOcRecommendation
                    {
                        Mode = "Performance",
                        ProfileName = "AI Performance",
                        MinTemp = 75,
                        MaxTemp = 81,
                        CoreOffsetMhz = perfCore,
                        MemoryOffsetMhz = perfMem,
                        PowerLimitPercent = null,
                        Rationale = "Mild mid-band offsets sized for this GPU class."
                    },
                    new AiOcRecommendation
                    {
                        Mode = "Extreme",
                        ProfileName = "AI Extreme",
                        MinTemp = 0,
                        MaxTemp = 74,
                        CoreOffsetMhz = extCore,
                        MemoryOffsetMhz = extMem,
                        PowerLimitPercent = null,
                        Rationale = "Cool-band profile kept well under software safety ceilings."
                    }
                }
            };
        }

        private static string InferVendor(string gpuName)
        {
            if (string.IsNullOrWhiteSpace(gpuName)) return "Unknown";
            if (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("GTX", StringComparison.OrdinalIgnoreCase))
                return "NVIDIA";
            if (gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                return "AMD";
            if (gpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                return "Intel";
            return "Unknown";
        }

        private static string StripMarkdownFence(string s)
        {
            s = s.Trim();
            if (!s.StartsWith("```", StringComparison.Ordinal)) return s;
            int firstNl = s.IndexOf('\n');
            if (firstNl < 0) return s;
            s = s[(firstNl + 1)..];
            int end = s.LastIndexOf("```", StringComparison.Ordinal);
            if (end >= 0) s = s[..end];
            return s.Trim();
        }

        private static string Truncate(string s, int max)
            => s.Length <= max ? s : s[..max] + "…";

        public void Dispose() { /* SharedHttp lives for the whole app, don't kill it */ }
    }
}
