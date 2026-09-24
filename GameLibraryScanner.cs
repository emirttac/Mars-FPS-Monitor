using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Win32;

namespace FPSOverlay
{
    /// <summary>
    /// Scans Steam / Epic / GOG / EA / Ubisoft installs into GameItem entries.
    /// Runs off the UI thread via ScanAsync.
    /// </summary>
    public sealed class GameLibraryScanner
    {
        private static readonly HashSet<string> SteamSkipAppIds = new(StringComparer.Ordinal)
        {
            "228980", // Steamworks Redistributables
            "250820", // SteamVR
            "480"     // Spacewar (dev)
        };

        public string? SteamGridDbApiKey { get; set; }

        public Task<List<GameItem>> ScanAsync(CancellationToken ct = default)
            => ScanAsync(enrichCovers: true, ct);

        public async Task<List<GameItem>> ScanAsync(bool enrichCovers, CancellationToken ct = default)
        {
            var results = await Task.Run(() => Scan(ct), ct).ConfigureAwait(false);
            if (enrichCovers)
            {
                // Epic / GOG / EA / Ubisoft / etc. without CoverUrl → Steam Store / SGDB
                await GameCoverScraper.EnrichMissingCoversAsync(
                    results, SteamGridDbApiKey, ct).ConfigureAwait(false);
            }
            return results;
        }

        public List<GameItem> Scan(CancellationToken ct = default)
        {
            var results = new List<GameItem>();
            try { results.AddRange(ScanSteam(ct)); } catch { }
            try { results.AddRange(ScanEpic(ct)); } catch { }
            try { results.AddRange(ScanGog(ct)); } catch { }
            try { results.AddRange(ScanEa(ct)); } catch { }
            try { results.AddRange(ScanUbisoft(ct)); } catch { }

            return results
                .Where(g => !string.IsNullOrWhiteSpace(g.Title))
                .GroupBy(g => DedupKey(g), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static GameItem CreateCustomFromExe(string exePath)
        {
            string full = Path.GetFullPath(exePath);
            string title = Path.GetFileNameWithoutExtension(full);
            return new GameItem
            {
                Id = "custom_" + Guid.NewGuid().ToString("N"),
                Title = title,
                ExePath = full,
                InstallDir = Path.GetDirectoryName(full),
                PlatformSource = GamePlatform.Custom,
                IsCustom = true
            };
        }

        // ───────── Steam ─────────

        private static IEnumerable<GameItem> ScanSteam(CancellationToken ct)
        {
            string? steamPath = ReadSteamPath();
            if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
                yield break;

            var libraryRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamPath };
            string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (var path in ParseLibraryFoldersVdf(File.ReadAllText(vdf)))
                {
                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                        libraryRoots.Add(path.Replace('/', '\\').TrimEnd('\\'));
                }
            }

            foreach (string root in libraryRoots)
            {
                ct.ThrowIfCancellationRequested();
                string apps = Path.Combine(root, "steamapps");
                if (!Directory.Exists(apps)) continue;

                foreach (string manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    ct.ThrowIfCancellationRequested();
                    GameItem? game = ParseSteamManifest(manifest, apps);
                    if (game != null)
                        yield return game;
                }
            }
        }

