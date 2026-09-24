using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FPSOverlay
{
    public sealed class GameLibraryStatsDocument
    {
        [JsonPropertyName("games")]
        public Dictionary<string, GameLifetimeStats> Games { get; set; } = new();

        [JsonPropertyName("open_session")]
        public OpenGameSession? OpenSession { get; set; }
    }

    /// <summary>
    /// library_stats.json beside the executable. Separate from library_cache.json so a rescan cannot wipe totals.
    /// </summary>
    public sealed class GameLibraryStatsStore
    {
        private readonly string _path;
        private readonly Dictionary<string, GameLifetimeStats> _games = new(StringComparer.OrdinalIgnoreCase);

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public GameLibraryStatsStore(string? path = null)
        {
            _path = path ?? AppPaths.LibraryStatsPath;
            Load();
        }

        public GameLifetimeStats? TryGet(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;
            return _games.TryGetValue(key, out var stats) ? stats : null;
        }

        public void Commit(GameLifetimeStats stats)
        {
            if (string.IsNullOrWhiteSpace(stats.Key))
                return;
            _games[stats.Key] = stats;
            Save(null);
        }

        public void WriteOpen(OpenGameSession session)
        {
            if (string.IsNullOrWhiteSpace(session.Key) || session.ElapsedSeconds <= 0)
                return;
            Save(session);
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return;

                var doc = JsonSerializer.Deserialize<GameLibraryStatsDocument>(File.ReadAllText(_path), JsonOpts);
                if (doc?.Games != null)
                {
                    foreach (var pair in doc.Games)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                            continue;
                        pair.Value.Key = pair.Key;
                        _games[pair.Key] = pair.Value;
                    }
                }

                var open = doc?.OpenSession;
                if (open == null || string.IsNullOrWhiteSpace(open.Key) || open.ElapsedSeconds <= 0)
                    return;

                var recovered = GameSessionAccumulator.FromSnapshot(open);
                _games.TryGetValue(open.Key, out var prior);
                _games[open.Key] = GameSessionAccumulator.Merge(prior, recovered, DateTime.UtcNow, open.Key);
                Save(null);
            }
            catch (Exception ex)
            {
                OcDebugLog.Write("library stats load: " + ex.Message);
            }
        }

        private void Save(OpenGameSession? openSession)
        {
            try
            {
                var doc = new GameLibraryStatsDocument
                {
                    Games = new Dictionary<string, GameLifetimeStats>(_games, StringComparer.OrdinalIgnoreCase),
                    OpenSession = openSession
                };

                string directory = Path.GetDirectoryName(_path) ?? AppDomain.CurrentDomain.BaseDirectory;
                Directory.CreateDirectory(directory);
                string temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(doc, JsonOpts));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                OcDebugLog.Write("library stats save: " + ex.Message);
            }
        }
    }
}
