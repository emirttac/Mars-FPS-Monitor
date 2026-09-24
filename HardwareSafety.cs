namespace FPSOverlay
{
    /// <summary>Crash / exit restore hook so GPU clocks return to driver defaults even if the UI is dead.</summary>
    public static class OcSafetyHook
    {
        public static Action? Restore;
    }

    /// <summary>Best-effort hardware release used by the crash path. GPU clocks first, then fans.</summary>
    public static class HardwareSafety
    {
        public static void RestoreAfterFault()
        {
            try
            {
                OcSafetyHook.Restore?.Invoke();
            }
            catch (Exception ex)
            {
                try { OcDebugLog.LogError(OcLogCategory.Oc, "fault OC restore failed", ex); } catch { }
            }

            try
            {
                FanSafetyHook.Restore?.Invoke();
            }
            catch (Exception ex)
            {
                try { OcDebugLog.LogError(OcLogCategory.Fan, "fault fan restore failed", ex); } catch { }
            }
        }
    }
}
