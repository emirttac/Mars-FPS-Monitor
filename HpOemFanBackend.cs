using System;
using System.Management;

namespace FPSOverlay
{
    /// <summary>
    /// HP OMEN / Victus via root\WMI hpqBIntM (Omen Gaming Hub BIOS interface).
    /// SetFanMode CommandType 0x1A with payload [0xFF, mode].
    /// Legacy modes: 0x00 Default, 0x01 Performance, 0x02 Cool, 0x03 Quiet.
    /// </summary>
    public sealed class HpOemFanBackend : OemProfileFanBackendBase
    {
        private const string Ns = @"root\WMI";
        private const string InterfaceClass = "hpqBIntM";
        private const string DataInClass = "hpqBDataIn";
        private const uint DefaultCommand = 0x20008;
        private const uint CmdSetFanMode = 0x1A;
        private const uint CmdGetFanCount = 0x10;
        private const uint CmdMaxFanSpeed = 0x27;

        private static readonly byte[] Sign = { 0x53, 0x45, 0x43, 0x55 }; // "SECU"

        private ManagementObject? _bios;

        public override string Name => "HP Omen BIOS WMI";
        protected override string ChannelId => "oem:hp:fanmode";
        protected override string ChannelName => "HP Fan Mode";
        protected override FanVendorHint VendorHint => FanVendorHint.Hp;

        protected override bool MatchesChassis() => OemLaptopInfo.Is(OemLaptopVendor.Hp);

        protected override bool TryConnect(out string detail)
        {
            _bios?.Dispose();
            _bios = null;

            if (!WmiFanHelper.ClassExists(Ns, InterfaceClass))
            {
                detail = "hpqBIntM not present (Omen/Victus capability driver missing?)";
                return false;
            }

            _bios = WmiFanHelper.FirstInstance(Ns, InterfaceClass);
            if (_bios == null)
            {
                detail = "hpqBIntM instance missing";
                return false;
            }

            // Cheap capability check: GetFanCount (0x10) — failure still allows SetFanMode attempt.
            bool fanCountOk = TryBiosCall(CmdGetFanCount, new byte[] { 0, 0, 0, 0 }, "4", out _);
            detail = fanCountOk
                ? $"Ready · {OemLaptopInfo.Model} · hpqBIntM"
                : $"Ready · {OemLaptopInfo.Model} · hpqBIntM (fan count N/A)";
            return true;
        }

        protected override bool TrySetProfile(OemFanProfileMapper.Profile profile, out string detail)
        {
            // Clear max-fan latch when leaving FullSpeed.
            if (profile != OemFanProfileMapper.Profile.FullSpeed)
                TryBiosCall(CmdMaxFanSpeed, new byte[] { 0x00 }, "0", out _);

            if (profile == OemFanProfileMapper.Profile.FullSpeed)
            {
                // Extreme (0x04) + MaxFanSpeedOn — same combo OmenHwCtl uses for max RPM.
                bool modeOk = TryBiosCall(CmdSetFanMode, new byte[] { 0xFF, 0x04 }, "0", out string modeErr);
                if (!modeOk)
                    modeOk = TryBiosCall(CmdSetFanMode, new byte[] { 0xFF, 0x01 }, "0", out modeErr); // Performance fallback

                bool maxOk = TryBiosCall(CmdMaxFanSpeed, new byte[] { 0x01 }, "0", out string maxErr);
                if (!modeOk && !maxOk)
                {
                    detail = $"FullSpeed failed · mode={modeErr} · max={maxErr}";
                    return false;
                }

                detail = maxOk ? "Extreme/Perf + MaxFanSpeedOn" : $"FanMode set · MaxFanSpeed: {maxErr}";
                return true;
            }

            byte mode = profile switch
            {
                OemFanProfileMapper.Profile.Quiet => 0x03,
                OemFanProfileMapper.Profile.Performance => 0x01,
                _ => 0x00 // Default / Eco
            };

            if (!TryBiosCall(CmdSetFanMode, new byte[] { 0xFF, mode }, "0", out string err))
            {
                detail = err;
                return false;
            }

            detail = $"FanMode=0x{mode:X2}";
            return true;
        }

        protected override bool TryGetProfile(out OemFanProfileMapper.Profile profile, out string detail)
        {
            profile = OemFanProfileMapper.Profile.Balanced;
            detail = "assumed Balanced";
            return true;
        }

        private bool TryBiosCall(uint commandType, byte[] data, string outputSize, out string error)
        {
            error = "";
            if (_bios == null)
            {
                error = "not connected";
                return false;
            }

            string method = "hpqBIOSInt" + outputSize;
            try
            {
                var scope = new ManagementScope(Ns);
                scope.Connect();
                using var dataInClass = new ManagementClass(scope, new ManagementPath(DataInClass), null);
                ManagementObject dataIn = dataInClass.CreateInstance()
                    ?? throw new InvalidOperationException("hpqBDataIn CreateInstance failed");

                dataIn["Command"] = DefaultCommand;
                dataIn["CommandType"] = commandType;
                dataIn["Sign"] = Sign;
                dataIn["Size"] = (uint)data.Length;
                dataIn["hpqBData"] = data;

                ManagementBaseObject inParams = _bios.GetMethodParameters(method);
                inParams["InData"] = dataIn;
                ManagementBaseObject outParams = _bios.InvokeMethod(method, inParams, null);

                object? outData = outParams?["OutData"];
                if (outData is ManagementBaseObject embedded)
                {
                    object? code = embedded["rwReturnCode"];
                    if (code != null && Convert.ToUInt32(code) != 0)
                    {
                        error = $"hpqBIOSInt rwReturnCode={code}";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                OcDebugLog.Log($"[FAN] HP BIOS WMI 0x{commandType:X}: {ex.Message}");
                return false;
            }
        }
    }
}
