using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FPSOverlay
{
    /// <summary>
    /// Catches unexpected crashes and offers a one-click GitHub issue report (English UI).
    /// </summary>
    public static class CrashReporter
    {
        private static int _handling;

        public static void Register(System.Windows.Application app)
        {
            app.DispatcherUnhandledException += OnDispatcherUnhandled;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
            TaskScheduler.UnobservedTaskException += OnUnobservedTask;
        }

        private static void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                OcDebugLog.LogError(OcLogCategory.Crash, "UnobservedTaskException", e.Exception);
            }
            catch { /* never throw from logger path */ }
            e.SetObserved();
        }

        private static void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            OfferReportAndExit(e.Exception, "UI thread");
        }

        private static void OnDomainUnhandled(object? sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception
                     ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown fatal error");
            OfferReportAndExit(ex, "AppDomain");
        }

        /// <summary>Also usable from caught fatal startup failures.</summary>
        public static void OfferReportAndExit(Exception ex, string source)
        {
            if (Interlocked.Exchange(ref _handling, 1) == 1)
                return;

            try { OcDebugLog.Log(OcLogCategory.Crash, $"CRASH [{source}]: {ex}"); } catch { }
            HardwareRelease.ReleaseOnce();

            string shortMsg = Truncate(ex.GetBaseException().Message, 220);
            string dialog =
                "Mars FPS Monitor ran into an unexpected error and needs to close.\n\n" +
                $"{ex.GetBaseException().GetType().Name}: {shortMsg}\n\n" +
                "Would you like to send a one-click error report on GitHub?\n" +
                "Your browser will open a pre-filled issue so you can submit it in a single click.";

            MessageBoxResult answer = MessageBoxResult.No;
            try
            {
                answer = System.Windows.MessageBox.Show(
                    dialog,
                    "Mars FPS Monitor — Unexpected Error",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error,
                    MessageBoxResult.Yes);
            }
            catch
            {
                // MessageBox may fail if WPF is already dead — still try GitHub? skip
            }

            if (answer == MessageBoxResult.Yes)
                TryOpenGitHubIssue(ex, source);

            try { System.Windows.Application.Current?.Shutdown(1); } catch { }
            Environment.Exit(1);
        }

        public static void TryOpenGitHubIssue(Exception ex, string source)
        {
            try
            {
                var root = ex.GetBaseException();
                string title = $"[Crash] {AppInfo.ProductName} {AppInfo.Version} — {root.GetType().Name}";
                string body = BuildIssueBody(ex, source);

                // Keep URL under practical browser/GitHub limits
                body = Truncate(CrashReportSanitizer.Sanitize(body), 1200);

                string url =
                    $"{AppInfo.GitHubRepoUrl}/issues/new" +
                    "?title=" + Uri.EscapeDataString(title) +
                    "&body=" + Uri.EscapeDataString(body);

                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception openEx)
            {
                try { OcDebugLog.Write("CrashReporter open GitHub failed: " + openEx.Message); } catch { }
                try
                {
                    System.Windows.MessageBox.Show(
                        "Could not open the browser. Please report the crash manually at:\n" +
                        AppInfo.GitHubRepoUrl + "/issues",
                        "Mars FPS Monitor",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                catch { }
            }
        }

        private static string BuildIssueBody(Exception ex, string source)
        {
            var root = ex.GetBaseException();
            var sb = new StringBuilder();
            sb.AppendLine("### Crash report (auto-generated)");
            sb.AppendLine();
            sb.AppendLine("Mars FPS Monitor closed unexpectedly. Please submit this issue — add any extra steps if you can.");
            sb.AppendLine();
            sb.AppendLine("### Environment");
            sb.AppendLine($"- **App:** {AppInfo.ProductName} {AppInfo.Version}");
            sb.AppendLine($"- **OS:** {Environment.OSVersion}");
            sb.AppendLine($"- **64-bit OS:** {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"- **64-bit process:** {Environment.Is64BitProcess}");
            sb.AppendLine($"- **.NET:** {Environment.Version}");
            sb.AppendLine($"- **Source:** {source}");
            sb.AppendLine($"- **Time (UTC):** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
            sb.AppendLine();
            sb.AppendLine("### Exception");
            sb.AppendLine("```");
            sb.AppendLine(CrashReportSanitizer.Sanitize($"{root.GetType().FullName}: {Truncate(root.Message, 800)}"));
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("### Stack trace");
            sb.AppendLine("```");
            sb.AppendLine(CrashReportSanitizer.Sanitize(Truncate(root.StackTrace ?? "(no stack trace)", 1800)));
            sb.AppendLine("```");
            if (!ReferenceEquals(ex, root) && ex.StackTrace != null)
            {
                sb.AppendLine();
                sb.AppendLine("### Outer exception");
                sb.AppendLine("```");
                sb.AppendLine(CrashReportSanitizer.Sanitize(Truncate(ex.ToString(), 800)));
                sb.AppendLine("```");
            }
            sb.AppendLine();
            sb.AppendLine("### Steps to reproduce");
            sb.AppendLine("_Optional: what were you doing when it crashed?_");
            return sb.ToString();
        }

        private static string Truncate(string? text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r\n", "\n");
            return text.Length <= max ? text : text.Substring(0, max) + "\n…(truncated)";
        }
    }
}
