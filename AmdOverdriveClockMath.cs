namespace FPSOverlay
{
    /// <summary>
    /// ADL OverdriveN stores clocks in 10 kHz units (MHz × 100).
    /// Offsets are always applied to the driver default, never to the last manual value,
    /// so profile changes cannot stack and a zero offset restores the captured baseline.
    /// </summary>
    public static class AmdOverdriveClockMath
    {
        public const int TenKhzPerMhz = 100;

        public static int ApplyOffset(int baselineTenKhz, int offsetMhz)
        {
            long target = (long)baselineTenKhz + (long)offsetMhz * TenKhzPerMhz;
            if (target < 0) target = 0;
            if (target > int.MaxValue) target = int.MaxValue;
            return (int)target;
        }
    }
}
