using System;
using System.Collections.Generic;
using System.Management;

namespace FPSOverlay
{
    /// <summary>
    /// Dell Alienware / G-Series via AWCCWmiMethodFunction Thermal_Control / Thermal_Information.
    /// Quiet=150, Balanced=151, Performance=152 (USTT variants exist on some models).
    /// </summary>
    public sealed class DellOemFanBackend : OemProfileFanBackendBase
    {
        private const string Ns = @"root\WMI";
        private const string ClassName = "AWCCWmiMethodFunction";

        private const uint ModeQuiet = 150;
        private const uint ModeBalanced = 151;
        private const uint ModePerformance = 152;
        private const uint ModeFullSpeed = 153;

        private ManagementObject? _awcc;
        private readonly List<uint> _fanIds = new();

        public override string Name => "Dell AWCC WMI";
        protected override string ChannelId => "oem:dell:thermal";
        protected override string ChannelName => "Dell Thermal Profile";
        protected override FanVendorHint VendorHint => FanVendorHint.Dell;

        protected override bool MatchesChassis() => OemLaptopInfo.Is(OemLaptopVendor.Dell);

        protected override bool TryConnect(out string detail)
        {
            _awcc?.Dispose();
            _awcc = null;
            _fanIds.Clear();

            if (!WmiFanHelper.ClassExists(Ns, ClassName))
            {
                detail = "AWCCWmiMethodFunction not present (non-AWCC Dell or AWCC missing)";
                return false;
            }

            _awcc = WmiFanHelper.FirstInstance(Ns, ClassName);
            if (_awcc == null)
            {
                detail = "AWCCWmiMethodFunction instance missing";
                return false;
            }

            DiscoverFans();
            detail = _fanIds.Count > 0
                ? $"Ready · {OemLaptopInfo.Model} · {_fanIds.Count} fan id(s)"
                : $"Ready · {OemLaptopInfo.Model} · thermal profiles";
            return true;
        }

        private void DiscoverFans()
        {
            // Fan IDs are typically 49–99; probe a small known set used by AWCC tools.
            uint[] candidates = { 50, 51, 59, 60, 49, 52 };
            foreach (uint id in candidates)
            {
                if (TryThermalInfo((id << 8) | 5, out uint rpm) && rpm != 0xFFFFFFFFu && rpm > 0)
                    _fanIds.Add(id);
            }
        }

        private bool TryThermalInfo(uint arg, out uint result)
        {
            result = 0xFFFFFFFFu;
            if (_awcc == null) return false;
            if (!WmiFanHelper.TryInvoke(_awcc, "Thermal_Information",
                    new Dictionary<string, object> { ["arg2"] = arg }, out var outs))
                return false;
            uint? v = WmiFanHelper.ReadUIntOut(outs, "argr", "ReturnValue", "Data");
            if (v == null) return false;
            result = v.Value;
            return result != 0xFFFFFFFFu;
        }

        private bool TryThermalControl(uint arg, out uint result)
        {
            result = 0xFFFFFFFFu;
            if (_awcc == null) return false;
            if (!WmiFanHelper.TryInvoke(_awcc, "Thermal_Control",
                    new Dictionary<string, object> { ["arg2"] = arg }, out var outs))
                return false;
            uint? v = WmiFanHelper.ReadUIntOut(outs, "argr", "ReturnValue", "Data");
            result = v ?? 0xFFFFFFFFu;
            // Thermal_Control success is typically 0.
            return result == 0;
        }

        protected override bool TrySetProfile(OemFanProfileMapper.Profile profile, out string detail)
        {
            uint mode = profile switch
            {
                OemFanProfileMapper.Profile.Quiet => ModeQuiet,
                OemFanProfileMapper.Profile.Performance => ModePerformance,
                OemFanProfileMapper.Profile.FullSpeed => ModeFullSpeed,
                _ => ModeBalanced
            };

            uint arg = (mode << 8) | 1u;
            if (!TryThermalControl(arg, out _))
            {
                // Some G-Series reject FullSpeed (153); fall back to Performance then G-Mode.
                if (profile == OemFanProfileMapper.Profile.FullSpeed)
                {
                    const uint modeG = 171;
                    if (TryThermalControl((modeG << 8) | 1u, out _))
                    {
                        detail = "AWCC G-Mode (171)";
                        return true;
                    }
                    if (TryThermalControl((ModePerformance << 8) | 1u, out _))
                    {
                        // Last resort: push per-fan boost to 100% when Thermal_Control op 2 works.
                        bool boosted = false;
                        foreach (uint fanId in _fanIds)
                        {
                            uint boostArg = (100u << 16) | (fanId << 8) | 2u;
                            if (TryThermalControl(boostArg, out _))
                                boosted = true;
                        }
                        detail = boosted
                            ? "AWCC Performance + fan boost 100%"
                            : "AWCC Performance (FullSpeed unavailable)";
                        return true;
                    }
                }

                detail = $"Thermal_Control mode={mode} failed";
                return false;
            }

            // Extra: when FullSpeed succeeded as 153, also nudge boost on known fans.
            if (profile == OemFanProfileMapper.Profile.FullSpeed)
            {
                foreach (uint fanId in _fanIds)
                {
                    uint boostArg = (100u << 16) | (fanId << 8) | 2u;
                    TryThermalControl(boostArg, out _);
                }
            }

            detail = $"AWCC mode={mode}";
            return true;
        }

        protected override bool TryGetProfile(out OemFanProfileMapper.Profile profile, out string detail)
        {
            // AWCC does not always expose current mode via Thermal_Information; fall back.
            profile = OemFanProfileMapper.Profile.Balanced;
            detail = "assumed Balanced";
            return true;
        }

        protected override bool TryReadRpm(out float? rpm)
        {
            rpm = null;
            if (_awcc == null || _fanIds.Count == 0) return false;

            float max = 0;
            bool any = false;
            foreach (uint id in _fanIds)
            {
                if (TryThermalInfo((id << 8) | 5, out uint r) && r > 0 && r < 20000)
                {
                    max = Math.Max(max, r);
                    any = true;
                }
            }

            if (!any) return false;
            rpm = max;
            return true;
        }
    }
}
