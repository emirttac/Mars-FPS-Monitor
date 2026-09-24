using System;
using System.Linq;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace FPSOverlay
{
    /// <summary>
    /// AMD GPU OC via ADL (atiadlxx.dll) OverdriveN — Adrenalin drivers required.
    /// Clock offsets are applied to the driver default table, never stacked on the last write.
    /// </summary>
    public sealed class AmdGpuOverclockProvider : IGpuOverclockProvider, IDisposable
    {
        // adl_defines.h ADLODNControlType. Fan code keeps its own numeric mapping.
        private const int AdlModeDefault = 1;
        private const int AdlModeManual = 3;

        private IntPtr _context;
        private int _adapterIndex = -1;
        private bool _ownsContext;
        private bool _hasCoreBaseline;
        private bool _hasMemBaseline;
        private AdlNative.ADLODNPerformanceLevels _coreBaseline;
        private AdlNative.ADLODNPerformanceLevels _memBaseline;
        private int _basePowerLimit;
        private int _basePowerMin;
        private int _basePowerMax;
        private readonly string? _selectedGpuName;

        public string Name => "AMD ADL Overdrive";
        public string Vendor => "AMD";
        public bool IsAvailable { get; private set; }
        public string StatusMessage { get; private set; } = "Not initialized";

        public AmdGpuOverclockProvider(Computer? computer, string? selectedGpuName = null)
        {
            _selectedGpuName = selectedGpuName;
            bool hasAmd = false;
            try
            {
                hasAmd = computer?.Hardware?.Any(h => h.HardwareType == HardwareType.GpuAmd) == true;
            }
            catch { }

            var wanted = GpuNameMatch.VendorOf(_selectedGpuName);
            if (!GpuNameMatch.ShouldUseProvider(wanted, GpuNameMatch.Vendor.Amd))
            {
                IsAvailable = false;
                StatusMessage = "Selected GPU is not AMD";
                return;
            }

            try
            {
                InitializeAdl();
                if (_adapterIndex >= 0)
                {
                    IsAvailable = true;
                    StatusMessage = $"Ready · AMD adapter #{_adapterIndex}";
                }
                else if (!GpuNameMatch.IsUnknown(_selectedGpuName))
                {
                    IsAvailable = false;
                    StatusMessage = $"No AMD GPU matches '{_selectedGpuName}'";
                }
                else if (hasAmd)
                {
                    IsAvailable = false;
                    StatusMessage = "AMD GPU detected · ADL Overdrive not available (update Adrenalin)";
                }
                else
                {
                    IsAvailable = false;
                    StatusMessage = "No AMD GPU detected";
                }
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                StatusMessage = hasAmd
                    ? $"AMD GPU detected · ADL init failed: {ex.Message}"
                    : $"No AMD GPU / ADL: {ex.Message}";
            }
        }

        public OverclockApplyResult Apply(OverclockTarget target)
        {
            if (!IsAvailable || _adapterIndex < 0)
                return Fail(StatusMessage);

            try
            {
                target = OcHardwareLimits.ClampTarget(target);
                if (!EnsureClockBaselines())
                    return Fail("Could not read default AMD clocks");

                if (!TryWriteClocks(_coreBaseline, target.GpuCoreOffsetMhz, systemClocks: true) ||
                    !TryWriteClocks(_memBaseline, target.GpuMemoryOffsetMhz, systemClocks: false))
                    return Fail("ADL clock write failed");

                string plMsg = "PL unchanged";
                if (target.GpuPowerLimitPercent is int pl)
                {
                    plMsg = TryApplyPowerLimit(pl)
                        ? $"PL {pl}%"
                        : "PL skipped (no default power limit)";
                }

                return new OverclockApplyResult
                {
                    Success = true,
                    Message = $"AMD applied Core +{target.GpuCoreOffsetMhz} / Mem +{target.GpuMemoryOffsetMhz} / {plMsg}",
                    Applied = target
                };
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        public OverclockApplyResult RestoreDefaults()
        {
            if (!IsAvailable || _adapterIndex < 0)
                return Fail(StatusMessage);

            try
            {
                EnsureClockBaselines();
                bool coreOk = _hasCoreBaseline && TryWriteBaseline(_coreBaseline, systemClocks: true);
                bool memOk = _hasMemBaseline && TryWriteBaseline(_memBaseline, systemClocks: false);
                if (_basePowerLimit > 0)
                    TryWritePower(_basePowerLimit, AdlModeDefault);

                if (!coreOk || !memOk)
                    return Fail("ADL restore failed");

                return new OverclockApplyResult
                {
                    Success = true,
                    Message = "AMD clocks restored to driver default",
                    Applied = OcProfileStore.SafeStock.ToTarget()
                };
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        private void InitializeAdl()
        {
            // ADL wants Memory_Alloc callback or it sulks
            var alloc = new AdlNative.ADL_Main_Memory_Alloc(AdlNative.MallocCallback);
            int result = AdlNative.ADL2_Main_Control_Create(alloc, 1, out _context);
            if (result != AdlNative.ADL_OK || _context == IntPtr.Zero)
                throw new InvalidOperationException($"ADL2_Main_Control_Create failed ({result})");

            _ownsContext = true;

            result = AdlNative.ADL2_Adapter_NumberOfAdapters_Get(_context, out int count);
            if (result != AdlNative.ADL_OK || count <= 0)
                throw new InvalidOperationException("No ADL adapters");

            int size = Marshal.SizeOf<AdlNative.AdapterInfo>() * count;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                // some ADL builds: first int = size; AdapterInfo packed tight
                for (int i = 0; i < count; i++)
                {
                    var info = new AdlNative.AdapterInfo { Size = Marshal.SizeOf<AdlNative.AdapterInfo>() };
                    IntPtr p = IntPtr.Add(buffer, i * Marshal.SizeOf<AdlNative.AdapterInfo>());
                    Marshal.StructureToPtr(info, p, false);
                }

                result = AdlNative.ADL2_Adapter_AdapterInfo_Get(_context, buffer, size);
                if (result != AdlNative.ADL_OK)
                    throw new InvalidOperationException($"ADL2_Adapter_AdapterInfo_Get failed ({result})");

                var active = new List<(int Index, string Name, bool Overdrive)>();
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = IntPtr.Add(buffer, i * Marshal.SizeOf<AdlNative.AdapterInfo>());
                    var info = Marshal.PtrToStructure<AdlNative.AdapterInfo>(p);

                    AdlNative.ADL2_Adapter_Active_Get(_context, info.AdapterIndex, out int isActive);
                    if (isActive == 0) continue;

                    AdlNative.ADL2_Overdrive_Caps(_context, info.AdapterIndex, out int odSupported, out _, out _);
                    active.Add((info.AdapterIndex, info.AdapterName ?? "", odSupported != 0));
                }

                int? pick = GpuNameMatch.IndexOfBest(_selectedGpuName, active.Select(a => a.Name).ToList());
                if (pick == null)
                {
                    _adapterIndex = -1;
                    return;
                }

                var chosen = active[pick.Value];
                if (!chosen.Overdrive)
                {
                    int fallback = active.FindIndex(a => a.Overdrive && GpuNameMatch.Matches(_selectedGpuName, a.Name));
                    if (fallback >= 0 && !GpuNameMatch.IsUnknown(_selectedGpuName))
                        chosen = active[fallback];
                    else if (GpuNameMatch.IsUnknown(_selectedGpuName))
                    {
                        int anyOd = active.FindIndex(a => a.Overdrive);
                        if (anyOd >= 0)
                            chosen = active[anyOd];
                    }
                }

                _adapterIndex = chosen.Index;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private bool EnsureClockBaselines()
        {
            if (!_hasCoreBaseline)
                _hasCoreBaseline = TryReadClocks(systemClocks: true, out _coreBaseline);
            if (!_hasMemBaseline)
                _hasMemBaseline = TryReadClocks(systemClocks: false, out _memBaseline);
            return _hasCoreBaseline && _hasMemBaseline;
        }

        private bool TryReadClocks(bool systemClocks, out AdlNative.ADLODNPerformanceLevels levels)
        {
            levels = CreatePerfLevels(AdlModeDefault);
            int result = systemClocks
                ? AdlNative.ADL2_OverdriveN_SystemClocks_Get(_context, _adapterIndex, ref levels)
                : AdlNative.ADL2_OverdriveN_MemoryClocks_Get(_context, _adapterIndex, ref levels);
            if (result != AdlNative.ADL_OK || levels.INumberOfPerformanceLevels <= 0 || levels.ALevels == null)
                return false;

            levels = CloneLevels(levels);
            return true;
        }

        private bool TryWriteClocks(AdlNative.ADLODNPerformanceLevels baseline, int offsetMhz, bool systemClocks)
        {
            var levels = CloneLevels(baseline);
            int last = levels.INumberOfPerformanceLevels - 1;
            if (last < 0 || levels.ALevels == null || last >= levels.ALevels.Length)
                return false;

            levels.ALevels[last].IClock = AmdOverdriveClockMath.ApplyOffset(levels.ALevels[last].IClock, offsetMhz);
            levels.IMode = AdlModeManual;
            levels.ISize = Marshal.SizeOf<AdlNative.ADLODNPerformanceLevels>();
            return TrySetClocks(ref levels, systemClocks);
        }

        private bool TryWriteBaseline(AdlNative.ADLODNPerformanceLevels baseline, bool systemClocks)
        {
            var levels = CloneLevels(baseline);
            levels.IMode = AdlModeDefault;
            levels.ISize = Marshal.SizeOf<AdlNative.ADLODNPerformanceLevels>();
            return TrySetClocks(ref levels, systemClocks);
        }

        private bool TrySetClocks(ref AdlNative.ADLODNPerformanceLevels levels, bool systemClocks)
        {
            int result = systemClocks
                ? AdlNative.ADL2_OverdriveN_SystemClocks_Set(_context, _adapterIndex, ref levels)
                : AdlNative.ADL2_OverdriveN_MemoryClocks_Set(_context, _adapterIndex, ref levels);
            if (result != AdlNative.ADL_OK)
            {
                OcDebugLog.Write($"ADL {(systemClocks ? "core" : "memory")} clock set failed ({result})");
                return false;
            }
            return true;
        }

        private bool TryCaptureBasePower()
        {
            if (_basePowerLimit > 0)
                return true;

            var pl = new AdlNative.ADLODNPowerLimitSetting
            {
                ISize = Marshal.SizeOf<AdlNative.ADLODNPowerLimitSetting>(),
                IMode = AdlModeDefault
            };

            int get = AdlNative.ADL2_OverdriveN_PowerLimit_Get(_context, _adapterIndex, ref pl);
            if (get != AdlNative.ADL_OK || pl.IPowerLimit <= 0)
                return false;

            _basePowerLimit = pl.IPowerLimit;
            _basePowerMin = pl.IPowerLimitMin;
            _basePowerMax = pl.IPowerLimitMax;
            return true;
        }

        private bool TryApplyPowerLimit(int percent)
        {
            if (!TryCaptureBasePower())
                return false;

            int target = OcHardwareLimits.UnitsFromPercent(_basePowerLimit, percent, _basePowerMin, _basePowerMax);
            return TryWritePower(target, AdlModeManual);
        }

        private bool TryWritePower(int powerLimit, int mode)
        {
            var pl = new AdlNative.ADLODNPowerLimitSetting
            {
                ISize = Marshal.SizeOf<AdlNative.ADLODNPowerLimitSetting>(),
                IMode = mode,
                IPowerLimit = powerLimit,
                IPowerLimitMin = _basePowerMin,
                IPowerLimitMax = _basePowerMax
            };
            int set = AdlNative.ADL2_OverdriveN_PowerLimit_Set(_context, _adapterIndex, ref pl);
            if (set != AdlNative.ADL_OK)
            {
                OcDebugLog.Write($"ADL power limit set failed ({set})");
                return false;
            }
            return true;
        }

        private static AdlNative.ADLODNPerformanceLevels CreatePerfLevels(int mode)
        {
            return new AdlNative.ADLODNPerformanceLevels
            {
                ISize = Marshal.SizeOf<AdlNative.ADLODNPerformanceLevels>(),
                IMode = mode,
                INumberOfPerformanceLevels = AdlNative.ADL_MAX_NUM_PERFORMANCE_LEVELS_ODN,
                ALevels = new AdlNative.ADLODNPerformanceLevel[AdlNative.ADL_MAX_NUM_PERFORMANCE_LEVELS_ODN]
            };
        }

        private static AdlNative.ADLODNPerformanceLevels CloneLevels(AdlNative.ADLODNPerformanceLevels src)
        {
            var copy = src;
            copy.ALevels = src.ALevels == null
                ? new AdlNative.ADLODNPerformanceLevel[AdlNative.ADL_MAX_NUM_PERFORMANCE_LEVELS_ODN]
                : (AdlNative.ADLODNPerformanceLevel[])src.ALevels.Clone();
            return copy;
        }

        private static OverclockApplyResult Fail(string message) => new()
        {
            Success = false,
            Message = message
        };

        public void Dispose()
        {
            if (_ownsContext && _context != IntPtr.Zero)
            {
                try { AdlNative.ADL2_Main_Control_Destroy(_context); } catch { }
                _context = IntPtr.Zero;
            }
        }
    }

    internal static class AdlNative
    {
        public const int ADL_OK = 0;
        public const int ADL_MAX_NUM_PERFORMANCE_LEVELS_ODN = 8;
        public const int ODNControlType_Current = 1;
        public const int ODNControlType_Default = 0;
        public const int ODNControlType_Manual = 3;

        public delegate IntPtr ADL_Main_Memory_Alloc(int size);

        public static IntPtr MallocCallback(int size) => Marshal.AllocHGlobal(size);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Main_Control_Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters, out IntPtr context);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Main_Control_Destroy(IntPtr context);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int numAdapters);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr context, IntPtr info, int size);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Adapter_Active_Get(IntPtr context, int adapterIndex, out int status);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Overdrive_Caps(IntPtr context, int adapterIndex, out int supported, out int enabled, out int version);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_OverdriveN_SystemClocks_Get(IntPtr context, int adapterIndex, ref ADLODNPerformanceLevels levels);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_OverdriveN_SystemClocks_Set(IntPtr context, int adapterIndex, ref ADLODNPerformanceLevels levels);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_OverdriveN_MemoryClocks_Get(IntPtr context, int adapterIndex, ref ADLODNPerformanceLevels levels);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_OverdriveN_MemoryClocks_Set(IntPtr context, int adapterIndex, ref ADLODNPerformanceLevels levels);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_OverdriveN_PowerLimit_Get(IntPtr context, int adapterIndex, ref ADLODNPowerLimitSetting setting);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_OverdriveN_PowerLimit_Set(IntPtr context, int adapterIndex, ref ADLODNPowerLimitSetting setting);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_OverdriveN_Temperature_Get(IntPtr context, int adapterIndex, int temperatureType, out int temperature);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Overdrive5_Temperature_Get(IntPtr context, int adapterIndex, int thermalControllerIndex, ref ADLTemperature temperature);

        public const int ODN_TEMP_EDGE = 1;
        public const int ODN_TEMP_HOTSPOT = 7;

        [StructLayout(LayoutKind.Sequential)]
        public struct ADLTemperature
        {
            public int Size;
            /// <summary>Temperature in millidegrees Celsius.</summary>
            public int Temperature;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct AdapterInfo
        {
            public int Size;
            public int AdapterIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string UDID;
            public int BusNumber;
            public int DeviceNumber;
            public int FunctionNumber;
            public int VendorID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string AdapterName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DisplayName;
            public int Present;
            public int Exist;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DriverPath;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DriverPathExt;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string PNPString;
            public int OSDisplayIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ADLODNPerformanceLevel
        {
            public int IClock;
            public int IVDD;
            public int IEnabled;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ADLODNPerformanceLevels
        {
            public int ISize;
            public int IMode;
            public int INumberOfPerformanceLevels;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = ADL_MAX_NUM_PERFORMANCE_LEVELS_ODN)]
            public ADLODNPerformanceLevel[] ALevels;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ADLODNPowerLimitSetting
        {
            public int ISize;
            public int IMode;
            public int IPowerLimit;
            public int IPowerLimitMax;
            public int IPowerLimitMin;
            public int IPowerLimitStep;
        }
    }
}
