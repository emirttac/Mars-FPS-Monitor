using System;
using System.Collections.Generic;
using System.Management;

namespace FPSOverlay
{
    /// <summary>
    /// Lenovo Legion / LOQ / Ideapad Gaming via root\WMI LENOVO_GAMEZONE_DATA Smart Fan Mode.
    /// Modes: Quiet=1, Balanced=2, Performance=3 (Custom=255 on some BIOS — not used for restore).
    /// </summary>
    public sealed class LenovoOemFanBackend : OemProfileFanBackendBase
    {
        private const string Ns = @"root\WMI";
        private const string ClassName = "LENOVO_GAMEZONE_DATA";

        private ManagementObject? _gz;

        public override string Name => "Lenovo Gamezone WMI";
        protected override string ChannelId => "oem:lenovo:smartfan";
        protected override string ChannelName => "Lenovo Smart Fan";
        protected override FanVendorHint VendorHint => FanVendorHint.Lenovo;

        // Quiet=1 Balanced=2 Perf=3 — Balanced is the safe BIOS-like restore.
        protected override OemFanProfileMapper.Profile RestoreProfile
            => OemFanProfileMapper.Profile.Balanced;

        protected override bool MatchesChassis() => OemLaptopInfo.Is(OemLaptopVendor.Lenovo);

        protected override bool TryConnect(out string detail)
        {
            _gz?.Dispose();
            _gz = null;

            if (!WmiFanHelper.ClassExists(Ns, ClassName))
            {
                detail = "LENOVO_GAMEZONE_DATA not present (non-gaming Lenovo or driver missing)";
                return false;
            }

            _gz = WmiFanHelper.FirstInstance(Ns, ClassName);
            if (_gz == null)
            {
                detail = "LENOVO_GAMEZONE_DATA instance missing";
                return false;
            }

            // Prefer Smart Fan when advertised; still try Set/Get if the support query is absent.
            if (WmiFanHelper.TryInvoke(_gz, "IsSupportSmartFan", null, out var supportOut))
            {
                uint? support = WmiFanHelper.ReadUIntOut(supportOut, "Data", "ReturnValue");
                if (support is 0)
                {
                    detail = "Smart Fan not supported on this model";
                    return false;
                }
            }

            if (!TryGetProfile(out var current, out _))
            {
                // Some BIOS expose set but fail get — still usable.
                detail = $"Ready · {OemLaptopInfo.Model} · Smart Fan (get unavailable)";
                return true;
            }

            detail = $"Ready · {OemLaptopInfo.Model} · {OemFanProfileMapper.Label(current)}";
            return true;
        }

        protected override bool TrySetProfile(OemFanProfileMapper.Profile profile, out string detail)
        {
            if (_gz == null)
            {
                detail = "not connected";
                return false;
            }

            // Leave full-speed / dust-cleaner off unless entering FullSpeed.
            if (profile != OemFanProfileMapper.Profile.FullSpeed)
                TrySetFanCooling(false);

            if (profile == OemFanProfileMapper.Profile.FullSpeed)
            {
                // Performance + FanCooling (max) when available; else Custom (255).
                bool cooling = TrySetFanCooling(true);
                bool smart = WmiFanHelper.TryInvoke(_gz, "SetSmartFanMode",
                    new Dictionary<string, object> { ["Data"] = 3u }, out _);
                if (!cooling)
                {
                    smart = WmiFanHelper.TryInvoke(_gz, "SetSmartFanMode",
                        new Dictionary<string, object> { ["Data"] = 255u }, out _) || smart;
                }

                if (!cooling && !smart)
                {
                    detail = "FullSpeed (FanCooling/Custom) failed";
                    return false;
                }

                detail = cooling ? "FanCooling=on + Performance" : "SmartFanMode=Custom(255)";
                return true;
            }

            uint mode = profile switch
            {
                OemFanProfileMapper.Profile.Quiet => 1u,
                OemFanProfileMapper.Profile.Performance => 3u,
                _ => 2u
            };

            if (!WmiFanHelper.TryInvoke(_gz, "SetSmartFanMode",
                    new Dictionary<string, object> { ["Data"] = mode }, out _))
            {
                detail = "SetSmartFanMode failed";
                return false;
            }

            detail = $"SmartFanMode={mode}";
            return true;
        }

        private bool TrySetFanCooling(bool on)
        {
            if (_gz == null) return false;
            if (!WmiFanHelper.HasMethod(_gz, "SetFanCooling"))
                return false;
            return WmiFanHelper.TryInvoke(_gz, "SetFanCooling",
                new Dictionary<string, object> { ["Data"] = on ? 1u : 0u }, out _);
        }

        protected override bool TryGetProfile(out OemFanProfileMapper.Profile profile, out string detail)
        {
            profile = OemFanProfileMapper.Profile.Balanced;
            detail = "";
            if (_gz == null) return false;

            if (!WmiFanHelper.TryInvoke(_gz, "GetSmartFanMode", null, out var outParams))
                return false;

            uint? mode = WmiFanHelper.ReadUIntOut(outParams, "Data", "ReturnValue");
            if (mode == null) return false;

            profile = mode.Value switch
            {
                1 => OemFanProfileMapper.Profile.Quiet,
                3 => OemFanProfileMapper.Profile.Performance,
                255 => OemFanProfileMapper.Profile.FullSpeed,
                _ => OemFanProfileMapper.Profile.Balanced
            };
            detail = $"mode={mode.Value}";
            return true;
        }
    }
}
