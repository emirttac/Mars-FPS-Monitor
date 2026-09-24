using System;

namespace FPSOverlay
{
    /// <summary>
    /// Allowed Smart OC toast events. Thermal band, profile, and safety-stock hops stay silent.
    /// </summary>
    public enum OcNotificationEvent
    {
        GameStarted,
        GameExited,
        FailClosed
    }

    /// <summary>
    /// Auto OC toasts only when a game turns Smart OC on, and when the game ends and Auto returns to idle.
    /// Hotspot stock holds, sensor faults, and temperature-band switches do not toast.
    /// </summary>
    public sealed class NotificationService
    {
        private readonly NotificationManager _manager;

        public NotificationService(NotificationManager? manager = null)
        {
            _manager = manager ?? new NotificationManager();
        }

        public void OnGameStarted(string language, string gameExeName)
            => Emit(OcNotificationEvent.GameStarted, language, gameExeName);

        public void OnGameExited(string language)
            => Emit(OcNotificationEvent.GameExited, language, detail: null);

        public void OnFailClosed(string language, string reason)
        {
            _ = language;
            OcDebugLog.Write($"toast suppressed (safety stock): {reason}");
        }

        /// <summary>
        /// Single gate for all OC toasts. Unknown / disallowed events are dropped silently.
        /// </summary>
        public void Emit(OcNotificationEvent kind, string language, string? detail)
        {
            switch (kind)
            {
                case OcNotificationEvent.GameStarted:
                case OcNotificationEvent.GameExited:
                    break;
                default:
                    OcDebugLog.Write($"toast blocked (policy): {kind}");
                    return;
            }

            var s = UiStrings.For(language);
            string title = string.IsNullOrWhiteSpace(s.ToastOcTitle)
                ? "Mars Smart Overclock Engine"
                : s.ToastOcTitle;

            string body = kind switch
            {
                OcNotificationEvent.GameStarted =>
                    string.Format(s.ToastGameActive, NormalizeExe(detail)),
                OcNotificationEvent.GameExited =>
                    s.ToastGameInactive,
                _ => null!
            };

            if (string.IsNullOrWhiteSpace(body)) return;
            _manager.ShowHighPriority(title, body);
        }

        private static string NormalizeExe(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "game.exe";
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
        }
    }
}
