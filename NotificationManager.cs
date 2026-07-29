using System;
using Microsoft.Toolkit.Uwp.Notifications;

namespace FPSOverlay
{
    /// <summary>
    /// High-priority Windows toast engine for Smart OC lifecycle alerts.
    /// Always uses ToastScenario.Alarm (+ dismiss action) so Focus Assist / Game Mode
    /// cannot silently suppress start, exit, or fail-closed notifications.
    /// Queries SHQueryUserNotificationState for diagnostics only.
    /// Stateless and leak-free: no cached COM objects or event subscriptions.
    /// </summary>
    public sealed class NotificationManager
    {
        private const string DefaultToastTag = "mars-smart-oc";
        private const string DefaultToastGroup = "mars-smart-oc";

        public void ShowHighPriority(string title, string body, string? tag = null, string? group = null)
        {
            try
            {
                int state = Win32Api.GetUserNotificationState();
                bool suppressed = Win32Api.IsNotificationSuppressedState(state);
                string toastTag = string.IsNullOrWhiteSpace(tag) ? DefaultToastTag : tag;
                string toastGroup = string.IsNullOrWhiteSpace(group) ? DefaultToastGroup : group;
                OcDebugLog.Write($"toast Alarm · QUNS={state} suppressedHint={suppressed} tag={toastTag}");

                // Alarm + at least one button is required for Focus Assist bypass.
                // Non-looping default cue avoids a looping alarm siren in-game.
                new ToastContentBuilder()
                    .AddText(string.IsNullOrWhiteSpace(title) ? "Mars Smart Overclock Engine" : title)
                    .AddText(body ?? "")
                    .SetToastScenario(ToastScenario.Alarm)
                    .AddAudio(new ToastAudio
                    {
                        Src = new Uri("ms-winsoundevent:Notification.Default"),
                        Loop = false
                    })
                    .AddButton(new ToastButton()
                        .SetContent("OK")
                        .SetDismissActivation())
                    .Show(toast =>
                    {
                        toast.Tag = toastTag;
                        toast.Group = toastGroup;
                        toast.ExpirationTime = DateTimeOffset.Now.AddSeconds(12);
                    });
            }
            catch (Exception ex)
            {
                OcDebugLog.Write($"toast failed: {ex.Message}");
            }
        }
    }
}
