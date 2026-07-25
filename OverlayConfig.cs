using System;
using System.IO;
using System.Text.Json;

namespace FPSOverlay
{
    public enum OverlayPositionPreset
    {
        Custom = 0,
        TopLeft = 1, TopCenter = 2, TopRight = 3,
        MiddleLeft = 4, Center = 5, MiddleRight = 6,
        BottomLeft = 7, BottomCenter = 8, BottomRight = 9
    }

    public class OverlayConfig
    {
        public bool ShowGpuName { get; set; } = true;
        public bool ShowFps { get; set; } = true;
        public bool ShowFrametime { get; set; } = false;
        public bool ShowOnePercentLow { get; set; } = false;
        public bool ShowCpuTemp { get; set; } = true;
        public bool ShowCpuLoad { get; set; } = false;
        public bool ShowGpuTemp { get; set; } = true;
        public bool ShowGpuLoad { get; set; } = false;
        public bool ShowRamUsage { get; set; } = true;
        public bool ShowVramUsage { get; set; } = true;
        /// <summary>Show live GPU OC intensity on the overlay — flex the boost.</summary>
        public bool ShowOverclockStatus { get; set; } = true;
        public bool ShowClock { get; set; } = false;
        public int OverlayProfileIndex { get; set; } = 0;
        public int FontSize { get; set; } = 20;
        public string FontFamily { get; set; } = "Orbitron, Rajdhani, Segoe UI Semibold, Consolas";
        public string TextColorHex { get; set; } = "#F24C1D";
        public System.Collections.Generic.List<string> CustomColors { get; set; } = new System.Collections.Generic.List<string>();
        
        public OverlayPositionPreset PositionPreset { get; set; } = OverlayPositionPreset.TopRight;
        public double PositionPadding { get; set; } = 25;
        public double OverlayX { get; set; } = -1;
        public double OverlayY { get; set; } = -1;
        public bool PositionLocked { get; set; } = true;
        
        public string Language { get; set; } = "TR";
        public string SelectedGpuName { get; set; } = "";

        /// <summary>Off | Auto thermal vibes | Manual fixed curated tier.</summary>
        public OcControlMode OcControlMode { get; set; } = OcControlMode.Off;

        /// <summary>Which curated tier when <see cref="OcControlMode"/> is ManualFixed.</summary>
        public Guid ManualProfileId { get; set; } = Guid.Empty;

        /// <summary>Legacy JSON fossil — we migrate it into OcControlMode on load.</summary>
        public bool AutoGpuOverclockEnabled { get; set; } = false;

        /// <summary>AI OC Assistant HTTP endpoint (empty = local conservative fallback, still cool).</summary>
        public string AiOcApiEndpoint { get; set; } = "";

        /// <summary>Optional Bearer token for the AI OC API — hush hush.</summary>
        public string AiOcApiKey { get; set; } = "";

        /// <summary>When true, POST wraps hw request + chat prompt envelope for LLM gateways.</summary>
        public bool AiOcUseChatEnvelope { get; set; } = false;

        /// <summary>Reported/assumed GPU max PL% we send to AI (also a clamp hint).</summary>
        public int AiOcMaxPowerLimitPercent { get; set; } = 110;

        /// <summary>
        /// GitHub RAW (or any HTTP) URL for gpu_presets.json.
        /// Empty = no remote fetch (local-conservative-v1 only, unless AI API is set).
        /// </summary>
        public string GpuPresetsUrl { get; set; } =
            "https://raw.githubusercontent.com/emx17/gpu-presets/refs/heads/main/gpu_presets.json";

        /// <summary>HTTP timeout (seconds) for remote preset download — don't hang forever.</summary>
        public int GpuPresetsTimeoutSeconds { get; set; } = 8;

        private static string GetConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        }

        public static OverlayConfig Load()
        {
            string configPath = GetConfigPath();
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    var cfg = JsonSerializer.Deserialize<OverlayConfig>(json) ?? new OverlayConfig();
                    // old configs only had AutoGpuOverclockEnabled — migrate that fossil
                    if (cfg.AutoGpuOverclockEnabled && cfg.OcControlMode == OcControlMode.Off)
                        cfg.OcControlMode = OcControlMode.AutoThermal;
                    cfg.AutoGpuOverclockEnabled = cfg.OcControlMode == OcControlMode.AutoThermal;
                    return cfg;
                }
                catch
                {
                    return new OverlayConfig();
                }
            }
            return new OverlayConfig();
        }

        public void Save()
        {
            try
            {
                AutoGpuOverclockEnabled = OcControlMode == OcControlMode.AutoThermal;
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(GetConfigPath(), json);
            }
            catch { }
        }
    }
}

