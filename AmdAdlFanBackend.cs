using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace FPSOverlay
{
    /// <summary>AMD GPU fan via ADL Overdrive5 (percent) with OverdriveN fallback.</summary>
    public sealed class AmdAdlFanBackend : IFanBackend, IDisposable
    {
        private IntPtr _context;
        private int _adapterIndex = -1;
        private bool _ownsContext;
        private bool _od5;
        private bool _odn;
        private readonly string? _selectedGpuName;

        public string Name => "AMD ADL Fan";
        public bool IsAvailable { get; private set; }
        public string StatusMessage { get; private set; } = "Not initialized";

        public AmdAdlFanBackend(Computer? computer, string? selectedGpuName = null)
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
                if (_adapterIndex >= 0 && (_od5 || _odn))
                {
                    IsAvailable = true;
                    StatusMessage = $"Ready · AMD adapter #{_adapterIndex}";
                }
                else if (hasAmd)
                {
                    IsAvailable = false;
                    StatusMessage = "AMD GPU detected · fan control not available (driver lock / laptop BIOS)";
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
                    ? $"AMD GPU detected · ADL fan init failed: {ex.Message}"
                    : $"No AMD GPU / ADL: {ex.Message}";
            }
        }

        public IReadOnlyList<FanChannel> Probe()
        {
            if (!IsAvailable)
                return Array.Empty<FanChannel>();

            return new[]
            {
                new FanChannel
                {
                    Id = $"adl:adapter{_adapterIndex}:fan0",
                    Name = "AMD GPU Fan",
                    Kind = FanKind.Gpu,
                    BackendName = Name,
                    CanReadRpm = true,
                    CanWritePwm = true,
                    PreferredTempSource = FanTempSource.GpuCore,
                    MinSafePwm = FanChannel.DefaultMinSafePwm(FanKind.Gpu),
                    Capabilities = _od5 ? "ADL Overdrive5 percent" : "ADL OverdriveN fan control",
                    VendorHint = FanVendorHint.Amd
                }
            };
        }

        public FanLiveReading? Read(string channelId)
        {
            if (!IsAvailable || _adapterIndex < 0)
                return null;

            try
            {
                if (_od5)
                {
                    var rpm = new AdlFanNative.ADLFanSpeedValue
                    {
                        Size = Marshal.SizeOf<AdlFanNative.ADLFanSpeedValue>(),
                        SpeedType = AdlFanNative.SPEED_TYPE_RPM
                    };
                    var pct = new AdlFanNative.ADLFanSpeedValue
                    {
                        Size = Marshal.SizeOf<AdlFanNative.ADLFanSpeedValue>(),
                        SpeedType = AdlFanNative.SPEED_TYPE_PERCENT
                    };
                    AdlFanNative.ADL2_Overdrive5_FanSpeed_Get(_context, _adapterIndex, 0, ref rpm);
                    AdlFanNative.ADL2_Overdrive5_FanSpeed_Get(_context, _adapterIndex, 0, ref pct);
                    return new FanLiveReading
                    {
                        ChannelId = channelId,
                        Rpm = rpm.FanSpeed > 0 ? rpm.FanSpeed : null,
                        PwmPercent = pct.FanSpeed >= 0 ? pct.FanSpeed : null
                    };
                }

                if (_odn)
                {
                    var fan = new AdlFanNative.ADLODNFanControl { IMode = AdlNative.ODNControlType_Current };
                    int r = AdlFanNative.ADL2_OverdriveN_FanControl_Get(_context, _adapterIndex, ref fan);
                    if (r != AdlNative.ADL_OK)
                        return new FanLiveReading { ChannelId = channelId };
                    return new FanLiveReading
                    {
                        ChannelId = channelId,
                        Rpm = fan.ICurrentFanSpeed > 0 ? fan.ICurrentFanSpeed : null,
                        PwmPercent = fan.ITargetFanSpeed
                    };
                }
            }
            catch { }

            return new FanLiveReading { ChannelId = channelId };
        }

        public FanApplyResult SetPwm(string channelId, int percent)
        {
            if (!IsAvailable || _adapterIndex < 0)
                return FanApplyResult.Fail(StatusMessage);

            percent = Math.Clamp(percent, FanChannel.DefaultMinSafePwm(FanKind.Gpu), 100);
            try
            {
                if (_od5)
                {
                    var val = new AdlFanNative.ADLFanSpeedValue
                    {
                        Size = Marshal.SizeOf<AdlFanNative.ADLFanSpeedValue>(),
                        SpeedType = AdlFanNative.SPEED_TYPE_PERCENT,
                        FanSpeed = percent,
                        Flags = AdlFanNative.FLAG_USER_DEFINED
                    };
                    int r = AdlFanNative.ADL2_Overdrive5_FanSpeed_Set(_context, _adapterIndex, 0, ref val);
                    if (r != AdlNative.ADL_OK)
                        return FanApplyResult.Fail($"ADL Overdrive5 fan set failed ({r})");
                    return FanApplyResult.Ok($"ADL PWM {percent}%", percent);
                }

                if (_odn)
                {
                    var fan = new AdlFanNative.ADLODNFanControl { IMode = AdlNative.ODNControlType_Current };
                    int g = AdlFanNative.ADL2_OverdriveN_FanControl_Get(_context, _adapterIndex, ref fan);
                    if (g != AdlNative.ADL_OK)
                        return FanApplyResult.Fail($"ADL OverdriveN fan get failed ({g})");
                    fan.IMode = AdlNative.ODNControlType_Manual;
                    fan.IFanControlMode = 1;
                    fan.ITargetFanSpeed = percent;
                    int r = AdlFanNative.ADL2_OverdriveN_FanControl_Set(_context, _adapterIndex, ref fan);
                    if (r != AdlNative.ADL_OK)
                        return FanApplyResult.Fail($"ADL OverdriveN fan set failed ({r})");
                    return FanApplyResult.Ok($"ADL PWM {percent}%", percent);
                }

                return FanApplyResult.Fail("No AMD fan API");
            }
            catch (Exception ex)
            {
                return FanApplyResult.Fail(ex.Message);
            }
        }

        public FanApplyResult RestoreDefaults(string? channelId = null)
        {
            if (!IsAvailable || _adapterIndex < 0)
                return FanApplyResult.Fail(StatusMessage);

            try
            {
                if (_od5)
                {
                    int r = AdlFanNative.ADL2_Overdrive5_FanSpeedToDefault_Set(_context, _adapterIndex, 0);
                    if (r != AdlNative.ADL_OK)
                        return FanApplyResult.Fail($"ADL restore failed ({r})");
                    return FanApplyResult.Ok("ADL fan restored to default");
                }

                if (_odn)
                {
                    var fan = new AdlFanNative.ADLODNFanControl { IMode = AdlNative.ODNControlType_Default };
                    int r = AdlFanNative.ADL2_OverdriveN_FanControl_Set(_context, _adapterIndex, ref fan);
                    if (r != AdlNative.ADL_OK)
                        return FanApplyResult.Fail($"ADL OverdriveN restore failed ({r})");
                    return FanApplyResult.Ok("ADL fan restored to default");
                }

                return FanApplyResult.Ok("Nothing to restore");
            }
            catch (Exception ex)
            {
                return FanApplyResult.Fail(ex.Message);
            }
        }

        private void InitializeAdl()
        {
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
                for (int i = 0; i < count; i++)
                {
                    var info = new AdlNative.AdapterInfo { Size = Marshal.SizeOf<AdlNative.AdapterInfo>() };
                    IntPtr p = IntPtr.Add(buffer, i * Marshal.SizeOf<AdlNative.AdapterInfo>());
                    Marshal.StructureToPtr(info, p, false);
                }

                result = AdlNative.ADL2_Adapter_AdapterInfo_Get(_context, buffer, size);
                if (result != AdlNative.ADL_OK)
                    throw new InvalidOperationException($"ADL2_Adapter_AdapterInfo_Get failed ({result})");

                var active = new System.Collections.Generic.List<(int Index, string Name)>();
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = IntPtr.Add(buffer, i * Marshal.SizeOf<AdlNative.AdapterInfo>());
                    var info = Marshal.PtrToStructure<AdlNative.AdapterInfo>(p);
                    AdlNative.ADL2_Adapter_Active_Get(_context, info.AdapterIndex, out int isActive);
                    if (isActive == 0) continue;
                    active.Add((info.AdapterIndex, info.AdapterName ?? ""));
                }

                int? pick = GpuNameMatch.IndexOfBest(_selectedGpuName, active.Select(a => a.Name).ToList());
                if (pick == null)
                {
                    _adapterIndex = -1;
                    return;
                }

                if (GpuNameMatch.IsUnknown(_selectedGpuName))
                {
                    foreach (var row in active)
                    {
                        _adapterIndex = row.Index;
                        _od5 = false;
                        _odn = false;
                        ProbeFanApis();
                        if (_od5 || _odn)
                            return;
                    }
                    return;
                }

                _adapterIndex = active[pick.Value].Index;
                ProbeFanApis();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void ProbeFanApis()
        {
            try
            {
                var val = new AdlFanNative.ADLFanSpeedValue
                {
                    Size = Marshal.SizeOf<AdlFanNative.ADLFanSpeedValue>(),
                    SpeedType = AdlFanNative.SPEED_TYPE_PERCENT
                };
                int r = AdlFanNative.ADL2_Overdrive5_FanSpeed_Get(_context, _adapterIndex, 0, ref val);
                _od5 = r == AdlNative.ADL_OK;
            }
            catch { _od5 = false; }

            if (_od5) return;

            try
            {
                var fan = new AdlFanNative.ADLODNFanControl { IMode = AdlNative.ODNControlType_Current };
                int r = AdlFanNative.ADL2_OverdriveN_FanControl_Get(_context, _adapterIndex, ref fan);
                _odn = r == AdlNative.ADL_OK;
            }
            catch { _odn = false; }
        }

        public void Dispose()
        {
            if (_ownsContext && _context != IntPtr.Zero)
            {
                try { AdlNative.ADL2_Main_Control_Destroy(_context); } catch { }
                _context = IntPtr.Zero;
            }
        }
    }

    internal static class AdlFanNative
    {
        public const int SPEED_TYPE_PERCENT = 1;
        public const int SPEED_TYPE_RPM = 2;
        public const int FLAG_USER_DEFINED = 1;

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Overdrive5_FanSpeed_Get(IntPtr context, int adapterIndex, int thermalControllerIndex, ref ADLFanSpeedValue fanSpeedValue);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Overdrive5_FanSpeed_Set(IntPtr context, int adapterIndex, int thermalControllerIndex, ref ADLFanSpeedValue fanSpeedValue);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Overdrive5_FanSpeedToDefault_Set(IntPtr context, int adapterIndex, int thermalControllerIndex);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_OverdriveN_FanControl_Get(IntPtr context, int adapterIndex, ref ADLODNFanControl fanControl);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_OverdriveN_FanControl_Set(IntPtr context, int adapterIndex, ref ADLODNFanControl fanControl);

        [StructLayout(LayoutKind.Sequential)]
        public struct ADLFanSpeedValue
        {
            public int Size;
            public int SpeedType;
            public int FanSpeed;
            public int Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ADLODNFanControl
        {
            public int IMode;
            public int IFanControlMode;
            public int ICurrentFanSpeedMode;
            public int ICurrentFanSpeed;
            public int ITargetFanSpeed;
            public int ITargetTemperature;
            public int IMinPerformanceClock;
            public int IMinFanLimit;
        }
    }
}
