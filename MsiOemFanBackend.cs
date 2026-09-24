using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace FPSOverlay
{
    /// <summary>
    /// MSI WMI2 platform (Get_Fan / Set_Fan / Get_AP / Set_AP) when present.
    /// Falls back to profile-style control via Set_AP fan-mode flag when full tables are unavailable.
    /// Package layout follows msi-wmi-platform: byte0 = subfeature, following bytes = payload.
    /// </summary>
    public sealed class MsiOemFanBackend : IFanBackend
    {
        private const string Ns = @"root\WMI";
        private ManagementObject? _platform;
        private string _className = "";
        private int _lastPwm = 50;
        private bool _tablesEnabled;

        public string Name => "MSI Platform WMI";
        public bool IsAvailable { get; private set; }
        public string StatusMessage { get; private set; } = "Not probed";

        private const string ChannelId = "oem:msi:system";

        public IReadOnlyList<FanChannel> Probe()
        {
            var channels = new List<FanChannel>();
            _platform?.Dispose();
            _platform = null;
            IsAvailable = false;

            if (!OemLaptopInfo.Is(OemLaptopVendor.Msi))
            {
                StatusMessage = "Chassis manufacturer mismatch";
                return channels;
            }

            _className = FindPlatformClass();
            if (string.IsNullOrEmpty(_className))
            {
                StatusMessage = "MSI WMI platform class not found";
                return channels;
            }

            _platform = WmiFanHelper.FirstInstance(Ns, _className);
            if (_platform == null)
            {
                StatusMessage = $"{_className} instance missing";
                return channels;
            }

            bool canFan = WmiFanHelper.HasMethod(_platform, "Set_Fan") || WmiFanHelper.HasMethod(_platform, "Set_AP");
            if (!canFan)
            {
                StatusMessage = $"{_className} has no Set_Fan / Set_AP";
                return channels;
            }

            IsAvailable = true;
            StatusMessage = $"Ready · {OemLaptopInfo.Model} · {_className}";
            channels.Add(new FanChannel
            {
                Id = ChannelId,
                Name = "MSI System Fans",
                Kind = FanKind.Chassis,
                BackendName = Name,
                CanReadRpm = WmiFanHelper.HasMethod(_platform, "Get_Fan"),
                CanWritePwm = true,
                PreferredTempSource = FanTempSource.MaxCpuGpu,
                MinSafePwm = 20,
                Capabilities = "MSI WMI2 · PWM% → fan tables (100% = flat max table)",
                VendorHint = FanVendorHint.Msi
            });
            return channels;
        }

        public FanLiveReading? Read(string channelId)
        {
            if (!string.Equals(channelId, ChannelId, StringComparison.OrdinalIgnoreCase))
                return null;

            float? rpm = null;
            if (TryPackage("Get_Fan", BuildPkg(0x00), out byte[]? outPkg) && outPkg != null && outPkg.Length > 2)
            {
                // Best-effort: some firmwares return RPM-ish values in early payload bytes.
                int a = outPkg[1];
                int b = outPkg.Length > 2 ? outPkg[2] : 0;
                int guess = a | (b << 8);
                if (guess > 200 && guess < 12000)
                    rpm = guess;
            }

            return new FanLiveReading
            {
                ChannelId = channelId,
                Rpm = rpm,
                PwmPercent = _lastPwm
            };
        }

        public FanApplyResult SetPwm(string channelId, int percent)
        {
            if (!string.Equals(channelId, ChannelId, StringComparison.OrdinalIgnoreCase))
                return FanApplyResult.Fail("Unknown channel");
            if (_platform == null && Probe().Count == 0)
                return FanApplyResult.Fail(StatusMessage);

            percent = Math.Clamp(percent, 0, 100);

            // Prefer real 6-point fan tables when Set_Fan exists — write the requested % (incl. 100).
            if (WmiFanHelper.HasMethod(_platform!, "Set_Fan"))
            {
                if (!_tablesEnabled)
                    TryEnableFanTables(true);

                byte[] cpuTable = BuildFlatTable(0x01, percent);
                byte[] gpuTable = BuildFlatTable(0x02, percent);

                bool okCpu = TryPackage("Set_Fan", cpuTable, out _);
                bool okGpu = TryPackage("Set_Fan", gpuTable, out _);
                if (!okCpu && !okGpu)
                    return FanApplyResult.Fail("Set_Fan failed");

                _lastPwm = percent;
                OcDebugLog.Log($"[FAN] MSI Set_Fan · {percent}%");
                return FanApplyResult.Ok($"fan table {percent}%", percent);
            }

            // Fallback: Quiet/Balanced/Performance/FullSpeed via AP + flat tables when possible.
            var profile = OemFanProfileMapper.FromPwm(percent);
            bool manual = profile != OemFanProfileMapper.Profile.Balanced;
            if (!TryEnableFanTables(manual || profile == OemFanProfileMapper.Profile.FullSpeed))
                return FanApplyResult.Fail("Set_AP fan mode failed");

            int flat = profile == OemFanProfileMapper.Profile.FullSpeed
                ? 100
                : OemFanProfileMapper.RepresentativePwm(profile);

            if (manual && WmiFanHelper.HasMethod(_platform!, "Set_Fan"))
            {
                TryPackage("Set_Fan", BuildFlatTable(0x01, flat), out _);
                TryPackage("Set_Fan", BuildFlatTable(0x02, flat), out _);
            }

            _lastPwm = percent;
            return FanApplyResult.Ok($"{OemFanProfileMapper.Label(profile)}", percent);
        }

        public FanApplyResult RestoreDefaults(string? channelId = null)
        {
            if (channelId != null &&
                !string.Equals(channelId, ChannelId, StringComparison.OrdinalIgnoreCase))
                return FanApplyResult.Ok("skipped");

            if (_platform == null && Probe().Count == 0)
                return FanApplyResult.Fail(StatusMessage);

            if (!TryEnableFanTables(false))
                return FanApplyResult.Fail("restore Set_AP failed");

            _tablesEnabled = false;
            _lastPwm = 50;
            return FanApplyResult.Ok("MSI auto fan mode", 50);
        }

        private bool TryEnableFanTables(bool enable)
        {
            if (!WmiFanHelper.HasMethod(_platform!, "Set_AP"))
            {
                _tablesEnabled = enable;
                return WmiFanHelper.HasMethod(_platform!, "Set_Fan"); // tables-only path
            }

            // Get current AP block if possible, then set bit7 of flags byte.
            byte[] pkg = BuildPkg(0x01);
            if (TryPackage("Get_AP", BuildPkg(0x01), out byte[]? cur) && cur != null && cur.Length >= 3)
                Array.Copy(cur, pkg, Math.Min(cur.Length, pkg.Length));

            pkg[0] = 0x01;
            if (pkg.Length > 1)
            {
                if (enable) pkg[1] = (byte)(pkg[1] | 0x80);
                else pkg[1] = (byte)(pkg[1] & ~0x80);
            }

            bool ok = TryPackage("Set_AP", pkg, out _);
            if (ok) _tablesEnabled = enable;
            return ok;
        }

        private static byte[] BuildFlatTable(byte subfeature, int percent)
        {
            byte p = (byte)Math.Clamp(percent, 0, 100);
            var pkg = new byte[32];
            pkg[0] = subfeature;
            for (int i = 0; i < 6; i++)
                pkg[1 + i] = p;
            return pkg;
        }

        private static byte[] BuildPkg(byte subfeature)
        {
            var pkg = new byte[32];
            pkg[0] = subfeature;
            return pkg;
        }

        private bool TryPackage(string method, byte[] input, out byte[]? output)
        {
            output = null;
            if (_platform == null) return false;

            // Windows MOF typically exposes a single byte[] / object[] named "Data".
            string[] argNames = { "Data", "data", "Package", "package" };
            foreach (var arg in argNames)
            {
                if (!WmiFanHelper.TryInvoke(_platform, method,
                        new Dictionary<string, object> { [arg] = input }, out var outs))
                    continue;

                try
                {
                    object? raw = outs?[arg] ?? outs?["Data"];
                    if (raw is byte[] bytes)
                    {
                        output = bytes;
                        return true;
                    }
                    if (raw is object[] objs)
                    {
                        output = objs.Select(o => Convert.ToByte(o)).ToArray();
                        return true;
                    }
                }
                catch { }

                return true; // invoked successfully even without parseable out buffer
            }
            return false;
        }

        private static string FindPlatformClass()
        {
            // Prefer classes that expose Set_Fan / Get_Fan.
            foreach (var name in WmiFanHelper.ListClassNames(Ns, n =>
                         n.Contains("MSI", StringComparison.OrdinalIgnoreCase) &&
                         (n.Contains("ACPI", StringComparison.OrdinalIgnoreCase) ||
                          n.Contains("Platform", StringComparison.OrdinalIgnoreCase) ||
                          n.Contains("WMI", StringComparison.OrdinalIgnoreCase))))
            {
                try
                {
                    using var mo = WmiFanHelper.FirstInstance(Ns, name);
                    if (mo == null) continue;
                    if (WmiFanHelper.HasMethod(mo, "Set_Fan") ||
                        WmiFanHelper.HasMethod(mo, "Get_Fan") ||
                        WmiFanHelper.HasMethod(mo, "Set_AP"))
                        return name;
                }
                catch { }
            }

            // Known fallback names
            string[] known = { "MSI_ACPI_WMI_BUS", "MSI_ACPI", "msi_acpi_wmi" };
            foreach (var k in known)
            {
                if (WmiFanHelper.ClassExists(Ns, k))
                    return k;
            }
            return "";
        }
    }
}
