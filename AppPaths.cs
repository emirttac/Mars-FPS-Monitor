using System;
using System.IO;

namespace FPSOverlay
{
    /// <summary>User data lives under LocalAppData so updates do not wipe settings next to the exe.</summary>
    public static class AppPaths
    {
        public const string FolderName = "Mars FPS Monitor";

        public static readonly string[] MigratedFileNames =
        {
            "config.json",
            "oc_profiles.json",
            "fan_curves.json",
            "library_cache.json",
            "library_stats.json",
            "oc_debug.log",
            "oc_debug.log.1"
        };

        public static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            FolderName);

        public static string ConfigPath => Path.Combine(Root, "config.json");
        public static string OcProfilesPath => Path.Combine(Root, "oc_profiles.json");
        public static string FanCurvesPath => Path.Combine(Root, "fan_curves.json");
        public static string LibraryCachePath => Path.Combine(Root, "library_cache.json");
        public static string LibraryStatsPath => Path.Combine(Root, "library_stats.json");
        public static string DebugLogPath => Path.Combine(Root, "oc_debug.log");

        /// <summary>Written by Setup so the next launch shows release notes once, including a reinstall of the same version.</summary>
        public const string ReleaseNotesFlagName = "show-whats-new";

        public static string ReleaseNotesFlagPath => Path.Combine(Root, ReleaseNotesFlagName);

        public static bool HasPendingReleaseNotes()
        {
            try { return File.Exists(ReleaseNotesFlagPath); }
            catch { return false; }
        }

        public static void ClearPendingReleaseNotes()
        {
            try
            {
                if (File.Exists(ReleaseNotesFlagPath))
                    File.Delete(ReleaseNotesFlagPath);
            }
            catch { /* next launch shows the notes again */ }
        }

        public static void EnsureRoot()
        {
            Directory.CreateDirectory(Root);
        }

        public static void MigrateLegacyFiles(string? legacyDirectory = null, string? destinationRoot = null)
        {
            if (destinationRoot == null)
                EnsureRoot();
            string legacy = legacyDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            string destination = destinationRoot ?? Root;
            foreach (string name in MigratedFileNames)
                TryMigrateFile(Path.Combine(legacy, name), Path.Combine(destination, name));
        }

        /// <summary>
        /// Copy then delete. A failed or short copy leaves the original in place.
        /// An existing destination file is left untouched.
        /// </summary>
        public static bool TryMigrateFile(string legacyPath, string newPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(legacyPath) || string.IsNullOrWhiteSpace(newPath))
                    return false;
                if (File.Exists(newPath) || !File.Exists(legacyPath))
                    return false;

                string? directory = Path.GetDirectoryName(newPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.Copy(legacyPath, newPath, overwrite: false);
                var copied = new FileInfo(newPath);
                var source = new FileInfo(legacyPath);
                if (!copied.Exists || copied.Length != source.Length)
                {
                    try
                    {
                        if (copied.Exists)
                            File.Delete(newPath);
                    }
                    catch { /* keep the source */ }
                    return false;
                }

                File.Delete(legacyPath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
