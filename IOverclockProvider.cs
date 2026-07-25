namespace FPSOverlay
{
    public interface IGpuOverclockProvider
    {
        string Name { get; }
        string Vendor { get; }
        bool IsAvailable { get; }
        string StatusMessage { get; }
        OverclockApplyResult Apply(OverclockTarget target);
        OverclockApplyResult RestoreDefaults();
    }
}
