using System;
using System.Collections.Generic;
using System.Linq;

namespace FPSOverlay
{
    public enum FanControlMode
    {
        Off = 0,
        AutoCurve = 1,
        ManualFixed = 2
    }

    public enum FanKind
    {
        Unknown = 0,
        Cpu = 1,
        Gpu = 2,
        Chassis = 3,
        Pump = 4
    }

    public enum FanTempSource
    {
        CpuPackage = 0,
        GpuCore = 1,
        MaxCpuGpu = 2,
        Motherboard = 3
    }

    public enum FanVendorHint
    {
        None = 0,
        Nvidia = 1,
        Amd = 2,
        Intel = 3,
        Asus = 10,
        Lenovo = 11,
        Hp = 12,
        Dell = 13,
        Msi = 14
    }

    public sealed class FanCurvePoint
    {
        public int TempC { get; set; }
        public int PwmPercent { get; set; }

        public FanCurvePoint Clone() => new() { TempC = TempC, PwmPercent = PwmPercent };
    }

    public sealed class FanCurve
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "New Curve";
        public float HysteresisC { get; set; } = 2f;
        public FanTempSource TempSource { get; set; } = FanTempSource.MaxCpuGpu;
        public List<FanCurvePoint> Points { get; set; } = new();

        public FanCurve Clone() => new()
        {
            Id = Id,
            Name = Name,
            HysteresisC = HysteresisC,
            TempSource = TempSource,
            Points = Points.Select(p => p.Clone()).ToList()
        };

        public override string ToString()
        {
            if (Points.Count == 0) return Name;
            string pts = string.Join(" · ", Points
                .OrderBy(p => p.TempC)
                .Select(p => $"{p.TempC}°C→{p.PwmPercent}%"));
            return $"{Name}    {pts}";
        }
    }

    public sealed class FanChannelAssignment
    {
        public string ChannelId { get; set; } = "";
        public FanControlMode Mode { get; set; } = FanControlMode.Off;
        public Guid CurveId { get; set; }
        public int ManualPwmPercent { get; set; } = 40;
    }

    public sealed class FanChannel
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public FanKind Kind { get; init; }
        public string BackendName { get; init; } = "";
        public bool CanReadRpm { get; init; }
        public bool CanWritePwm { get; init; }
        public string? PairedRpmId { get; init; }
        public FanTempSource PreferredTempSource { get; init; } = FanTempSource.MaxCpuGpu;
        public int MinSafePwm { get; init; }
        public string Capabilities { get; init; } = "";
        public FanVendorHint VendorHint { get; init; }

        public static int DefaultMinSafePwm(FanKind kind)
            => kind == FanKind.Gpu ? 30 : 25;

        /// <summary>
        /// GPU writes ignore the user floor and stop at MinSafePwm (never 0).
        /// CPU and chassis writes use the higher of MinSafePwm and the user floor.
        /// </summary>
        public int ClampPwm(int requestedPercent, int userFloorPercent)
        {
            int floor = Kind == FanKind.Gpu ? 0 : Math.Clamp(userFloorPercent, 0, 100);
            int min = Math.Max(MinSafePwm, floor);
            if (min > 100)
                min = 100;
            return Math.Clamp(requestedPercent, min, 100);
        }
    }

    public sealed class FanLiveReading
    {
        public string ChannelId { get; init; } = "";
        public float? Rpm { get; init; }
        public float? PwmPercent { get; init; }
    }

    public sealed class FanApplyResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public int? AppliedPwm { get; init; }

        public static FanApplyResult Ok(string message, int? pwm = null) => new()
        {
            Success = true,
            Message = message,
            AppliedPwm = pwm
        };

        public static FanApplyResult Fail(string message) => new()
        {
            Success = false,
            Message = message
        };
    }

    public sealed class FanRpmReading
    {
        public string Name { get; init; } = "";
        public FanKind Kind { get; init; }
        public float Rpm { get; init; }
        public float? PwmPercent { get; init; }
    }

    public sealed class FanChannelStatus
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public FanKind Kind { get; init; }
        public string BackendName { get; init; } = "";
        public bool CanReadRpm { get; init; }
        public bool CanWritePwm { get; init; }
        public float? Rpm { get; init; }
        public float? PwmPercent { get; init; }
        public int? AppliedPwm { get; init; }
        public string Capabilities { get; init; } = "";
        public string Status { get; init; } = "";
    }

    public sealed class FanControlStatus
    {
        public FanControlMode ControlMode { get; set; } = FanControlMode.Off;
        public string ActiveCurveName { get; set; } = "BIOS / Auto";
        public Guid ActiveCurveId { get; set; }
        public bool HasWritableChannel { get; set; }
        public bool HasReadableChannel { get; set; }
        public string BackendSummary { get; set; } = "";
        public string StatusMessage { get; set; } = "";
        public string ThermalReason { get; set; } = "";
        public float? LastCpuTempC { get; set; }
        public float? LastGpuTempC { get; set; }
        public int? LastManualPwm { get; set; }
        public bool FailClosed { get; set; }
        public List<FanChannelStatus> Channels { get; set; } = new();
    }

    public static class FanKindClassifier
    {
        public static FanKind FromName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return FanKind.Unknown;
            string n = name;

            if (ContainsAny(n, "pump", "water", "aio", "liquid"))
                return FanKind.Pump;
            if (ContainsAny(n, "gpu", "graphics", "vrm gpu"))
                return FanKind.Gpu;
            if (ContainsAny(n, "cpu", "processor", "package"))
                return FanKind.Cpu;
            if (ContainsAny(n, "sys", "cha", "chassis", "system", "aux", "opt", "case", "motherboard"))
                return FanKind.Chassis;
            return FanKind.Unknown;
        }

        public static FanTempSource PreferredSource(FanKind kind) => kind switch
        {
            FanKind.Cpu => FanTempSource.CpuPackage,
            FanKind.Gpu => FanTempSource.GpuCore,
            _ => FanTempSource.MaxCpuGpu
        };

        private static bool ContainsAny(string name, params string[] tokens)
        {
            foreach (var t in tokens)
            {
                if (name.Contains(t, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    /// <summary>Crash / exit restore hook so fans return to BIOS auto even if the UI is dead.</summary>
    public static class FanSafetyHook
    {
        public static Action? Restore;
    }
}
