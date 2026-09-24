using System;
using System.Collections.Generic;

namespace FPSOverlay
{
    /// <summary>
    /// Shared profile-style OEM backend: Fan Curve / Manual PWM% → Quiet/Balanced/Performance/FullSpeed.
    /// RestoreDefaults returns the OEM Balanced (or Auto) profile.
    /// </summary>
    public abstract class OemProfileFanBackendBase : IFanBackend
    {
        private OemFanProfileMapper.Profile? _applied;
        private int _lastReportedPwm = 50;
        private bool _probed;

        public abstract string Name { get; }
        public bool IsAvailable { get; protected set; }
        public string StatusMessage { get; protected set; } = "Not probed";

        protected abstract string ChannelId { get; }
        protected abstract string ChannelName { get; }
        protected abstract FanVendorHint VendorHint { get; }
        protected abstract bool MatchesChassis();
        protected abstract bool TryConnect(out string detail);
        protected abstract bool TrySetProfile(OemFanProfileMapper.Profile profile, out string detail);
        protected abstract bool TryGetProfile(out OemFanProfileMapper.Profile profile, out string detail);
        protected virtual bool TryReadRpm(out float? rpm)
        {
            rpm = null;
            return false;
        }

        protected virtual OemFanProfileMapper.Profile RestoreProfile
            => OemFanProfileMapper.Profile.Balanced;

        public IReadOnlyList<FanChannel> Probe()
        {
            _probed = true;
            var channels = new List<FanChannel>();

            if (!MatchesChassis())
            {
                IsAvailable = false;
                StatusMessage = "Chassis manufacturer mismatch";
                return channels;
            }

            if (!TryConnect(out string detail))
            {
                IsAvailable = false;
                StatusMessage = detail;
                return channels;
            }

            IsAvailable = true;
            StatusMessage = detail;
            channels.Add(new FanChannel
            {
                Id = ChannelId,
                Name = ChannelName,
                Kind = FanKind.Chassis,
                BackendName = Name,
                CanReadRpm = TryReadRpm(out _),
                CanWritePwm = true,
                PreferredTempSource = FanTempSource.MaxCpuGpu,
                MinSafePwm = 15,
                Capabilities = "OEM profile · PWM% → Quiet/Balanced/Performance/FullSpeed",
                VendorHint = VendorHint
            });
            return channels;
        }

        public FanLiveReading? Read(string channelId)
        {
            if (!string.Equals(channelId, ChannelId, StringComparison.OrdinalIgnoreCase))
                return null;

            float? rpm = null;
            TryReadRpm(out rpm);

            // Prefer last commanded PWM so Manual 100% reads back as 100, not a soft "90".
            return new FanLiveReading
            {
                ChannelId = channelId,
                Rpm = rpm,
                PwmPercent = _lastReportedPwm
            };
        }

        public FanApplyResult SetPwm(string channelId, int percent)
        {
            if (!string.Equals(channelId, ChannelId, StringComparison.OrdinalIgnoreCase))
                return FanApplyResult.Fail("Unknown channel");
            if (!_probed && Probe().Count == 0)
                return FanApplyResult.Fail(StatusMessage);

            percent = Math.Clamp(percent, 0, 100);
            var profile = OemFanProfileMapper.FromPwm(percent);
            if (_applied == profile)
            {
                _lastReportedPwm = percent;
                return FanApplyResult.Ok($"hold {OemFanProfileMapper.Label(profile)}", percent);
            }

            if (!TrySetProfile(profile, out string detail))
                return FanApplyResult.Fail(detail);

            _applied = profile;
            _lastReportedPwm = percent;
            OcDebugLog.Log($"[FAN] {Name} · PWM {percent}% → {OemFanProfileMapper.Label(profile)} · {detail}");
            return FanApplyResult.Ok($"{OemFanProfileMapper.Label(profile)} ({detail})", percent);
        }

        public FanApplyResult RestoreDefaults(string? channelId = null)
        {
            if (channelId != null &&
                !string.Equals(channelId, ChannelId, StringComparison.OrdinalIgnoreCase))
                return FanApplyResult.Ok("skipped");

            if (!IsAvailable && Probe().Count == 0)
                return FanApplyResult.Fail(StatusMessage);

            if (!TrySetProfile(RestoreProfile, out string detail))
                return FanApplyResult.Fail(detail);

            _applied = RestoreProfile;
            _lastReportedPwm = OemFanProfileMapper.RepresentativePwm(RestoreProfile);
            return FanApplyResult.Ok($"restored {OemFanProfileMapper.Label(RestoreProfile)} · {detail}", _lastReportedPwm);
        }
    }
}
