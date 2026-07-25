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

        public string Name => "Intel IGCL";
        public string Vendor => "Intel Arc";
        public bool IsAvailable { get; private set; }
        public string StatusMessage { get; private set; } = "Not initialized";

        public IntelArcGpuOverclockProvider(Computer? computer)
        {
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
                // IGCL wants a warranty waiver before OC writes... classic
                IgclNative.ctlOverclockWaiverSet(_deviceHandle);

                double core = Math.Clamp(target.GpuCoreOffsetMhz, -500, 500);
                double mem = Math.Clamp(target.GpuMemoryOffsetMhz, -1000, 1000);

                var r1 = IgclNative.ctlOverclockGpuFrequencyOffsetSetV2(_deviceHandle, core);
                // VRAM APIs differ by gen — try V2 then legacy
                var r2 = IgclNative.ctlOverclockVramMemSpeedLimitSetV2(_deviceHandle, mem);
                if (r2 != IgclNative.CTL_RESULT_SUCCESS)
                    r2 = IgclNative.ctlOverclockVramFrequencyOffsetSet(_deviceHandle, mem);

                string plMsg = "PL stock";
                if (target.GpuPowerLimitPercent is int plPct)
                {
                    double powerWatts = 150.0 * (Math.Clamp(plPct, 50, 130) / 100.0);
                    IgclNative.ctlOverclockPowerLimitSetV2(_deviceHandle, powerWatts);
                    plMsg = $"PL {plPct}%";
                }

                bool ok = r1 == IgclNative.CTL_RESULT_SUCCESS;
                return new OverclockApplyResult
                {
                    Success = ok,
                    Message = ok
                        ? $"Intel Arc applied Core +{target.GpuCoreOffsetMhz} / Mem +{target.GpuMemoryOffsetMhz} / {plMsg}"
                        : $"IGCL frequency set failed (0x{r1:X})",
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

                // just grab adapter #0 and go
                _deviceHandle = Marshal.ReadIntPtr(array, 0);
                _initialized = _deviceHandle != IntPtr.Zero;
                return _initialized;
            }
            finally
            {
                Marshal.FreeHGlobal(array);
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
