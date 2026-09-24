using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FPSOverlay
{
    /// <summary>Root doc for the official Mars GPU preset catalog.</summary>
    public sealed class GpuPresetsDocument
    {
        [JsonPropertyName("gpus")]
        public Dictionary<string, GpuPresetTiers>? Gpus { get; set; }
    }

    public sealed class GpuPresetTiers
    {
        [JsonPropertyName("eco")]
        public GpuPresetOffsets? Eco { get; set; }

        [JsonPropertyName("performance")]
        public GpuPresetOffsets? Performance { get; set; }

        [JsonPropertyName("extreme")]
        public GpuPresetOffsets? Extreme { get; set; }
    }

    public sealed class GpuPresetOffsets
    {
        [JsonPropertyName("core")]
        public int Core { get; set; }

        [JsonPropertyName("mem")]
        public int Mem { get; set; }

        [JsonPropertyName("power")]
        public int Power { get; set; }
    }

    public sealed class GpuRemotePresetResult
    {
        public bool Success { get; init; }
        public string Source { get; init; } = "none";
        public string? MatchedKey { get; init; }
        public string? Reason { get; init; }
        public AiOcResponse? Response { get; init; }
    }
}
