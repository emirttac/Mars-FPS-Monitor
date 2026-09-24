using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FPSOverlay
{
    public sealed class GameLibraryCacheDocument
    {
        [JsonPropertyName("scanned_at_utc")]
        public DateTime? ScannedAtUtc { get; set; }

        [JsonPropertyName("games")]
        public List<GameItem> Games { get; set; } = new();

        [JsonPropertyName("custom_games")]
        public List<GameItem> CustomGames { get; set; } = new();
    }

    /// <summary>Persist scanned + custom library entries to library_cache.json.</summary>
    public sealed class GameLibraryStore
    {
        private readonly string _path;
        private readonly object _sync = new();
        private List<GameItem> _games = new();
        private List<GameItem> _custom = new();
        private DateTime? _scannedAtUtc;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public GameLibraryStore(string? path = null)
        {
            _path = path ?? AppPaths.LibraryCachePath;
            Load();
        }

        public DateTime? ScannedAtUtc
        {
            get { lock (_sync) return _scannedAtUtc; }
        }

        public IReadOnlyList<GameItem> GetAll()
        {
            lock (_sync)
                return MergeLists(_games, _custom).Select(g => g.Clone()).ToList();
        }

        public IReadOnlyList<GameItem> GetCustom()
        {
            lock (_sync)
                return _custom.Select(g => g.Clone()).ToList();
        }

        public void ReplaceScanned(IEnumerable<GameItem> scanned, DateTime utcNow)
        {
            lock (_sync)
            {
                // Keep previously resolved covers across re-scans.
                var prevByKey = _games
                    .Concat(_custom)
                    .GroupBy(DedupKey, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                _games = scanned
                    .Where(g => !g.IsCustom)
                    .Select(g =>
                    {
                        var clone = g.Clone();
                        if (prevByKey.TryGetValue(DedupKey(clone), out var prev))
                        {
                            if (string.IsNullOrWhiteSpace(clone.CoverUrl) && !string.IsNullOrWhiteSpace(prev.CoverUrl))
                                clone.CoverUrl = prev.CoverUrl;
                            if (prev.CoverLookupDone)
                                clone.CoverLookupDone = true;
                        }
                        return clone;
                    })
                    .GroupBy(g => DedupKey(g), StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                _scannedAtUtc = utcNow;
                SaveLocked();
            }
        }

        /// <summary>Persist a resolved cover URL (or failed lookup) for scanned/custom entry.</summary>
        public void UpdateCover(string id, string? coverUrl, bool lookupDone)
        {
            lock (_sync)
            {
                var item = _games.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase))
                    ?? _custom.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
                if (item == null) return;

                if (!string.IsNullOrWhiteSpace(coverUrl))
                    item.CoverUrl = coverUrl;
                item.CoverLookupDone = lookupDone;
                SaveLocked();
            }
        }

        public void AddCustom(GameItem item)
        {
            lock (_sync)
            {
                item.IsCustom = true;
                item.PlatformSource = GamePlatform.Custom;
                if (string.IsNullOrWhiteSpace(item.Id))
                    item.Id = "custom_" + Guid.NewGuid().ToString("N");

                string key = DedupKey(item);
                _custom.RemoveAll(g => string.Equals(DedupKey(g), key, StringComparison.OrdinalIgnoreCase));
                _custom.Add(item.Clone());
                SaveLocked();
            }
        }

        public bool RemoveCustom(string id)
        {
            lock (_sync)
            {
                int n = _custom.RemoveAll(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
                if (n > 0) SaveLocked();
                return n > 0;
            }
        }

        public void Load()
        {
            lock (_sync)
            {
                _games.Clear();
                _custom.Clear();
                _scannedAtUtc = null;
                try
                {
                    if (!File.Exists(_path)) return;
                    string json = File.ReadAllText(_path);
                    var doc = JsonSerializer.Deserialize<GameLibraryCacheDocument>(json, JsonOpts);
                    if (doc == null) return;
                    _scannedAtUtc = doc.ScannedAtUtc;
                    _games = (doc.Games ?? new()).Select(g => g.Clone()).ToList();
                    _custom = (doc.CustomGames ?? new()).Select(g =>
                    {
                        g.IsCustom = true;
                        g.PlatformSource = GamePlatform.Custom;
                        return g.Clone();
                    }).ToList();
                }
                catch { }
            }
        }

        private void SaveLocked()
        {
            try
            {
                var doc = new GameLibraryCacheDocument
                {
                    ScannedAtUtc = _scannedAtUtc,
                    Games = _games.Select(g => g.Clone()).ToList(),
                    CustomGames = _custom.Select(g => g.Clone()).ToList()
                };
                string? directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(_path, JsonSerializer.Serialize(doc, JsonOpts));
            }
            catch { }
        }

        private static IEnumerable<GameItem> MergeLists(List<GameItem> scanned, List<GameItem> custom)
        {
            var map = new Dictionary<string, GameItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in scanned)
                map[DedupKey(g)] = g;
            foreach (var g in custom)
                map[DedupKey(g)] = g; // custom wins on same path
            return map.Values.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase);
        }

        public static string DedupKey(GameItem g)
        {
            if (!string.IsNullOrWhiteSpace(g.LaunchUri))
                return "uri:" + g.LaunchUri;
            if (!string.IsNullOrWhiteSpace(g.ExePath))
                return "exe:" + g.ExePath;
            if (!string.IsNullOrWhiteSpace(g.PlatformAppId))
                return g.PlatformSource + ":" + g.PlatformAppId;
            return "id:" + g.Id;
        }
    }
}
