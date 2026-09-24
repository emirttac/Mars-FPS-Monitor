using System.Collections.Generic;

namespace FPSOverlay
{
    public interface IFanBackend
    {
        string Name { get; }
        bool IsAvailable { get; }
        string StatusMessage { get; }

        IReadOnlyList<FanChannel> Probe();
        FanLiveReading? Read(string channelId);
        FanApplyResult SetPwm(string channelId, int percent);
        FanApplyResult RestoreDefaults(string? channelId = null);
    }
}
