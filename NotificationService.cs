using System;

namespace FPSOverlay
{
    /// <summary>
    /// Allowed Smart OC toast events. Thermal band / profile hops are intentionally absent.
    /// </summary>
    public enum OcNotificationEvent
    {
        GameStarted,
        GameExited,
        FailClosed
    }

    /// <summary>
    /// Strict notification filter for Auto OC.
    /// Only OnGameStarted, OnGameExited, and Fail-Closed may emit UI toasts.
    /// Dynamic temperature-band profile switches stay silent (offsets still apply in OC manager).
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
            => Emit(OcNotificationEvent.FailClosed, language, reason);

        /// <summary>
        /// Single gate for all OC toasts. Unknown / disallowed events are dropped silently.
        /// </summary>
        public void Emit(OcNotificationEvent kind, string language, string? detail)
        {
            switch (kind)
            {
                case OcNotificationEvent.GameStarted:
                case OcNotificationEvent.GameExited:
                case OcNotificationEvent.FailClosed:
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
                OcNotificationEvent.FailClosed =>
                    string.IsNullOrWhiteSpace(detail)
                        ? s.ToastFailClosed
                        : string.Format(s.ToastFailClosedDetail, detail),
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
