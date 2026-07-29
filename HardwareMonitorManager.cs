using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace FPSOverlay
{
    public class AdvancedOverlayData
    {
        public string CpuName { get; set; } = "CPU";
        public float CpuLoad { get; set; }
        public float CpuFreq { get; set; }
        public float CpuTemp { get; set; }
        
        public string RamName { get; set; } = "RAM";
        public float RamUsedGB { get; set; }
        public float RamTotalGB { get; set; }
        public float RamLoad { get; set; }
        
        public string GpuName { get; set; } = "GPU";
        public float GpuLoad { get; set; }
        public float GpuFreq { get; set; }
        public float GpuTemp { get; set; }
        
        public string VramName { get; set; } = "VRAM";
        public float VramUsedGB { get; set; }
        public float VramTotalGB { get; set; }
        public float VramLoad { get; set; }
    }

    public class HardwareMonitorManager : IDisposable
    {
        public event Action? OnHardwareDataUpdated;
        
        private FpsMonitor _fpsMonitor;
        public FpsMonitor FpsMonitor => _fpsMonitor;
        
        private Computer _computer;
        public Computer Computer => _computer;

        private List<string> _availableGpus = new List<string>();
        public IReadOnlyList<string> AvailableGpus => _availableGpus;

        /// <summary>Optional live OC summary for overlay sensors (App plugs this in).</summary>
        public Func<string>? OverclockStatusProvider { get; set; }

        // Display temps: sample every 1000ms into a 5-deep buffer, show Round(average).
        private readonly TemperatureSmoother _cpuTempSmooth = new(bufferSize: 5, sampleIntervalMs: 1000);
        private readonly TemperatureSmoother _gpuTempSmooth = new(bufferSize: 5, sampleIntervalMs: 1000);
        private string _gpuSmoothKey = "";

        public HardwareMonitorManager()
        {
            _fpsMonitor = new FpsMonitor();
            
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true
            };
            
            try
            {
                _computer.Open();
                GetAvailableGpus();
            }
            catch
            {
                // if LHM refuses to wake up... we just vibe and move on
            }
        }

        private void GetAvailableGpus()
        {
            RefreshAvailableGpus();
        }

        /// <summary>
        /// Re-scan LHM adapters (Update first — first Open() can miss GPUs).
        /// Prefer real cards; only fall back to the unknown placeholder if empty.
        /// </summary>
        public void RefreshAvailableGpus()
        {
            _availableGpus.Clear();
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.GpuNvidia &&
                        hardware.HardwareType != HardwareType.GpuAmd &&
                        hardware.HardwareType != HardwareType.GpuIntel)
                        continue;

                    try { hardware.Update(); } catch { }
                    if (!string.IsNullOrWhiteSpace(hardware.Name))
                        _availableGpus.Add(hardware.Name);
                }
            }
            catch { }

            if (_availableGpus.Count == 0)
                _availableGpus.Add("Bilinmeyen GPU / Unknown GPU");
        }

        public static bool IsUnknownGpuLabel(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string n = name.Trim();
            return n.Equals("Bilinmeyen GPU / Unknown GPU", StringComparison.OrdinalIgnoreCase)
                || n.Equals("Bilinmeyen GPU", StringComparison.OrdinalIgnoreCase)
                || n.Equals("Unknown GPU", StringComparison.OrdinalIgnoreCase)
                || n.Equals("Naməlum GPU", StringComparison.OrdinalIgnoreCase)
                || n.Equals("Unbekannte GPU", StringComparison.OrdinalIgnoreCase)
                || n.Equals("GPU Desconocida", StringComparison.OrdinalIgnoreCase)
                || n.Equals("GPU Inconnu", StringComparison.OrdinalIgnoreCase)
                || n.Equals("GPU Desconhecida", StringComparison.OrdinalIgnoreCase)
                || n.Equals("Неизвестная GPU", StringComparison.OrdinalIgnoreCase)
                || n.Equals("未知 GPU", StringComparison.OrdinalIgnoreCase);
        }

        private static int GpuPreferenceScore(string name)
        {
            // Discrete first; iGPU last so laptops pick the game GPU by default
            if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("GTX", StringComparison.OrdinalIgnoreCase))
                return 300;
            if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                return 200;
            if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Iris", StringComparison.OrdinalIgnoreCase))
                return 100;
            return 50;
        }

        /// <summary>
        /// If config has no/unknown/stale GPU, pick the best detected adapter and write it back.
        /// Returns true when SelectedGpuName changed.
        /// </summary>
        public bool EnsureSelectedGpu(OverlayConfig config)
        {
            RefreshAvailableGpus();
            var real = _availableGpus.Where(g => !IsUnknownGpuLabel(g)).ToList();
            if (real.Count == 0)
                return false;

            bool needsPick = IsUnknownGpuLabel(config.SelectedGpuName)
                || !real.Any(g =>
                    g.Equals(config.SelectedGpuName, StringComparison.OrdinalIgnoreCase)
                    || g.Contains(config.SelectedGpuName!, StringComparison.OrdinalIgnoreCase)
                    || config.SelectedGpuName!.Contains(g, StringComparison.OrdinalIgnoreCase));

            if (!needsPick)
                return false;

            string best = real
                .OrderByDescending(GpuPreferenceScore)
                .ThenByDescending(n => n.Length)
                .First();

            config.SelectedGpuName = best;
            return true;
        }

        public string ResolveGpuDisplayName(OverlayConfig config)
        {
            if (!IsUnknownGpuLabel(config.SelectedGpuName))
                return config.SelectedGpuName;

            var real = _availableGpus.Where(g => !IsUnknownGpuLabel(g)).ToList();
            if (real.Count > 0)
                return real.OrderByDescending(GpuPreferenceScore).First();

            return string.IsNullOrWhiteSpace(config.SelectedGpuName) ? "Unknown GPU" : config.SelectedGpuName;
        }

        public int GetCpuTemperature()
        {
            // Sample LHM at most every 1000ms into a 5-deep buffer; display Round(average).
            return _cpuTempSmooth.PushAndRead(ReadCpuTemperatureC);
        }

        /// <summary>Raw LibreHardwareMonitor CPU package/Tctl reading (°C), no smoothing.</summary>
        public float ReadCpuTemperatureC()
        {
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.Cpu) continue;
                    hardware.Update();
                    float? v = PickCpuTempSensor(hardware);
                    if (v.HasValue) return v.Value;
                }
            }
            catch { }
            return 0;
        }

        private static float? PickCpuTempSensor(IHardware hardware)
        {
            ISensor? best = null;
            int bestScore = -1;
            foreach (var s in hardware.Sensors.Where(x => x.SensorType == SensorType.Temperature && x.Value != null))
            {
                string n = s.Name;
                int score =
                    n.Contains("Package", StringComparison.OrdinalIgnoreCase) ? 100 :
                    n.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ? 95 :
                    n.Contains("CCD", StringComparison.OrdinalIgnoreCase) ? 80 :
                    n.Contains("Core", StringComparison.OrdinalIgnoreCase) && !n.Contains("Distance", StringComparison.OrdinalIgnoreCase) ? 60 :
                    10;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }
            return best?.Value;
        }

        public int GetGpuTemperature(string selectedGpuName)
        {
            string key = selectedGpuName ?? "";
            if (!string.Equals(_gpuSmoothKey, key, StringComparison.Ordinal))
            {
                _gpuSmoothKey = key;
                _gpuTempSmooth.Reset();
            }

            return _gpuTempSmooth.PushAndRead(() => ReadGpuTemperatureC(key));
        }

        /// <summary>Raw LibreHardwareMonitor GPU core reading (°C), no smoothing.</summary>
        public float ReadGpuTemperatureC(string selectedGpuName)
        {
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.GpuNvidia &&
                        hardware.HardwareType != HardwareType.GpuAmd &&
                        hardware.HardwareType != HardwareType.GpuIntel)
                        continue;

                    if (!IsSelectedGpu(hardware.Name, selectedGpuName))
                        continue;

                    hardware.Update();
                    float? v = PickGpuTempSensor(hardware);
                    if (v.HasValue) return v.Value;
                }
            }
            catch { }
            return 0;
        }

        private static float? PickGpuTempSensor(IHardware hardware)
        {
            ISensor? best = null;
            int bestScore = -1;
            foreach (var s in hardware.Sensors.Where(x => x.SensorType == SensorType.Temperature && x.Value != null))
            {
                string n = s.Name;
                if (n.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Hotspot", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Junction", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                    continue;

                int score =
                    n.Contains("Core", StringComparison.OrdinalIgnoreCase) ? 100 :
                    n.Equals("GPU", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Temperature", StringComparison.OrdinalIgnoreCase) ? 90 :
                    40;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }
            return best?.Value;
        }

        private static bool IsSelectedGpu(string hardwareName, string selectedGpuName)
        {
            if (IsUnknownGpuLabel(selectedGpuName))
                return true;

            return hardwareName.Contains(selectedGpuName, StringComparison.OrdinalIgnoreCase) ||
                   selectedGpuName.Contains(hardwareName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Core + Hotspot temps for the thermal OC brain. Invalid if we can't read core.
        /// </summary>
        public GpuThermalSample GetGpuThermalSample(string selectedGpuName)
        {
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.GpuNvidia &&
                        hardware.HardwareType != HardwareType.GpuAmd &&
                        hardware.HardwareType != HardwareType.GpuIntel)
                        continue;

                    if (!IsSelectedGpu(hardware.Name, selectedGpuName))
                        continue;

                    hardware.Update();

                    float? core = null;
                    float? hotspot = null;

                    foreach (var s in hardware.Sensors.Where(x => x.SensorType == SensorType.Temperature && x.Value != null))
                    {
                        string n = s.Name;
                        if (n.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ||
                            n.Contains("Hotspot", StringComparison.OrdinalIgnoreCase) ||
                            n.Contains("Junction", StringComparison.OrdinalIgnoreCase))
                        {
                            hotspot = s.Value;
                        }
                        else if (n.Contains("Core", StringComparison.OrdinalIgnoreCase) && core == null)
                        {
                            core = s.Value;
                        }
                    }

                    if (core == null)
                    {
                        var any = hardware.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Temperature && x.Value != null);
                        if (any?.Value != null) core = any.Value;
                    }

                    if (core == null || core <= 0)
                        return GpuThermalSample.Invalid;

                    return new GpuThermalSample { CoreTempC = core, HotspotTempC = hotspot };
                }
            }
            catch { }

            return GpuThermalSample.Invalid;
        }

        public string GetRamUsage()
        {
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType == HardwareType.Memory)
                    {
                        hardware.Update();
                        var usedMemSensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Memory Used"));
                        if (usedMemSensor?.Value != null)
                        {
                            return $"{usedMemSensor.Value.Value:F1} GB";
                        }
                    }
                }
            }
            catch { }
            return "N/A";
        }

        /// <summary>RAM load % and used/total GB for home dashboard fuel gauge.</summary>
        public (float LoadPercent, float UsedGb, float TotalGb) GetRamSnapshot()
        {
            var data = GetAdvancedData("");
            return (
                Math.Clamp(data.RamLoad, 0, 100),
                Math.Max(0, data.RamUsedGB),
                Math.Max(0, data.RamTotalGB));
        }

        public string GetVramUsage(string selectedGpuName)
        {
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType == HardwareType.GpuNvidia || 
                        hardware.HardwareType == HardwareType.GpuAmd ||
                        hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        if (IsSelectedGpu(hardware.Name, selectedGpuName))
                        {
                            hardware.Update();
                            
                            var vramSensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Used"));
                            if (vramSensor == null)
                                vramSensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Memory Used"));

                            if (vramSensor?.Value != null)
                            {
                                float val = vramSensor.Value.Value;
                                if (vramSensor.SensorType == SensorType.SmallData) 
                                {
                                    // LHM SmallData VRAM is usually MiB — divide or cry later
                                    return $"{(val / 1024f):F1} GB";
                                }
                                else 
                                {
                                    return $"{val:F1} GB";
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return "N/A";
        }

        public int GetCurrentFps()
        {
            _fpsMonitor.RefreshFps();
            return _fpsMonitor.CurrentFps;
        }

        public string FormatOverlayText(OverlayConfig config)
        {
            string defaultGpuName = "Unknown GPU";
            string adminReq = "ADMIN REQUIRED!";
            string lang = config.Language ?? "EN";
            switch (lang)
            {
                case "TR": defaultGpuName = "Bilinmeyen GPU"; adminReq = "YÖNETİCİ İZNİ GEREKLİ!"; break;
                case "AZ": defaultGpuName = "Naməlum GPU"; adminReq = "ADMİN İCAZƏSİ LAZIMDIR!"; break;
                case "DE": defaultGpuName = "Unbekannte GPU"; adminReq = "ADMIN-RECHTE ERFORDERLICH!"; break;
                case "ES": defaultGpuName = "GPU Desconocida"; adminReq = "¡SE REQUIERE ADMINISTRADOR!"; break;
                case "FR": defaultGpuName = "GPU Inconnu"; adminReq = "ADMINISTRATEUR REQUIS !"; break;
                case "PT": defaultGpuName = "GPU Desconhecida"; adminReq = "ADMINISTRADOR NECESSÁRIO!"; break;
                case "BR": defaultGpuName = "GPU Desconhecida"; adminReq = "NECESSÁRIO ADMINISTRADOR!"; break;
                case "RU": defaultGpuName = "Неизвестная GPU"; adminReq = "ТРЕБУЮТСЯ ПРАВА АДМИНИСТРАТОРА!"; break;
                case "ZH": defaultGpuName = "未知 GPU"; adminReq = "需要管理员权限！"; break;
            }

            string gpuName = ResolveGpuDisplayName(config);
            if (IsUnknownGpuLabel(gpuName))
                gpuName = defaultGpuName;
            int fps = GetCurrentFps();
            string fpsText = fps < 0 ? adminReq : fps.ToString();
            bool fpsOk = fps >= 0;

            // Tower mode = Afterburner stack, keep the order CLEAN
            if (config.OverlayProfileIndex == 6)
                return FormatTowerOverlay(config, gpuName, fpsText, fpsOk);

            List<string> topParts = new List<string>();
            List<string> bottomParts = new List<string>();

            if (config.ShowGpuName) topParts.Add($"[{gpuName}]");
            if (config.ShowFps) topParts.Add($"FPS: {fpsText}");
            if (config.ShowFrametime && fpsOk)
                topParts.Add($"FT: {_fpsMonitor.CurrentFrametimeMs:F1}ms");
            if (config.ShowOnePercentLow && fpsOk)
                topParts.Add($"1%: {_fpsMonitor.OnePercentLowFps:F0}");

            if (config.ShowCpuTemp)
            {
                int cpuTemp = GetCpuTemperature();
                bottomParts.Add($"CPU: {(cpuTemp > 0 ? cpuTemp.ToString() : "N/A")}°C");
            }

            if (config.ShowCpuLoad)
            {
                float load = GetCpuLoadPercent();
                bottomParts.Add($"CPU Load: {(load > 0 ? $"{load:F0}%" : "N/A")}");
            }

            if (config.ShowGpuTemp)
            {
                int gpuTemp = GetGpuTemperature(gpuName);
                bottomParts.Add($"GPU: {(gpuTemp > 0 ? gpuTemp.ToString() : "N/A")}°C");
            }

            if (config.ShowGpuLoad)
            {
                float load = GetGpuLoadPercent(gpuName);
                bottomParts.Add($"GPU Load: {(load > 0 ? $"{load:F0}%" : "N/A")}");
            }

            if (config.ShowVramUsage) bottomParts.Add($"VRAM: {GetVramUsage(gpuName)}");
            if (config.ShowRamUsage) bottomParts.Add($"RAM: {GetRamUsage()}");
            if (config.ShowOverclockStatus && OverclockStatusProvider != null)
                bottomParts.Add(OverclockStatusProvider());
            if (config.ShowClock)
                bottomParts.Add(DateTime.Now.ToString("HH:mm"));

            bool stacked = config.OverlayProfileIndex == 2 || config.OverlayProfileIndex == 5;
            if (stacked)
            {
                string topStr = string.Join("  |  ", topParts);
                string bottomStr = string.Join("  |  ", bottomParts);

                if (string.IsNullOrEmpty(bottomStr)) return topStr;
                if (string.IsNullOrEmpty(topStr)) return bottomStr;

                return $"{topStr}\n{bottomStr}";
            }

            List<string> allParts = new List<string>(topParts);
            allParts.AddRange(bottomParts);
            return string.Join("  |  ", allParts);
        }

        /// <summary>
        /// Vertical MSI Afterburner-ish stack:
        /// GPU name → FPS block → CPU → GPU → memory → OC → clock
        /// </summary>
        private string FormatTowerOverlay(OverlayConfig config, string gpuName, string fpsText, bool fpsOk)
        {
            var lines = new List<string>();

            if (config.ShowGpuName)
                lines.Add(gpuName);

            if (config.ShowFps)
                lines.Add($"FPS      {fpsText}");
            if (config.ShowFrametime && fpsOk)
                lines.Add($"Frametime {_fpsMonitor.CurrentFrametimeMs:F1} ms");
            if (config.ShowOnePercentLow && fpsOk)
                lines.Add($"1% Low  {_fpsMonitor.OnePercentLowFps:F0}");

            bool hasPerf = config.ShowFps || (config.ShowFrametime && fpsOk) || (config.ShowOnePercentLow && fpsOk);
            bool hasCpu = config.ShowCpuTemp || config.ShowCpuLoad;
            bool hasGpu = config.ShowGpuTemp || config.ShowGpuLoad;
            bool hasMem = config.ShowVramUsage || config.ShowRamUsage;

            if (hasPerf && (hasCpu || hasGpu || hasMem || config.ShowOverclockStatus || config.ShowClock))
                lines.Add("────────────");

            if (config.ShowCpuTemp)
            {
                int cpuTemp = GetCpuTemperature();
                lines.Add($"CPU Temp {FmtTemp(cpuTemp)}");
            }
            if (config.ShowCpuLoad)
            {
                float load = GetCpuLoadPercent();
                lines.Add($"CPU Load {FmtPct(load)}");
            }

            if (hasCpu && (hasGpu || hasMem || config.ShowOverclockStatus || config.ShowClock))
                lines.Add("────────────");

            if (config.ShowGpuTemp)
            {
                int gpuTemp = GetGpuTemperature(gpuName);
                lines.Add($"GPU Temp {FmtTemp(gpuTemp)}");
            }
            if (config.ShowGpuLoad)
            {
                float load = GetGpuLoadPercent(gpuName);
                lines.Add($"GPU Load {FmtPct(load)}");
            }

            if (hasGpu && (hasMem || config.ShowOverclockStatus || config.ShowClock))
                lines.Add("────────────");

            if (config.ShowVramUsage)
                lines.Add($"VRAM     {GetVramUsage(gpuName)}");
            if (config.ShowRamUsage)
                lines.Add($"RAM      {GetRamUsage()}");

            if (hasMem && (config.ShowOverclockStatus || config.ShowClock))
                lines.Add("────────────");

            if (config.ShowOverclockStatus && OverclockStatusProvider != null)
                lines.Add(OverclockStatusProvider());
            if (config.ShowClock)
                lines.Add($"Clock    {DateTime.Now:HH:mm:ss}");

            return lines.Count == 0 ? "—" : string.Join("\n", lines);
        }

        private static string FmtTemp(int c) => c > 0 ? $"{c}°C" : "N/A";
        private static string FmtPct(float p) => p > 0 ? $"{p:F0}%" : "N/A";

        public float GetCpuLoadPercent()
        {
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.Cpu) continue;
                    hardware.Update();
                    var load = hardware.Sensors.FirstOrDefault(s =>
                        s.SensorType == SensorType.Load &&
                        (s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
                         s.Name.Equals("CPU Total", StringComparison.OrdinalIgnoreCase)));
                    if (load?.Value != null) return load.Value.Value;
                    load = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
                    if (load?.Value != null) return load.Value.Value;
                }
            }
            catch { }
            return 0;
        }

        public float GetGpuLoadPercent(string selectedGpuName)
        {
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.GpuNvidia &&
                        hardware.HardwareType != HardwareType.GpuAmd &&
                        hardware.HardwareType != HardwareType.GpuIntel)
                        continue;

                    if (!IsSelectedGpu(hardware.Name, selectedGpuName))
                        continue;

                    hardware.Update();

                    ISensor? best = null;
                    int bestScore = -1;
                    foreach (var s in hardware.Sensors.Where(x => x.SensorType == SensorType.Load && x.Value != null))
                    {
                        string n = s.Name;
                        // skip encode/mem engines or "GPU %" goes wild
                        if (n.Contains("Memory", StringComparison.OrdinalIgnoreCase) ||
                            n.Contains("Video", StringComparison.OrdinalIgnoreCase) ||
                            n.Contains("Encode", StringComparison.OrdinalIgnoreCase) ||
                            n.Contains("Decode", StringComparison.OrdinalIgnoreCase) ||
                            n.Contains("Copy", StringComparison.OrdinalIgnoreCase) ||
                            n.Contains("Bus", StringComparison.OrdinalIgnoreCase))
                            continue;

                        int score =
                            n.Contains("Core", StringComparison.OrdinalIgnoreCase) ? 100 :
                            n.Contains("D3D", StringComparison.OrdinalIgnoreCase) && n.Contains("3D", StringComparison.OrdinalIgnoreCase) ? 70 :
                            n.Contains("GPU", StringComparison.OrdinalIgnoreCase) ? 60 :
                            20;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = s;
                        }
                    }

                    if (best?.Value != null) return best.Value.Value;
                }
            }
            catch { }
            return 0;
        }

        public AdvancedOverlayData GetAdvancedData(string selectedGpuName)
        {
            var data = new AdvancedOverlayData();
            
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();

                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        data.CpuName = hardware.Name;
                        var load = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total"));
                        if (load?.Value != null) data.CpuLoad = load.Value.Value;

                        var clock = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains("Core"));
                        if (clock == null) clock = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock);
                        if (clock?.Value != null) data.CpuFreq = clock.Value.Value;

                        data.CpuTemp = GetCpuTemperature();
                    }
                    else if (hardware.HardwareType == HardwareType.Memory)
                    {
                        var used = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Memory Used"));
                        var avail = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Memory Available"));
                        var load = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Memory"));
                        
                        if (used?.Value != null) data.RamUsedGB = used.Value.Value;
                        if (avail?.Value != null) data.RamTotalGB = data.RamUsedGB + avail.Value.Value;
                        if (load?.Value != null) data.RamLoad = load.Value.Value;
                        else if (data.RamTotalGB > 0) data.RamLoad = (data.RamUsedGB / data.RamTotalGB) * 100f;
                    }
                    else if (hardware.HardwareType == HardwareType.GpuNvidia || 
                             hardware.HardwareType == HardwareType.GpuAmd ||
                             hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        if (IsSelectedGpu(hardware.Name, selectedGpuName))
                        {
                            data.GpuName = hardware.Name;
                            data.GpuLoad = GetGpuLoadPercent(selectedGpuName);
                            data.GpuTemp = GetGpuTemperature(selectedGpuName);

                            var clock = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains("Core"));
                            if (clock?.Value != null) data.GpuFreq = clock.Value.Value;

                            var vramUsed = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Used"));
                            var vramTotal = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Total"));
                            
                            if (vramUsed == null) vramUsed = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Memory Used"));
                            if (vramTotal == null) vramTotal = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Memory Total"));

                            if (vramUsed?.Value != null)
                            {
                                float val = vramUsed.Value.Value;
                                data.VramUsedGB = vramUsed.SensorType == SensorType.SmallData ? val / 1024f : val;
                            }
                            
                            if (vramTotal?.Value != null)
                            {
                                float val = vramTotal.Value.Value;
                                data.VramTotalGB = vramTotal.SensorType == SensorType.SmallData ? val / 1024f : val;
                            }

                            if (data.VramTotalGB > 0)
                            {
                                data.VramLoad = (data.VramUsedGB / data.VramTotalGB) * 100f;
                            }
                        }
                    }
                }
            }
            catch { }

            return data;
        }

        public void TriggerUpdate()
        {
            OnHardwareDataUpdated?.Invoke();
        }

        public void Dispose()
        {
            _fpsMonitor?.Dispose();
            
            try
            {
                _computer?.Close();
            }
            catch { }
        }
    }
}