        private static string? ReadSteamPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                string? path = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrWhiteSpace(path))
                    return path.Replace('/', '\\');
            }
            catch { }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                    ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
                string? path = key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(path))
                    return path.Replace('/', '\\');
            }
            catch { }

            return null;
        }

        private static IEnumerable<string> ParseLibraryFoldersVdf(string text)
        {
            // libraryfolders.vdf: "path" "D:\\SteamLibrary"
            foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
            {
                string p = m.Groups[1].Value.Replace(@"\\", @"\");
                if (!string.IsNullOrWhiteSpace(p))
                    yield return p;
            }
        }

        private static GameItem? ParseSteamManifest(string manifestPath, string steamAppsDir)
        {
            try
            {
                string text = File.ReadAllText(manifestPath);
                string? appId = ReadVdfValue(text, "appid");
                string? name = ReadVdfValue(text, "name");
                string? installDir = ReadVdfValue(text, "installdir");
                if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name))
                    return null;
                if (SteamSkipAppIds.Contains(appId))
                    return null;

                string? common = Path.Combine(steamAppsDir, "common", installDir ?? "");
                string? exe = FindLikelyExe(common);

                return new GameItem
                {
                    Id = "steam_" + appId,
                    Title = name.Trim(),
                    PlatformAppId = appId,
                    PlatformSource = GamePlatform.Steam,
                    InstallDir = Directory.Exists(common) ? common : null,
                    ExePath = exe,
                    LaunchUri = $"steam://rungameid/{appId}",
                    CoverUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
                    IsCustom = false
                };
            }
            catch
            {
                return null;
            }
        }

        private static string? ReadVdfValue(string text, string key)
        {
            var m = Regex.Match(text, $"\"{Regex.Escape(key)}\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        // ───────── Epic ─────────

        private static IEnumerable<GameItem> ScanEpic(CancellationToken ct)
        {
            string manifests = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(manifests))
                yield break;

            foreach (string file in Directory.EnumerateFiles(manifests, "*.item"))
            {
                ct.ThrowIfCancellationRequested();
                GameItem? game = ParseEpicItem(file);
                if (game != null)
                    yield return game;
            }
        }

        private static GameItem? ParseEpicItem(string path)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                string? display = GetJsonString(root, "DisplayName");
                string? install = GetJsonString(root, "InstallLocation");
                string? launchExe = GetJsonString(root, "LaunchExecutable");
                string? appName = GetJsonString(root, "AppName");
                string? catalogNs = GetJsonString(root, "CatalogNamespace");
                string? catalogItemId = GetJsonString(root, "CatalogItemId");
                bool? isApp = GetJsonBool(root, "bIsApplication");
                if (isApp == false) return null;
                if (string.IsNullOrWhiteSpace(display)) return null;

                // Skip launcher / helpers
                if (display.Contains("Epic Games Launcher", StringComparison.OrdinalIgnoreCase))
                    return null;

                string? exe = null;
                if (!string.IsNullOrWhiteSpace(install) && !string.IsNullOrWhiteSpace(launchExe))
                {
                    string candidate = Path.Combine(install, launchExe.Replace('/', '\\'));
                    if (File.Exists(candidate)) exe = candidate;
                }

                string? uri = null;
                if (!string.IsNullOrWhiteSpace(catalogNs) &&
                    !string.IsNullOrWhiteSpace(catalogItemId) &&
                    !string.IsNullOrWhiteSpace(appName))
                {
                    uri = $"com.epicgames.launcher://apps/{Uri.EscapeDataString(catalogNs)}%3A{Uri.EscapeDataString(catalogItemId)}%3A{Uri.EscapeDataString(appName)}?action=launch";
                }

                if (string.IsNullOrWhiteSpace(exe) && string.IsNullOrWhiteSpace(uri))
                    return null;

                return new GameItem
                {
                    Id = "epic_" + (appName ?? Guid.NewGuid().ToString("N")),
                    Title = display.Trim(),
                    PlatformAppId = appName,
                    PlatformSource = GamePlatform.Epic,
                    InstallDir = install,
                    ExePath = exe,
                    LaunchUri = uri,
                    IsCustom = false
                };
            }
            catch
            {
                return null;
            }
        }

        // ───────── GOG ─────────

        private static IEnumerable<GameItem> ScanGog(CancellationToken ct)
        {
            string[] roots =
            {
                @"SOFTWARE\WOW6432Node\GOG.com\Games",
                @"SOFTWARE\GOG.com\Games"
            };

            foreach (string root in roots)
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(root);
                if (baseKey == null) continue;

                foreach (string sub in baseKey.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    using var gameKey = baseKey.OpenSubKey(sub);
                    if (gameKey == null) continue;

                    string? name = gameKey.GetValue("gameName") as string
                        ?? gameKey.GetValue("GAMENAME") as string;
                    string? path = gameKey.GetValue("path") as string
                        ?? gameKey.GetValue("PATH") as string;
                    string? exe = gameKey.GetValue("exe") as string
                        ?? gameKey.GetValue("EXE") as string
                        ?? gameKey.GetValue("launchCommand") as string;

                    if (string.IsNullOrWhiteSpace(name)) continue;

                    string? exeFull = null;
                    if (!string.IsNullOrWhiteSpace(exe))
                    {
                        if (Path.IsPathRooted(exe) && File.Exists(exe))
                            exeFull = exe;
                        else if (!string.IsNullOrWhiteSpace(path))
                        {
                            string c = Path.Combine(path, Path.GetFileName(exe));
                            if (File.Exists(c)) exeFull = c;
                            else if (File.Exists(Path.Combine(path, exe)))
                                exeFull = Path.Combine(path, exe);
                        }
                    }

                    if (exeFull == null && !string.IsNullOrWhiteSpace(path))
                        exeFull = FindLikelyExe(path);

                    if (exeFull == null) continue;

                    yield return new GameItem
                    {
                        Id = "gog_" + sub,
                        Title = name.Trim(),
                        PlatformAppId = sub,
                        PlatformSource = GamePlatform.Gog,
                        InstallDir = path,
                        ExePath = exeFull,
                        IsCustom = false
                    };
                }
            }
        }

        // ───────── EA ─────────

        private static IEnumerable<GameItem> ScanEa(CancellationToken ct)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var game in ScanEaManifests(ct))
            {
                if (seen.Add(DedupKey(game)))
                    yield return game;
            }

            foreach (var game in ScanEaRegistry(ct))
            {
                if (seen.Add(DedupKey(game)))
                    yield return game;
            }
        }

        private static IEnumerable<GameItem> ScanEaManifests(CancellationToken ct)
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string[] dirs =
            {
                Path.Combine(programData, "EA Desktop", "Manifests"),
                Path.Combine(programData, "Electronic Arts", "EA Desktop", "Manifests")
            };

            foreach (string dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.EnumerateFiles(dir, "*.xml"))
                {
                    ct.ThrowIfCancellationRequested();
                    GameItem? g = ParseEaXml(file);
                    if (g != null) yield return g;
                }
            }
        }

        private static GameItem? ParseEaXml(string path)
        {
            try
            {
                var doc = XDocument.Load(path);
                string? title =
                    doc.Descendants().FirstOrDefault(x => x.Name.LocalName is "DisplayName" or "Title" or "GameTitle")?.Value
                    ?? doc.Root?.Attribute("title")?.Value
                    ?? doc.Root?.Attribute("name")?.Value;

                string? install =
                    doc.Descendants().FirstOrDefault(x => x.Name.LocalName is "InstallDir" or "InstallPath" or "ContentPath")?.Value;

                string? exeRel =
                    doc.Descendants().FirstOrDefault(x => x.Name.LocalName is "Executable" or "LaunchExecutable" or "FileName")?.Value;

                if (string.IsNullOrWhiteSpace(title)) return null;

                string? exe = null;
                if (!string.IsNullOrWhiteSpace(install) && !string.IsNullOrWhiteSpace(exeRel))
                {
                    string c = Path.Combine(install, exeRel.Replace('/', '\\'));
                    if (File.Exists(c)) exe = c;
                }
                if (exe == null && !string.IsNullOrWhiteSpace(install))
                    exe = FindLikelyExe(install);
                if (exe == null) return null;

                string idSeed = Path.GetFileNameWithoutExtension(path);
                return new GameItem
                {
                    Id = "ea_" + idSeed,
                    Title = title.Trim(),
                    PlatformAppId = idSeed,
                    PlatformSource = GamePlatform.Ea,
                    InstallDir = install,
                    ExePath = exe,
                    IsCustom = false
                };
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<GameItem> ScanEaRegistry(CancellationToken ct)
        {
            string[] roots =
            {
                @"SOFTWARE\WOW6432Node\EA Games",
                @"SOFTWARE\EA Games",
                @"SOFTWARE\WOW6432Node\Electronic Arts",
                @"SOFTWARE\Electronic Arts"
            };

            foreach (string root in roots)
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(root);
                if (baseKey == null) continue;

                foreach (string sub in baseKey.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    using var gameKey = baseKey.OpenSubKey(sub);
                    if (gameKey == null) continue;

                    string name = (gameKey.GetValue("DisplayName") as string)
                        ?? (gameKey.GetValue("Product Name") as string)
                        ?? sub;
                    string? install = gameKey.GetValue("Install Dir") as string
                        ?? gameKey.GetValue("InstallDir") as string
                        ?? gameKey.GetValue("Install Path") as string;
                    if (string.IsNullOrWhiteSpace(install) || !Directory.Exists(install))
                        continue;

                    string? exe = FindLikelyExe(install);
                    if (exe == null) continue;

                    yield return new GameItem
                    {
                        Id = "ea_reg_" + sub.GetHashCode().ToString("X"),
                        Title = name.Trim(),
                        PlatformSource = GamePlatform.Ea,
                        InstallDir = install,
                        ExePath = exe,
                        IsCustom = false
                    };
                }
            }
        }

        // ───────── Ubisoft ─────────

        private static IEnumerable<GameItem> ScanUbisoft(CancellationToken ct)
        {
            string[] roots =
            {
                @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs",
                @"SOFTWARE\Ubisoft\Launcher\Installs"
            };

            foreach (string root in roots)
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(root);
                if (baseKey == null) continue;

                foreach (string sub in baseKey.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    using var gameKey = baseKey.OpenSubKey(sub);
                    if (gameKey == null) continue;

                    string? install = gameKey.GetValue("InstallDir") as string
                        ?? gameKey.GetValue("InstallPath") as string;
                    if (string.IsNullOrWhiteSpace(install) || !Directory.Exists(install))
                        continue;

                    string title = Path.GetFileName(install.TrimEnd('\\', '/')) ?? sub;
                    string? exe = FindLikelyExe(install);
                    if (exe == null) continue;

                    yield return new GameItem
                    {
                        Id = "ubi_" + sub,
                        Title = title,
                        PlatformAppId = sub,
                        PlatformSource = GamePlatform.Ubisoft,
                        InstallDir = install,
                        ExePath = exe,
                        LaunchUri = $"uplay://launch/{sub}/0",
                        IsCustom = false
                    };
                }
            }
        }

        // ───────── helpers ─────────

        private static string? FindLikelyExe(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return null;

            try
            {
                var exes = Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                    .Where(f => !IsHelperExe(Path.GetFileName(f)))
                    .ToList();

                if (exes.Count == 0)
                {
                    // one level deeper (common for Ubisoft / EA)
                    exes = Directory.EnumerateDirectories(dir)
                        .Take(8)
                        .SelectMany(d =>
                        {
                            try { return Directory.EnumerateFiles(d, "*.exe", SearchOption.TopDirectoryOnly); }
                            catch { return Enumerable.Empty<string>(); }
                        })
                        .Where(f => !IsHelperExe(Path.GetFileName(f)))
                        .ToList();
                }

                if (exes.Count == 0) return null;

                string folderName = Path.GetFileName(dir.TrimEnd('\\', '/')) ?? "";
                var preferred = exes.FirstOrDefault(e =>
                    Path.GetFileNameWithoutExtension(e).Contains(folderName, StringComparison.OrdinalIgnoreCase)
                    || folderName.Contains(Path.GetFileNameWithoutExtension(e), StringComparison.OrdinalIgnoreCase));
                if (preferred != null) return preferred;

                return exes
                    .OrderByDescending(f =>
                    {
                        try { return new FileInfo(f).Length; } catch { return 0L; }
                    })
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsHelperExe(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string n = name.ToLowerInvariant();
            return n.Contains("unins") || n.Contains("setup") || n.Contains("redist")
                || n.Contains("crash") || n.Contains("launcher") && n.Contains("unity")
                || n is "vcredist_x64.exe" or "vcredist_x86.exe" or "dotnet.exe"
                || n.StartsWith("unitycrash") || n.Contains("easyanticheat")
                || n.Contains("battleye");
        }

        private static string? GetJsonString(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
            return null;
        }

        private static bool? GetJsonBool(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var p)) return null;
            return p.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        private static string DedupKey(GameItem g)
        {
            if (!string.IsNullOrWhiteSpace(g.PlatformAppId))
                return g.PlatformSource + ":" + g.PlatformAppId;
            if (!string.IsNullOrWhiteSpace(g.ExePath))
                return "exe:" + g.ExePath;
            if (!string.IsNullOrWhiteSpace(g.LaunchUri))
                return "uri:" + g.LaunchUri;
            return "id:" + g.Id;
        }
    }
}
