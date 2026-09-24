using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FPSOverlay
{
    /// <summary>Client → AI backend request payload. what we send upstairs.</summary>
    public sealed class AiOcRequest
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("client")]
        public string Client { get; set; } = "mars-fps-monitor";

        [JsonPropertyName("locale")]
        public string Locale { get; set; } = "TR";

        [JsonPropertyName("hardware")]
        public AiOcHardwareSnapshot Hardware { get; set; } = new();

        [JsonPropertyName("constraints")]
        public AiOcClientConstraints Constraints { get; set; } = AiOcClientConstraints.Default;
    }

    public sealed class AiOcHardwareSnapshot
    {
        [JsonPropertyName("gpu_model")]
        public string GpuModel { get; set; } = "";

        [JsonPropertyName("vendor")]
        public string Vendor { get; set; } = "";

        [JsonPropertyName("vram_gb")]
        public double VramGb { get; set; }

        /// <summary>HW-reported max PL% (vs stock TDP), if we know it.</summary>
        [JsonPropertyName("max_power_limit_percent")]
        public int MaxPowerLimitPercent { get; set; } = 110;

        [JsonPropertyName("current_temp_c")]
        public float? CurrentTempC { get; set; }

        [JsonPropertyName("hotspot_temp_c")]
        public float? HotspotTempC { get; set; }
    }

    /// <summary>Hard limits we advertise to the model (also enforced locally via SafetyClamp).</summary>
    public sealed class AiOcClientConstraints
    {
        [JsonPropertyName("max_core_offset_mhz")]
        public int MaxCoreOffsetMhz { get; set; } = AiOcSafetyClamp.MaxCoreOffsetMhz;

        [JsonPropertyName("max_memory_offset_mhz")]
        public int MaxMemoryOffsetMhz { get; set; } = AiOcSafetyClamp.MaxMemoryOffsetMhz;

        [JsonPropertyName("min_core_offset_mhz")]
        public int MinCoreOffsetMhz { get; set; } = AiOcSafetyClamp.MinCoreOffsetMhz;

        [JsonPropertyName("min_memory_offset_mhz")]
        public int MinMemoryOffsetMhz { get; set; } = AiOcSafetyClamp.MinMemoryOffsetMhz;

        [JsonPropertyName("min_power_limit_percent")]
        public int MinPowerLimitPercent { get; set; } = AiOcSafetyClamp.MinPowerLimitPercent;

        [JsonPropertyName("max_power_limit_percent")]
        public int MaxPowerLimitPercent { get; set; } = AiOcSafetyClamp.MaxPowerLimitPercent;

        [JsonPropertyName("policy")]
        public string Policy { get; set; } = "extremely_conservative_safe";

        public static AiOcClientConstraints Default { get; } = new();
    }

    /// <summary>AI → client response payload (pre-clamp — still spicy until SafetyClamp).</summary>
    public sealed class AiOcResponse
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();

        [JsonPropertyName("recommendations")]
        public List<AiOcRecommendation> Recommendations { get; set; } = new();
    }

    public sealed class AiOcRecommendation
    {
        /// <summary>Eco | Performance | Extreme — pick your fighter</summary>
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "Eco";

        [JsonPropertyName("profile_name")]
        public string ProfileName { get; set; } = "AI Profile";

        [JsonPropertyName("min_temp")]
        public int MinTemp { get; set; }

        [JsonPropertyName("max_temp")]
        public int MaxTemp { get; set; }

        [JsonPropertyName("core_offset_mhz")]
        public int CoreOffsetMhz { get; set; }

        [JsonPropertyName("memory_offset_mhz")]
        public int MemoryOffsetMhz { get; set; }

        /// <summary>Null = leave power limit at stock. chill.</summary>
        [JsonPropertyName("power_limit_percent")]
        public int? PowerLimitPercent { get; set; }

        [JsonPropertyName("rationale")]
        public string? Rationale { get; set; }

        public OcProfile ToProfile() => new()
        {
            Id = Guid.NewGuid(),
            ProfileName = ProfileName,
            MinTemp = MinTemp,
            MaxTemp = MaxTemp,
            CoreOffsetMhz = CoreOffsetMhz,
            MemoryOffsetMhz = MemoryOffsetMhz,
            PowerLimitPercent = PowerLimitPercent
        };
    }

    public sealed class AiOcClampResult
    {
        public AiOcResponse Response { get; init; } = new();
        public List<string> ClampLog { get; init; } = new();
        public bool AnyClamped => ClampLog.Count > 0;
    }

    public static class AiOcJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static string SerializeRequest(AiOcRequest request)
            => JsonSerializer.Serialize(request, Options);

        public static AiOcResponse? DeserializeResponse(string json)
            => JsonSerializer.Deserialize<AiOcResponse>(json, Options);
    }
}
