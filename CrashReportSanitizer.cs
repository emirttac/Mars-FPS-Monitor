using System;
using System.Text.RegularExpressions;

namespace FPSOverlay
{
    /// <summary>Strips local paths and secrets from text that may be pasted into a public issue.</summary>
    public static class CrashReportSanitizer
    {
        public static string Sanitize(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            string cleaned = text.Replace("\r\n", "\n");
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile))
                cleaned = cleaned.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

            cleaned = Regex.Replace(cleaned, @"[A-Za-z]:\\Users\\[^\\\s""']+", "%USERPROFILE%", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"/Users/[^/\s""']+", "%USERPROFILE%");
            cleaned = Regex.Replace(cleaned, @"dpapi:[A-Za-z0-9+/=]+", "dpapi:[redacted]", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"Bearer\s+\S+", "Bearer [redacted]", RegexOptions.IgnoreCase);
            return cleaned;
        }
    }
}
