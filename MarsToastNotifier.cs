using System;

namespace FPSOverlay
{
    /// <summary>
    /// Backward-compatible entry points. Prefer <see cref="NotificationService"/> —
    /// profile-change toasts are intentionally no-ops (silent thermal band switches).
    /// </summary>
    public static class MarsToastNotifier
    {
        private static readonly NotificationService Service = new();

        public static void ShowGameActive(string language, string gameExeName)
            => Service.OnGameStarted(language, gameExeName);

        public static void ShowGameInactive(string language)
            => Service.OnGameExited(language);

        public static void ShowFailClosed(string language, string reason)
        {
            _ = language;
            OcDebugLog.Write($"toast suppressed (safety stock): {reason}");
        }

        /// <summary>Deprecated: dynamic band switches must stay silent during gameplay.</summary>
        public static void ShowProfileChanged(string language, string profileName)
        {
            OcDebugLog.Write($"toast suppressed (band switch): {profileName}");
        }
    }
}
