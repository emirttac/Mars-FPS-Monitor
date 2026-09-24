using System;
using System.Collections.Generic;
using System.Management;

namespace FPSOverlay
{
    /// <summary>
    /// ASUS ATK WMI (AsusAtkWmi_WMNB / similar): DEVS / DSTS for thermal + fan-boost policy.
    /// Device IDs mirror asus-wmi / G-Helper (0x00110018 fan boost, 0x00120075 throttle policy).
    /// </summary>
    public sealed class AsusOemFanBackend : OemProfileFanBackendBase
    {
        private const string Ns = @"root\WMI";
        // Throttle / thermal policy (0=default/balanced-ish, 1=performance, 2=quiet) — model dependent.
        private const uint DevThrottleThermalPolicy = 0x00120075;
        private const uint DevFanBoostMode = 0x00110018;
        private const uint DevThermalCtrl = 0x00110011;

        private ManagementObject? _atk;
        private string _className = "";
        private string _setMethod = "DEVS";
        private string _getMethod = "DSTS";
        private uint _activeDevice = DevThrottleThermalPolicy;

        public override string Name => "ASUS ATK WMI";
        protected override string ChannelId => "oem:asus:thermal";
        protected override string ChannelName => "ASUS Thermal Profile";
        protected override FanVendorHint VendorHint => FanVendorHint.Asus;

        protected override bool MatchesChassis() => OemLaptopInfo.Is(OemLaptopVendor.Asus);

        protected override bool TryConnect(out string detail)
        {
            _atk?.Dispose();
            _atk = null;

            _className = WmiFanHelper.FindAsusAtkClass();
            if (string.IsNullOrEmpty(_className))
            {
                detail = "ASUS ATK WMI class not found (ATK driver missing?)";
                return false;
            }

            _atk = WmiFanHelper.FirstInstance(Ns, _className);
            if (_atk == null)
            {
                detail = $"{_className} instance missing";
                return false;
            }

            ResolveMethods();

            if (CanUseDevice(DevThrottleThermalPolicy))
            {
                _activeDevice = DevThrottleThermalPolicy;
                detail = $"Ready · {OemLaptopInfo.Model} · {_className} thermal policy";
                return true;
            }

            if (CanUseDevice(DevFanBoostMode))
            {
                _activeDevice = DevFanBoostMode;
                detail = $"Ready · {OemLaptopInfo.Model} · {_className} fan boost";
                return true;
            }

            if (CanUseDevice(DevThermalCtrl))
            {
                _activeDevice = DevThermalCtrl;
                detail = $"Ready · {OemLaptopInfo.Model} · {_className} thermal ctrl";
                return true;
            }

            detail = $"{_className} present · no known fan/thermal device ID responded";
            return false;
        }

        private void ResolveMethods()
        {
            _setMethod = WmiFanHelper.HasMethod(_atk!, "DEVS") ? "DEVS"
                : WmiFanHelper.HasMethod(_atk!, "DEVICE_SET") ? "DEVICE_SET" : "DEVS";
            _getMethod = WmiFanHelper.HasMethod(_atk!, "DSTS") ? "DSTS"
                : WmiFanHelper.HasMethod(_atk!, "DEVICE_GET") ? "DEVICE_GET" : "DSTS";
        }

        private bool CanUseDevice(uint deviceId)
        {
            return TryDsts(deviceId, out _);
        }

        private bool TryDsts(uint deviceId, out uint value)
        {
            value = 0;
            if (_atk == null) return false;

            // Common signatures: DSTS(Device_ID) or DSTS(dev_id)
            string[] argNames = { "Device_ID", "dev_id", "DeviceID", "device_id" };
            foreach (var arg in argNames)
            {
                if (WmiFanHelper.TryInvoke(_atk, _getMethod,
                        new Dictionary<string, object> { [arg] = deviceId }, out var outs))
                {
                    uint? v = WmiFanHelper.ReadUIntOut(outs, "result", "Data", "ReturnValue", "ret");
                    if (v != null)
                    {
                        // ASUS often returns status in high bits; low byte is the value.
                        value = v.Value & 0xFFu;
                        return true;
                    }
                    // Method existed and ran — treat as usable even if decode is unknown.
                    return true;
                }
            }
            return false;
        }

        private bool TryDevs(uint deviceId, uint control)
        {
            if (_atk == null) return false;
            string[][] argPairs =
            {
                new[] { "Device_ID", "Control_Status" },
                new[] { "dev_id", "ctrl_param" },
                new[] { "DeviceID", "Status" },
                new[] { "device_id", "control_status" }
            };

            foreach (var pair in argPairs)
            {
                if (WmiFanHelper.TryInvoke(_atk, _setMethod,
                        new Dictionary<string, object>
                        {
                            [pair[0]] = deviceId,
                            [pair[1]] = control
                        }, out _))
                    return true;
            }
            return false;
        }

        protected override bool TrySetProfile(OemFanProfileMapper.Profile profile, out string detail)
        {
            // Throttle thermal policy: 0=balanced, 1=performance, 2=quiet (asus-wmi / Armoury).
            // Fan boost (0x00110018): 0=normal, 1=boost, 2=overboost — use overboost for FullSpeed.
            uint thermal = profile switch
            {
                OemFanProfileMapper.Profile.Quiet => 2u,
                OemFanProfileMapper.Profile.Performance => 1u,
                OemFanProfileMapper.Profile.FullSpeed => 1u,
                _ => 0u
            };

            uint fanBoost = profile switch
            {
                OemFanProfileMapper.Profile.Quiet => 0u,
                OemFanProfileMapper.Profile.Balanced => 0u,
                OemFanProfileMapper.Profile.Performance => 1u,
                OemFanProfileMapper.Profile.FullSpeed => 2u,
                _ => 0u
            };

            bool okThermal = true;
            if (_activeDevice == DevThrottleThermalPolicy || _activeDevice == DevThermalCtrl)
                okThermal = TryDevs(_activeDevice, thermal);
            else if (_activeDevice == DevFanBoostMode)
                okThermal = TryDevs(DevFanBoostMode, fanBoost);

            // Always try fan-boost device when present — critical for FullSpeed feeling like max.
            bool okBoost = TryDevs(DevFanBoostMode, fanBoost);
            if (!okBoost && profile == OemFanProfileMapper.Profile.FullSpeed)
            {
                // Some BIOS only accept 1 as max boost.
                okBoost = TryDevs(DevFanBoostMode, 1u);
                fanBoost = 1;
            }

            if (!okThermal && !okBoost)
            {
                detail = $"DEVS thermal/boost failed (dev=0x{_activeDevice:X8})";
                return false;
            }

            detail = $"thermal={thermal} boost={fanBoost}";
            return true;
        }

        protected override bool TryGetProfile(out OemFanProfileMapper.Profile profile, out string detail)
        {
            profile = OemFanProfileMapper.Profile.Balanced;
            detail = "";
            if (!TryDsts(_activeDevice, out uint value))
                return false;

            profile = value switch
            {
                2 => OemFanProfileMapper.Profile.Quiet,
                1 => OemFanProfileMapper.Profile.Performance,
                _ => OemFanProfileMapper.Profile.Balanced
            };
            detail = $"raw={value}";
            return true;
        }
    }
}
