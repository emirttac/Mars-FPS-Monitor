using System;
using System.Globalization;
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
        
        public string Language { get; set; } = "";

        /// <summary>Release-notes version already accepted. Empty until the first-launch window is confirmed.</summary>
        public string ReleaseNotesSeenVersion { get; set; } = "";

        /// <summary>Raised when a secret could not be encrypted and the previous protected value was kept.</summary>
        public static event Action? SecretSaveFailed;
        public string SelectedGpuName { get; set; } = "";

        /// <summary>Off | Auto thermal vibes | Manual fixed curated tier.</summary>
        public OcControlMode OcControlMode { get; set; } = OcControlMode.Off;

        /// <summary>Which curated tier when <see cref="OcControlMode"/> is ManualFixed.</summary>
        public Guid ManualProfileId { get; set; } = Guid.Empty;

        /// <summary>Off = BIOS/EC auto · Auto = temperature curve · Manual = fixed PWM.</summary>
        public FanControlMode FanControlMode { get; set; } = FanControlMode.Off;

        /// <summary>Active Silent / Balanced / Performance (or custom) curve id.</summary>
        public Guid FanActiveCurveId { get; set; } = Guid.Empty;

        /// <summary>Fixed PWM when <see cref="FanControlMode"/> is ManualFixed.</summary>
        public int FanManualPwmPercent { get; set; } = 40;

        /// <summary>Floor for CPU / chassis PWM. GPU fans may go to 0.</summary>
        public int FanMinPwmFloor { get; set; } = 15;

        /// <summary>Show live fan RPM on the overlay.</summary>
        public bool ShowFanSpeed { get; set; } = false;

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
        /// GPU preset catalog. The official Mars catalog is the default.
        /// A custom URL is kept. Fetched when the user asks for suggestions, not at startup.
        /// </summary>
        public string GpuPresetsUrl { get; set; } = GpuRemotePresetFetcher.DefaultUrl;

        /// <summary>HTTP timeout (seconds) for remote preset download — don't hang forever.</summary>
        public int GpuPresetsTimeoutSeconds { get; set; } = 8;

        /// <summary>
        /// Optional SteamGridDB API key for non-Steam vertical covers.
        /// Empty = Steam Store search only.
        /// </summary>
        public string SteamGridDbApiKey { get; set; } = "";

        private static string GetConfigPath() => AppPaths.ConfigPath;

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
                    cfg.FanManualPwmPercent = Math.Clamp(cfg.FanManualPwmPercent, 0, 100);
                    cfg.FanMinPwmFloor = Math.Clamp(cfg.FanMinPwmFloor, 0, 100);
                    if (cfg.FanActiveCurveId == Guid.Empty)
                        cfg.FanActiveCurveId = FanCurveStore.BalancedId;

                    cfg.AiOcApiKey = SecretProtector.Unprotect(cfg.AiOcApiKey);
                    cfg.SteamGridDbApiKey = SecretProtector.Unprotect(cfg.SteamGridDbApiKey);
                    if (string.IsNullOrWhiteSpace(cfg.Language))
                        cfg.Language = UiLanguage.FromCulture(CultureInfo.CurrentUICulture);
                    bool migratePresets = GpuRemotePresetFetcher.ShouldUseOfficialCatalog(cfg.GpuPresetsUrl);
                    if (migratePresets)
                        cfg.GpuPresetsUrl = GpuRemotePresetFetcher.DefaultUrl;

                    // Re-save once so legacy plaintext keys and the old preset URL are migrated
                    if (NeedsSecretRewrite(json) || migratePresets)
                    {
                        try { cfg.Save(); }
                        catch (Exception ex) { OcDebugLog.LogError(OcLogCategory.Config, "config migration save failed", ex); }
                    }

                    return cfg;
                }
                catch (Exception ex)
                {
                    OcDebugLog.LogError(OcLogCategory.Config, "config load failed", ex);
                    return new OverlayConfig();
                }
            }
            var fresh = new OverlayConfig
            {
                Language = UiLanguage.FromCulture(CultureInfo.CurrentUICulture)
            };
            return fresh;
        }

        private static bool NeedsSecretRewrite(string json)
        {
            // Plaintext keys present (no dpapi: prefix) — migrate on next Save
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var name in new[] { "AiOcApiKey", "SteamGridDbApiKey" })
                {
                    if (!doc.RootElement.TryGetProperty(name, out var el)) continue;
                    string? v = el.GetString();
                    if (!string.IsNullOrEmpty(v) && !SecretProtector.IsProtected(v))
                        return true;
                }
            }
            catch { /* ignore */ }
            return false;
        }

        private static string ReadStoredSecret(string path, string propertyName)
        {
            try
            {
                if (!File.Exists(path))
                    return "";
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty(propertyName, out var el))
                    return "";
                return el.GetString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        public void Save()
        {
            try
            {
                AutoGpuOverclockEnabled = OcControlMode == OcControlMode.AutoThermal;

                // Serialize with secrets protected; keep in-memory plaintext for runtime use.
                // A failed DPAPI protect never writes the plaintext. The previous blob stays.
                string plainAi = AiOcApiKey ?? "";
                string plainSteam = SteamGridDbApiKey ?? "";
                string path = GetConfigPath();
                string previousAi = ReadStoredSecret(path, "AiOcApiKey");
                string previousSteam = ReadStoredSecret(path, "SteamGridDbApiKey");
                bool aiOk = SecretProtector.TryProtect(plainAi, out string storedAi);
                bool steamOk = SecretProtector.TryProtect(plainSteam, out string storedSteam);
                if (!aiOk)
                    storedAi = SecretProtector.KeepPreviousOnFailure(previousAi);
                if (!steamOk)
                    storedSteam = SecretProtector.KeepPreviousOnFailure(previousSteam);
                AiOcApiKey = storedAi;
                SteamGridDbApiKey = storedSteam;

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                AppPaths.EnsureRoot();
                File.WriteAllText(path, json);

                AiOcApiKey = plainAi;
                SteamGridDbApiKey = plainSteam;
                if (!aiOk || !steamOk)
                {
                    OcDebugLog.Log(OcLogCategory.Config, "secret protect failed · previous protected value kept");
                    try { SecretSaveFailed?.Invoke(); } catch { }
                }
            }
            catch (Exception ex)
            {
                OcDebugLog.LogError(OcLogCategory.Config, "config save failed", ex);
            }
        }
    }
}

