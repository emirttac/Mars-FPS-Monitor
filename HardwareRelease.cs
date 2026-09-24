using System.Threading;

namespace FPSOverlay
{
    /// <summary>
    /// One-shot hardware release shared by tray exit, session end, process exit, and crashes.
    /// A second call does not write the GPU or fans again.
    /// </summary>
    public static class HardwareRelease
    {
        private static int _released;

        public static bool IsReleased => Volatile.Read(ref _released) == 1;

        public static void ReleaseOnce()
        {
            if (Interlocked.Exchange(ref _released, 1) == 1)
                return;
            HardwareSafety.RestoreAfterFault();
        }
    }
}
