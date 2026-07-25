using System;
using System.Linq;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace FPSOverlay
{
    /// <summary>
    /// AMD GPU OC via ADL (atiadlxx.dll) OverdriveN — Adrenalin drivers required.
    /// Applies curated core/mem offsets + power limit %. red team go.
    /// </summary>
    public sealed class AmdGpuOverclockProvider : IGpuOverclockProvider, IDisposable
    {
        private IntPtr _context;
        private int _adapterIndex = -1;
        private bool _ownsContext;

        public string Name => "AMD ADL Overdrive";
        public string Vendor => "AMD";
        public bool IsAvailable { get; private set; }
        public string StatusMessage { get; private set; } = "Not initialized";

        public AmdGpuOverclockProvider(Computer? computer)
        {
            bool hasAmd = false;
            try
            {
                hasAmd = computer?.Hardware?.Any(h => h.HardwareType == HardwareType.GpuAmd) == true;
            }
            catch { }

            try
            {
                InitializeAdl();
                if (_adapterIndex >= 0)
                {
                    IsAvailable = true;
                    StatusMessage = $"Ready · AMD adapter #{_adapterIndex}";
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
                string plMsg = "PL stock";
                if (target.GpuPowerLimitPercent is int pl)
                {
                    ApplyPowerLimit(pl);
                    plMsg = $"PL {pl}%";
                }
                ApplyClockOffsets(target.GpuCoreOffsetMhz, target.GpuMemoryOffsetMhz);

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
            return Apply(OcProfileStore.SafeStock.ToTarget());
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

                for (int i = 0; i < count; i++)
                {
                    IntPtr p = IntPtr.Add(buffer, i * Marshal.SizeOf<AdlNative.AdapterInfo>());
                    var info = Marshal.PtrToStructure<AdlNative.AdapterInfo>(p);

                    AdlNative.ADL2_Adapter_Active_Get(_context, info.AdapterIndex, out int active);
                    if (active == 0) continue;

                    // pick discrete AMD that speaks Overdrive
                    AdlNative.ADL2_Overdrive_Caps(_context, info.AdapterIndex, out int odSupported, out _, out _);
                    if (odSupported != 0)
                    {
                        _adapterIndex = info.AdapterIndex;
                        break;
                    }

                    if (_adapterIndex < 0)
                        _adapterIndex = info.AdapterIndex;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void ApplyPowerLimit(int percent)
        {
            percent = Math.Clamp(percent, 50, 130);

            // PowerLimit iMode0=current; % vs absolute depends on OD version 🙃
            var pl = new AdlNative.ADLODNPowerLimitSetting
            {
                ISize = Marshal.SizeOf<AdlNative.ADLODNPowerLimitSetting>(),
                IMode = AdlNative.ODNControlType_Current
            };

            int get = AdlNative.ADL2_OverdriveN_PowerLimit_Get(_context, _adapterIndex, ref pl);
            if (get != AdlNative.ADL_OK)
                return; // optional on some ASICs — skip if missing, no biggie

            // iPowerLimit might be TDP or % — treat as % of default when Current
            int stock = pl.IPowerLimit;
            if (stock <= 0) stock = 100;
            pl.IMode = AdlNative.ODNControlType_Manual;
            pl.IPowerLimit = (int)Math.Round(stock * (percent / 100.0));
            AdlNative.ADL2_OverdriveN_PowerLimit_Set(_context, _adapterIndex, ref pl);
        }

        private void ApplyClockOffsets(int coreOffsetMhz, int memOffsetMhz)
        {
            // system clocks time
            var sys = CreatePerfLevels();
            int r = AdlNative.ADL2_OverdriveN_SystemClocks_Get(_context, _adapterIndex, ref sys);
            if (r == AdlNative.ADL_OK && sys.INumberOfPerformanceLevels > 0)
            {
                int last = sys.INumberOfPerformanceLevels - 1;
                int baseClock = sys.ALevels[last].IClock / 100; // ADL often uses 10kHz units — divide like the docs say
                if (baseClock <= 0) baseClock = sys.ALevels[last].IClock;

                int newClock = Math.Max(0, baseClock + coreOffsetMhz);
                // write back in the SAME units we read (duh)
                bool was10kHz = sys.ALevels[last].IClock > 10000;
                sys.ALevels[last].IClock = was10kHz ? newClock * 100 : newClock;
                sys.IMode = AdlNative.ODNControlType_Manual;
                AdlNative.ADL2_OverdriveN_SystemClocks_Set(_context, _adapterIndex, ref sys);
            }

            // mem clocks
            var mem = CreatePerfLevels();
            r = AdlNative.ADL2_OverdriveN_MemoryClocks_Get(_context, _adapterIndex, ref mem);
            if (r == AdlNative.ADL_OK && mem.INumberOfPerformanceLevels > 0)
            {
                int last = mem.INumberOfPerformanceLevels - 1;
                int baseClock = mem.ALevels[last].IClock / 100;
                if (baseClock <= 0) baseClock = mem.ALevels[last].IClock;
                int newClock = Math.Max(0, baseClock + memOffsetMhz);
                bool was10kHz = mem.ALevels[last].IClock > 10000;
                mem.ALevels[last].IClock = was10kHz ? newClock * 100 : newClock;
                mem.IMode = AdlNative.ODNControlType_Manual;
                AdlNative.ADL2_OverdriveN_MemoryClocks_Set(_context, _adapterIndex, ref mem);
            }
        }

        private static AdlNative.ADLODNPerformanceLevels CreatePerfLevels()
        {
            var levels = new AdlNative.ADLODNPerformanceLevels
            {
                ISize = Marshal.SizeOf<AdlNative.ADLODNPerformanceLevels>(),
                IMode = AdlNative.ODNControlType_Current,
                INumberOfPerformanceLevels = AdlNative.ADL_MAX_NUM_PERFORMANCE_LEVELS_ODN,
                ALevels = new AdlNative.ADLODNPerformanceLevel[AdlNative.ADL_MAX_NUM_PERFORMANCE_LEVELS_ODN]
            };
            return levels;
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
