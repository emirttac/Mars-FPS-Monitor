using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace FPSOverlay
{
    /// <summary>
    /// Intel Arc OC via IGCL (ControlLib.dll). Needs Arc drivers + that warranty waiver lol.
    /// </summary>
    public sealed class IntelArcGpuOverclockProvider : IGpuOverclockProvider, IDisposable
    {
        private IntPtr _apiHandle;
        private IntPtr _deviceHandle;
        private bool _initialized;
        private double? _baselinePowerWatts;

        public string Name => "Intel IGCL";
        public string Vendor => "Intel Arc";
        public bool IsAvailable { get; private set; }
        public string StatusMessage { get; private set; } = "Not initialized";

        public IntelArcGpuOverclockProvider(Computer? computer, string? selectedGpuName = null)
        {
            var wanted = GpuNameMatch.VendorOf(selectedGpuName);
            if (!GpuNameMatch.ShouldUseProvider(wanted, GpuNameMatch.Vendor.Intel))
            {
                IsAvailable = false;
                StatusMessage = "Selected GPU is not Intel";
                return;
            }

            bool hasIntelGpu = false;
            try
            {
                hasIntelGpu = computer?.Hardware?.Any(h => h.HardwareType == HardwareType.GpuIntel) == true;
            }
            catch { }

            try
            {
                if (!IgclNative.TryLoad())
                {
                    IsAvailable = false;
                    StatusMessage = hasIntelGpu
                        ? "Intel GPU detected · ControlLib.dll (IGCL) not found"
                        : "No Intel Arc GPU / IGCL not found";
                    return;
                }

                if (!Initialize())
                {
                    IsAvailable = false;
                    if (string.IsNullOrEmpty(StatusMessage) || StatusMessage == "Not initialized")
                        StatusMessage = hasIntelGpu
                            ? "Intel GPU detected · IGCL overclock init failed"
                            : "IGCL init failed";
                    return;
                }

                IsAvailable = true;
                StatusMessage = "Ready · Intel Arc IGCL";
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                StatusMessage = hasIntelGpu
                    ? $"Intel GPU detected · IGCL error: {ex.Message}"
                    : ex.Message;
            }
        }

        public OverclockApplyResult Apply(OverclockTarget target)
        {
            if (!IsAvailable || _deviceHandle == IntPtr.Zero)
                return Fail(StatusMessage);

            try
            {
                target = OcHardwareLimits.ClampTarget(target);
                TryCaptureBaselinePower();

                // IGCL wants a warranty waiver before OC writes.
                IgclNative.ctlOverclockWaiverSet(_deviceHandle);

                double core = target.GpuCoreOffsetMhz;
                double mem = target.GpuMemoryOffsetMhz;

                var r1 = IgclNative.ctlOverclockGpuFrequencyOffsetSetV2(_deviceHandle, core);
                // VRAM APIs differ by gen — try V2 then legacy
                var r2 = IgclNative.ctlOverclockVramMemSpeedLimitSetV2(_deviceHandle, mem);
                if (r2 != IgclNative.CTL_RESULT_SUCCESS)
                    r2 = IgclNative.ctlOverclockVramFrequencyOffsetSet(_deviceHandle, mem);

                string plMsg = "PL unchanged";
                if (target.GpuPowerLimitPercent is int plPct)
                {
                    if (_baselinePowerWatts is double baseline)
                    {
                        double watts = OcHardwareLimits.WattsFromPercent(baseline, plPct);
                        int powerResult = IgclNative.ctlOverclockPowerLimitSetV2(_deviceHandle, watts);
                        plMsg = powerResult == IgclNative.CTL_RESULT_SUCCESS
                            ? $"PL {plPct}% ({watts:F0} W)"
                            : "PL write failed";
                    }
                    else
                    {
                        plMsg = "PL skipped (no baseline)";
                    }
                }

                bool coreOk = r1 == IgclNative.CTL_RESULT_SUCCESS;
                bool memOk = r2 == IgclNative.CTL_RESULT_SUCCESS;
                bool ok = coreOk && memOk;
                return new OverclockApplyResult
                {
                    Success = ok,
                    Message = ok
                        ? $"Intel Arc applied Core +{target.GpuCoreOffsetMhz} / Mem +{target.GpuMemoryOffsetMhz} / {plMsg}"
                        : $"IGCL write failed · core {(coreOk ? "ok" : $"0x{r1:X}")} · mem {(memOk ? "ok" : $"0x{r2:X}")} / {plMsg}",
                    Applied = ok ? target : null
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

        private bool Initialize()
        {
            var args = new IgclNative.ctl_init_args_t
            {
                Size = (uint)Marshal.SizeOf<IgclNative.ctl_init_args_t>(),
                Version = IgclNative.CTL_MAKE_VERSION(1, 1),
                AppVersion = IgclNative.CTL_MAKE_VERSION(1, 0),
                flags = 0,
                SupportedVersion = 0,
                ApplicationUID = 0
            };

            var result = IgclNative.ctlInit(ref args, out _apiHandle);
            if (result != IgclNative.CTL_RESULT_SUCCESS || _apiHandle == IntPtr.Zero)
            {
                StatusMessage = $"ctlInit failed (0x{result:X})";
                return false;
            }

            uint count = 0;
            result = IgclNative.ctlEnumerateDevices(_apiHandle, ref count, IntPtr.Zero);
            if (result != IgclNative.CTL_RESULT_SUCCESS || count == 0)
            {
                StatusMessage = "No IGCL devices";
                return false;
            }

            IntPtr array = Marshal.AllocHGlobal(IntPtr.Size * (int)count);
            try
            {
                result = IgclNative.ctlEnumerateDevices(_apiHandle, ref count, array);
                if (result != IgclNative.CTL_RESULT_SUCCESS)
                {
                    StatusMessage = $"ctlEnumerateDevices failed (0x{result:X})";
                    return false;
                }

                if (count != 1)
                {
                    StatusMessage = "Multiple Intel GPUs cannot be distinguished. Overclock writes are disabled.";
                    return false;
                }

                _deviceHandle = Marshal.ReadIntPtr(array, 0);
                _initialized = _deviceHandle != IntPtr.Zero;
                return _initialized;
            }
            finally
            {
                Marshal.FreeHGlobal(array);
            }
        }

        private void TryCaptureBaselinePower()
        {
            if (_baselinePowerWatts.HasValue || _deviceHandle == IntPtr.Zero)
                return;

            try
            {
                double watts = 0;
                int result = IgclNative.ctlOverclockPowerLimitGetV2(_deviceHandle, ref watts);
                if (result != IgclNative.CTL_RESULT_SUCCESS)
                    return;
                if (double.IsNaN(watts) || double.IsInfinity(watts) || watts < 10 || watts > 2000)
                    return;
                _baselinePowerWatts = watts;
            }
            catch (Exception ex)
            {
                OcDebugLog.Write("IGCL power limit read failed: " + ex.Message);
            }
        }

        private static OverclockApplyResult Fail(string message) => new()
        {
            Success = false,
            Message = message
        };

        public void Dispose()
        {
            if (_initialized && _apiHandle != IntPtr.Zero)
            {
                try { IgclNative.ctlClose(_apiHandle); } catch { }
                _apiHandle = IntPtr.Zero;
                _deviceHandle = IntPtr.Zero;
            }
        }
    }

    internal static class IgclNative
    {
        public const int CTL_RESULT_SUCCESS = 0;

        public static uint CTL_MAKE_VERSION(uint major, uint minor) => (major << 16) | (minor & 0xffff);

        private static IntPtr _module;

        public static bool TryLoad()
        {
            if (_module != IntPtr.Zero) return true;

            string[] candidates =
            {
                "ControlLib.dll",
                Path.Combine(Environment.SystemDirectory, "ControlLib.dll"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Intel", "Intel Graphics Control Library", "ControlLib.dll"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Intel", "Intel Graphics Control Library", "ControlLib.dll")
            };

            foreach (var c in candidates)
            {
                try
                {
                    _module = LoadLibrary(c);
                    if (_module != IntPtr.Zero) return true;
                }
                catch { }
            }

            // poke DriverStore a little (best effort, shallow)
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DriverStore", "FileRepository");
                if (Directory.Exists(root))
                {
                    foreach (var dir in Directory.EnumerateDirectories(root, "iigd*")
                                 .Concat(Directory.EnumerateDirectories(root, "*intel*graphics*"))
                                 .Take(40))
                    {
                        string dll = Path.Combine(dir, "ControlLib.dll");
                        if (!File.Exists(dll)) continue;
                        _module = LoadLibrary(dll);
                        if (_module != IntPtr.Zero) return true;
                    }
                }
            }
            catch { }

            return false;
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ctlInit(ref ctl_init_args_t pInitArgs, out IntPtr phAPIHandle);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ctlClose(IntPtr hAPIHandle);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ctlEnumerateDevices(IntPtr hAPIHandle, ref uint pCount, IntPtr phDevices);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ctlOverclockWaiverSet(IntPtr hDeviceAdapter);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ctlOverclockGpuFrequencyOffsetSetV2(IntPtr hDeviceAdapter, double frequencyOffset);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ctlOverclockVramFrequencyOffsetSet(IntPtr hDeviceAdapter, double frequencyOffset);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ctlOverclockVramMemSpeedLimitSetV2(IntPtr hDeviceAdapter, double memSpeedLimit);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ctlOverclockPowerLimitGetV2(IntPtr hDeviceAdapter, ref double powerLimit);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ctlOverclockPowerLimitSetV2(IntPtr hDeviceAdapter, double powerLimit);

        [StructLayout(LayoutKind.Sequential)]
        public struct ctl_init_args_t
        {
            public uint Size;
            public uint Version;
            public uint AppVersion;
            public uint flags;
            public uint SupportedVersion;
            public ulong ApplicationUID;
        }
    }
}
