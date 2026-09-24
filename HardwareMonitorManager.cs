using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;

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

        private readonly object _computerLock = new();

        private List<string> _availableGpus = new List<string>();
        public IReadOnlyList<string> AvailableGpus => _availableGpus;

        /// <summary>Optional live OC summary for overlay sensors (App plugs this in).</summary>
        public Func<string>? OverclockStatusProvider { get; set; }

        /// <summary>Optional live fan RPM / mode summary for overlay sensors.</summary>
        public Func<string>? FanStatusProvider { get; set; }

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
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true
            };

            LogPawnIoStatus();

            try
            {
                lock (_computerLock)
                {
                    _computer.Open();
                }
            }
            catch (Exception ex)
            {
                OcDebugLog.LogError("LHM Computer.Open failed", ex);
                return;
            }

            DumpHardwareTree();
            GetAvailableGpus();
        }

        /// <summary>Test harness. Does not open LibreHardwareMonitor or start ETW.</summary>
        internal HardwareMonitorManager(Computer computer)
        {
            _computer = computer;
            _fpsMonitor = null!;
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
            lock (_computerLock)
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
            lock (_computerLock)
            {
                int v = _cpuTempSmooth.PushAndRead(ReadCpuTemperatureCCore);
                return v > 0 ? v : _cpuTempSmooth.LastDisplay;
            }
        }

        /// <summary>
        /// Prime LHM + fill CPU/GPU smoothers before the first HUD/home paint.
        /// First Open() often returns empty temps until several Update() cycles;
        /// ACPI thermal zones can also lag a second after boot.
        /// </summary>
        public async System.Threading.Tasks.Task WarmUpSensorsAsync(
            string? selectedGpuName = null,
            int maxPasses = 16,
            int delayMs = 120)
        {
            for (int i = 0; i < maxPasses; i++)
            {
                bool ready = WarmUpSensorsPass(selectedGpuName);
                if (ready)
                {
                    OcDebugLog.Log(OcLogCategory.Sensor, $"sensor warm-up ready after {i + 1} pass(es)");
                    return;
                }

                // Prefer having CPU before we leave splash — GPU can catch up on the HUD.
                if (_cpuTempSmooth.HasSample && i >= 5)
                {
                    OcDebugLog.Log(OcLogCategory.Sensor,
                        $"sensor warm-up CPU ready after {i + 1} pass(es) · gpu={_gpuTempSmooth.HasSample}");
                    return;
                }

                await System.Threading.Tasks.Task.Delay(delayMs).ConfigureAwait(false);
            }

            OcDebugLog.Log(OcLogCategory.Sensor,
                $"sensor warm-up finished without full readings · cpu={_cpuTempSmooth.HasSample} gpu={_gpuTempSmooth.HasSample}");
        }

        /// <summary>
        /// Synchronous warm-up (home intro / fallback). Yields briefly between passes so
        /// LHM / ACPI have time to populate — rapid no-delay loops often stay empty.
        /// </summary>
        public void WarmUpSensors(string? selectedGpuName = null, int maxPasses = 6)
        {
            for (int i = 0; i < maxPasses; i++)
            {
                if (WarmUpSensorsPass(selectedGpuName))
                    return;
                if (i < maxPasses - 1)
                    System.Threading.Thread.Sleep(80);
            }
        }

        private bool WarmUpSensorsPass(string? selectedGpuName)
        {
            lock (_computerLock)
            {
                foreach (var hardware in _computer.Hardware)
                {
                    try { UpdateHardwareRecursive(hardware); }
                    catch { /* best-effort warm-up */ }
                }

                float cpu = ReadCpuTemperatureCCore();
                if (cpu > 0)
                    _cpuTempSmooth.ForcePush(cpu);

                if (!string.IsNullOrWhiteSpace(selectedGpuName))
                {
                    string key = selectedGpuName;
                    if (!string.Equals(_gpuSmoothKey, key, StringComparison.Ordinal))
                    {
                        _gpuSmoothKey = key;
                        _gpuTempSmooth.Reset();
                    }

                    float gpu = ReadGpuTemperatureCCore(key);
                    if (gpu > 0)
                        _gpuTempSmooth.ForcePush(gpu);
                }

                bool cpuReady = _cpuTempSmooth.HasSample;
                bool gpuReady = string.IsNullOrWhiteSpace(selectedGpuName) || _gpuTempSmooth.HasSample;
                return cpuReady && gpuReady;
            }
        }

        /// <summary>Raw LibreHardwareMonitor CPU package/Tctl reading (°C), no smoothing.</summary>
        public float ReadCpuTemperatureC()
        {
            lock (_computerLock)
                return ReadCpuTemperatureCCore();
        }

        private float ReadCpuTemperatureCCore()
        {
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.Cpu) continue;
                    UpdateHardwareRecursive(hardware);
                    float? v = PickCpuTempSensor(hardware);
                    if (v is > 0) return v.Value;
                }

                // MSR / CPU node missed — Super I/O / EC on the motherboard.
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.Motherboard) continue;
                    UpdateHardwareRecursive(hardware);
                    float? v = PickMotherboardCpuTempSensor(hardware);
                    if (v is > 0) return v.Value;
                }
            }
            catch (Exception ex)
            {
                OcDebugLog.LogError("ReadCpuTemperatureC failed", ex);
            }

            // PawnIO missing + OEM board with no Super I/O (this HP 8BB2 dump): ACPI TZ.
            float acpi = WmiCpuTemperatureReader.TryReadCelsius();
            return acpi > 0 ? acpi : 0;
        }

        private static float? PickCpuTempSensor(IHardware hardware)
        {
            ISensor? best = null;
            int bestScore = -1;
            ConsiderCpuTempTree(hardware, motherboardCpuOnly: false, ref best, ref bestScore);
            return best?.Value is float t && IsUsableTemp(t) ? t : null;
        }

        private static float? PickMotherboardCpuTempSensor(IHardware hardware)
        {
            ISensor? best = null;
            int bestScore = -1;
            ConsiderCpuTempTree(hardware, motherboardCpuOnly: true, ref best, ref bestScore);
            return best?.Value is float t && IsUsableTemp(t) ? t : null;
        }

        private static void ConsiderCpuTempTree(IHardware hardware, bool motherboardCpuOnly, ref ISensor? best, ref int bestScore)
        {
            foreach (var s in hardware.Sensors)
            {
                if (s.SensorType != SensorType.Temperature) continue;
                if (s.Value is not float v || !IsUsableTemp(v)) continue;

                string n = s.Name;
                if (n.Contains("Distance", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (motherboardCpuOnly)
                {
                    if (!n.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (n.Contains("VRM", StringComparison.OrdinalIgnoreCase) ||
                        n.Contains("Memory", StringComparison.OrdinalIgnoreCase) ||
                        n.Contains("DIMM", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                int score = ScoreCpuTempSensorName(n);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }

            foreach (var sub in hardware.SubHardware)
                ConsiderCpuTempTree(sub, motherboardCpuOnly, ref best, ref bestScore);
        }

        private static int ScoreCpuTempSensorName(string n)
        {
            if (n.Contains("Package", StringComparison.OrdinalIgnoreCase)) return 100;
            if (n.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Tdie", StringComparison.OrdinalIgnoreCase)) return 95;
            if (n.Contains("CCD", StringComparison.OrdinalIgnoreCase)) return 80;
            if (n.Contains("Core", StringComparison.OrdinalIgnoreCase)) return 60;
            return 10;
        }

        private static bool IsUsableTemp(float t) => float.IsFinite(t) && t > 0;

        public int GetGpuTemperature(string selectedGpuName)
        {
            string key = selectedGpuName ?? "";
            lock (_computerLock)
            {
                if (!string.Equals(_gpuSmoothKey, key, StringComparison.Ordinal))
                {
                    _gpuSmoothKey = key;
                    _gpuTempSmooth.Reset();
                }

                int v = _gpuTempSmooth.PushAndRead(() => ReadGpuTemperatureCCore(key));
                return v > 0 ? v : _gpuTempSmooth.LastDisplay;
            }
        }

        /// <summary>Raw GPU core reading (°C): LHM first, AMD ADL fallback when LHM misses.</summary>
        public float ReadGpuTemperatureC(string selectedGpuName)
        {
            lock (_computerLock)
                return ReadGpuTemperatureCCore(selectedGpuName);
        }

        private float ReadGpuTemperatureCCore(string selectedGpuName)
        {
            try
            {
                bool sawAmd = false;
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.GpuNvidia &&
                        hardware.HardwareType != HardwareType.GpuAmd &&
                        hardware.HardwareType != HardwareType.GpuIntel)
                        continue;

                    if (!IsSelectedGpu(hardware.Name, selectedGpuName))
                        continue;

                    if (hardware.HardwareType == HardwareType.GpuAmd)
                        sawAmd = true;

                    UpdateHardwareRecursive(hardware);
                    float? v = PickGpuTempSensor(hardware);
                    if (v is > 0) return v.Value;
                }

                // LHM missed — ADL only when the selected / matched GPU is AMD.
                if (sawAmd || (IsUnknownGpuLabel(selectedGpuName) && HasAmdGpuCore()))
                {
                    float adl = AmdGpuTemperatureReader.TryReadCoreCelsius(selectedGpuName);
                    if (adl > 0) return adl;
                }
            }
            catch { }
            return 0;
        }

        private bool HasAmdGpuCore()
        {
            try
            {
                return _computer.Hardware.Any(h => h.HardwareType == HardwareType.GpuAmd);
            }
            catch { return false; }
        }

        private static float? PickGpuTempSensor(IHardware hardware)
        {
            ISensor? best = null;
            int bestScore = -1;
            foreach (var s in hardware.Sensors.Where(x =>
                         x.SensorType == SensorType.Temperature &&
                         x.Value is float v && v > 0))
            {
                string n = s.Name;
                // Memory junction is not a useful "GPU temp" for the HUD.
                if (n.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isHotspot =
                    n.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Hotspot", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Junction", StringComparison.OrdinalIgnoreCase);

                int score =
                    n.Contains("Core", StringComparison.OrdinalIgnoreCase) ? 100 :
                    n.Equals("GPU", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Temperature", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Edge", StringComparison.OrdinalIgnoreCase) ? 90 :
                    isHotspot ? 20 : // last resort when AMD only exposes junction/hotspot
                    40;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }
            return best?.Value is float t && t > 0 ? t : null;
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
            lock (_computerLock)
            {
                try
                {
                    bool sawAmd = false;
                    foreach (var hardware in _computer.Hardware)
                    {
                        if (hardware.HardwareType != HardwareType.GpuNvidia &&
                            hardware.HardwareType != HardwareType.GpuAmd &&
                            hardware.HardwareType != HardwareType.GpuIntel)
                            continue;

                        if (!IsSelectedGpu(hardware.Name, selectedGpuName))
                            continue;

                        if (hardware.HardwareType == HardwareType.GpuAmd)
                            sawAmd = true;

                        UpdateHardwareRecursive(hardware);

                        float? core = PickGpuTempSensor(hardware);
                        float? hotspot = null;

                        foreach (var s in hardware.Sensors.Where(x =>
                                     x.SensorType == SensorType.Temperature &&
                                     x.Value is float v && v > 0))
                        {
                            string n = s.Name;
                            if (n.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ||
                                n.Contains("Hotspot", StringComparison.OrdinalIgnoreCase) ||
                                n.Contains("Junction", StringComparison.OrdinalIgnoreCase))
                            {
                                hotspot = s.Value;
                                break;
                            }
                        }

                        if (core is > 0)
                            return new GpuThermalSample { CoreTempC = core, HotspotTempC = hotspot };

                        // Matched GPU but LHM had no usable temp — ADL fallback for AMD.
                        if (hardware.HardwareType == HardwareType.GpuAmd)
                        {
                            var adl = AmdGpuTemperatureReader.TryReadThermalSample(selectedGpuName);
                            if (adl.IsValid) return adl;
                        }

                        return GpuThermalSample.Invalid;
                    }

                    if (sawAmd || (IsUnknownGpuLabel(selectedGpuName) && HasAmdGpuCore()))
                    {
                        var adl = AmdGpuTemperatureReader.TryReadThermalSample(selectedGpuName);
                        if (adl.IsValid) return adl;
                    }
                }
                catch { }

                return GpuThermalSample.Invalid;
            }
        }

        public string GetRamUsage()
        {
            lock (_computerLock)
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
            lock (_computerLock)
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
                bottomParts.Add($"CPU: {(cpuTemp > 0 ? cpuTemp.ToString() : "—")}°C");
            }

            if (config.ShowCpuLoad)
            {
                float load = GetCpuLoadPercent();
                bottomParts.Add($"CPU Load: {(load > 0 ? $"{load:F0}%" : "—")}");
            }

            if (config.ShowGpuTemp)
            {
                int gpuTemp = GetGpuTemperature(gpuName);
                bottomParts.Add($"GPU: {(gpuTemp > 0 ? gpuTemp.ToString() : "—")}°C");
            }

            if (config.ShowGpuLoad)
            {
                float load = GetGpuLoadPercent(gpuName);
                bottomParts.Add($"GPU Load: {(load > 0 ? $"{load:F0}%" : "—")}");
            }

            if (config.ShowVramUsage) bottomParts.Add($"VRAM: {GetVramUsage(gpuName)}");
            if (config.ShowRamUsage) bottomParts.Add($"RAM: {GetRamUsage()}");
            if (config.ShowFanSpeed)
                bottomParts.Add(FormatFanOverlayLine(config));
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
            bool hasFan = config.ShowFanSpeed;

            if (hasPerf && (hasCpu || hasGpu || hasMem || hasFan || config.ShowOverclockStatus || config.ShowClock))
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

            if (hasCpu && (hasGpu || hasMem || hasFan || config.ShowOverclockStatus || config.ShowClock))
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

            if (hasGpu && (hasMem || hasFan || config.ShowOverclockStatus || config.ShowClock))
                lines.Add("────────────");

            if (config.ShowVramUsage)
                lines.Add($"VRAM     {GetVramUsage(gpuName)}");
            if (config.ShowRamUsage)
                lines.Add($"RAM      {GetRamUsage()}");

            if (hasMem && (hasFan || config.ShowOverclockStatus || config.ShowClock))
                lines.Add("────────────");

            if (config.ShowFanSpeed)
            {
                lines.Add(FormatFanOverlayLine(config, tower: true));
                if (config.ShowOverclockStatus || config.ShowClock)
                    lines.Add("────────────");
            }

            if (config.ShowOverclockStatus && OverclockStatusProvider != null)
                lines.Add(OverclockStatusProvider());
            if (config.ShowClock)
                lines.Add($"Clock    {DateTime.Now:HH:mm:ss}");

            return lines.Count == 0 ? "—" : string.Join("\n", lines);
        }

        private static string FmtTemp(int c) => c > 0 ? $"{c}°C" : "—";
        private static string FmtPct(float p) => p > 0 ? $"{p:F0}%" : "N/A";

        private string FormatFanOverlayLine(OverlayConfig config, bool tower = false)
        {
            if (FanStatusProvider != null)
            {
                string fromMgr = FanStatusProvider();
                if (!string.IsNullOrWhiteSpace(fromMgr))
                    return tower ? fromMgr.Replace("FAN: ", "FAN      ", StringComparison.Ordinal) : fromMgr;
            }

            var fans = GetFanSnapshot();
            var cpu = fans.FirstOrDefault(f => f.Kind == FanKind.Cpu && f.Rpm > 0);
            var gpu = fans.FirstOrDefault(f => f.Kind == FanKind.Gpu && f.Rpm > 0);
            var any = fans.FirstOrDefault(f => f.Rpm > 0);

            var bits = new List<string>();
            if (cpu != null) bits.Add($"CPU {cpu.Rpm:F0}");
            if (gpu != null) bits.Add($"GPU {gpu.Rpm:F0}");
            if (bits.Count == 0 && any != null) bits.Add($"{any.Rpm:F0}");

            string body = bits.Count == 0 ? "N/A" : string.Join(" · ", bits);
            return tower ? $"FAN      {body}" : $"FAN: {body}";
        }

        public float GetCpuLoadPercent()
        {
            lock (_computerLock)
            {
                try
                {
                    foreach (var hardware in _computer.Hardware)
                    {
                        if (hardware.HardwareType != HardwareType.Cpu) continue;
                        UpdateHardwareRecursive(hardware);
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
        }

        public float GetGpuLoadPercent(string selectedGpuName)
        {
            lock (_computerLock)
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
        }

        public AdvancedOverlayData GetAdvancedData(string selectedGpuName)
        {
            var data = new AdvancedOverlayData();

            lock (_computerLock)
            {
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

                            data.CpuTemp = _cpuTempSmooth.PushAndRead(ReadCpuTemperatureCCore);
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
                catch (Exception ex)
                {
                    OcDebugLog.LogError("GetAdvancedData LHM update failed", ex);
                }
            }

            return data;
        }

        public T WithComputer<T>(Func<Computer, T> fn)
        {
            lock (_computerLock)
                return fn(_computer);
        }

        public void WithComputer(Action<Computer> action)
        {
            lock (_computerLock)
                action(_computer);
        }

        /// <summary>LibreHardwareMonitor Fan + Control snapshot for overlay / diagnostics (read-only).</summary>
        public IReadOnlyList<FanRpmReading> GetFanSnapshot()
        {
            lock (_computerLock)
            {
                var list = new List<FanRpmReading>();
                try
                {
                    if (_computer?.Hardware == null) return list;
                    CollectFanReadings(_computer.Hardware, list);
                }
                catch (Exception ex)
                {
                    OcDebugLog.LogError("GetFanSnapshot failed", ex);
                }
                return list;
            }
        }

        private static void CollectFanReadings(IEnumerable<IHardware> hardware, List<FanRpmReading> list)
        {
            foreach (var h in hardware)
            {
                try { h.Update(); } catch { }
                var fans = h.Sensors.Where(s => s.SensorType == SensorType.Fan && s.Value is > 0).ToList();
                var controls = h.Sensors.Where(s => s.SensorType == SensorType.Control && s.Value != null).ToList();

                foreach (var fan in fans)
                {
                    float? pwm = controls.FirstOrDefault(c => c.Index == fan.Index)?.Value;
                    list.Add(new FanRpmReading
                    {
                        Name = fan.Name,
                        Kind = FanKindClassifier.FromName(fan.Name) is FanKind.Unknown && IsGpuHardware(h)
                            ? FanKind.Gpu
                            : FanKindClassifier.FromName(fan.Name),
                        Rpm = fan.Value ?? 0,
                        PwmPercent = pwm
                    });
                }

                CollectFanReadings(h.SubHardware, list);
            }
        }

        private static bool IsGpuHardware(IHardware hardware) =>
            hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

        public void TriggerUpdate()
        {
            OnHardwareDataUpdated?.Invoke();
        }

        private static void LogPawnIoStatus()
        {
            try
            {
                string lhmVersion = typeof(Computer).Assembly.GetName().Version?.ToString() ?? "unknown";
                bool installed = PawnIo.IsInstalled;
                string pawnVersion = PawnIo.Version?.ToString() ?? "null";
                OcDebugLog.Log($"[LHM] LibreHardwareMonitorLib {lhmVersion} | PawnIO installed={installed} version={pawnVersion}");
                if (!installed)
                {
                    OcDebugLog.Log("[LHM Warning] PawnIO driver is not installed. MSR CPU temperature reading may fail.");
                    OcDebugLog.Log("[LHM] Will use motherboard Super I/O, then ACPI thermal-zone fallback.");
                }
            }
            catch (Exception ex)
            {
                OcDebugLog.LogError("PawnIO status check failed", ex);
            }
        }

        public void DumpHardwareTree()
        {
            lock (_computerLock)
            {
                try
                {
                    OcDebugLog.Log("[LHM] === Hardware tree dump ===");
                    if (_computer?.Hardware == null)
                    {
                        OcDebugLog.Log("[LHM] Computer.Hardware is null");
                        OcDebugLog.Log("[LHM] === End hardware tree dump ===");
                        return;
                    }

                    int fanCount = 0;
                    int controlCount = 0;
                    foreach (var hardware in _computer.Hardware)
                        DumpHardwareNode(hardware, indent: 0, ref fanCount, ref controlCount);

                    OcDebugLog.Log($"[LHM] Fan sensors={fanCount} · Control (PWM) sensors={controlCount}");
                    OcDebugLog.Log("[LHM] === End hardware tree dump ===");
                }
                catch (Exception ex)
                {
                    OcDebugLog.LogError("DumpHardwareTree failed", ex);
                }
            }
        }

        private static void DumpHardwareNode(IHardware hardware, int indent, ref int fanCount, ref int controlCount)
        {
            string pad = new string(' ', indent * 2);
            try
            {
                hardware.Update();
            }
            catch (Exception ex)
            {
                OcDebugLog.LogError($"{pad}LHM Update failed: {hardware.Name}", ex);
            }

            OcDebugLog.Log($"{pad}[{hardware.HardwareType}] \"{hardware.Name}\" id={hardware.Identifier}");
            foreach (var s in hardware.Sensors)
            {
                if (s.SensorType == SensorType.Fan) fanCount++;
                if (s.SensorType == SensorType.Control) controlCount++;
                string val = s.Value is float f
                    ? f.ToString(CultureInfo.InvariantCulture)
                    : "NULL";
                OcDebugLog.Log($"{pad}  {s.SensorType,-12} \"{s.Name}\" value={val} id={s.Identifier}");
            }

            foreach (var sub in hardware.SubHardware)
                DumpHardwareNode(sub, indent + 1, ref fanCount, ref controlCount);
        }

        private static void UpdateHardwareRecursive(IHardware hardware)
        {
            try
            {
                hardware.Update();
            }
            catch (Exception ex)
            {
                OcDebugLog.LogError($"LHM Update failed: {hardware.Name}", ex);
            }

            foreach (var sub in hardware.SubHardware)
                UpdateHardwareRecursive(sub);
        }

        public void Dispose()
        {
            _fpsMonitor?.Dispose();
            AmdGpuTemperatureReader.Shutdown();

            try
            {
                lock (_computerLock)
                {
                    _computer?.Close();
                }
            }
            catch (Exception ex)
            {
                OcDebugLog.LogError("LHM Computer.Close failed", ex);
            }
        }
    }
}
