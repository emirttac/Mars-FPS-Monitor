using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FPSOverlay
{
    /// <summary>User OC profile matched by GPU core temp band. custom sauce.</summary>
    public sealed class OcProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonPropertyName("profile_name")]
        public string ProfileName { get; set; } = "New Profile";

        [JsonPropertyName("min_temp")]
        public int MinTemp { get; set; }

        [JsonPropertyName("max_temp")]
        public int MaxTemp { get; set; }

        [JsonPropertyName("core_offset_mhz")]
        public int CoreOffsetMhz { get; set; }

        [JsonPropertyName("memory_offset_mhz")]
        public int MemoryOffsetMhz { get; set; }

        /// <summary>Null = leave GPU PL at stock / don't write PL.</summary>
        [JsonPropertyName("power_limit_percent")]
        public int? PowerLimitPercent { get; set; }

        public bool ContainsTemp(float tempC) => tempC >= MinTemp && tempC <= MaxTemp;

        public OverclockTarget ToTarget() => new()
        {
            GpuCoreOffsetMhz = CoreOffsetMhz,
            GpuMemoryOffsetMhz = MemoryOffsetMhz,
            GpuPowerLimitPercent = PowerLimitPercent
        };

        public OcProfile Clone() => new()
        {
            Id = Id,
            ProfileName = ProfileName,
            MinTemp = MinTemp,
            MaxTemp = MaxTemp,
            CoreOffsetMhz = CoreOffsetMhz,
            MemoryOffsetMhz = MemoryOffsetMhz,
            PowerLimitPercent = PowerLimitPercent
        };

        public override string ToString()
            => $"{ProfileName}    {MinTemp}–{MaxTemp}°C    +{CoreOffsetMhz} / +{MemoryOffsetMhz}";
    }

    /// <summary>CRUD + save/load + import/export for OC profiles. the whole pantry.</summary>
    public sealed class OcProfileStore
    {
        private readonly string _path;
        private readonly List<OcProfile> _profiles = new();
        private readonly object _sync = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public OcProfileStore(string? path = null)
        {
            _path = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "oc_profiles.json");
            LoadOrCreateDefaults();
        }

        public IReadOnlyList<OcProfile> Profiles
        {
            get { lock (_sync) return _profiles.Select(p => p.Clone()).ToList(); }
        }

        public event Action? ProfilesChanged;

        public static List<OcProfile> CreateDefaultTemplates() => new()
        {
            new OcProfile
            {
                ProfileName = "Extreme",
                MinTemp = 0,
                MaxTemp = 74,
                CoreOffsetMhz = 50,
                MemoryOffsetMhz = 50,
                PowerLimitPercent = null
            },
            new OcProfile
            {
                ProfileName = "Performance",
                MinTemp = 75,
                MaxTemp = 81,
                CoreOffsetMhz = 25,
                MemoryOffsetMhz = 25,
                PowerLimitPercent = null
            },
            new OcProfile
            {
                ProfileName = "Eco",
                MinTemp = 82,
                MaxTemp = 100,
                CoreOffsetMhz = 0,
                MemoryOffsetMhz = 0,
                PowerLimitPercent = null
            }
        };

        /// <summary>Stock / fail-safe when no band matches or sensors ghost us.</summary>
        public static OcProfile SafeStock { get; } = new()
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            ProfileName = "Safe / Off",
            MinTemp = 0,
            MaxTemp = 0,
            CoreOffsetMhz = 0,
            MemoryOffsetMhz = 0,
            PowerLimitPercent = 100
        };

        public OcProfile? GetById(Guid id)
        {
            lock (_sync)
                return _profiles.FirstOrDefault(p => p.Id == id)?.Clone();
        }

        public void Add(OcProfile profile)
        {
            lock (_sync)
            {
                if (profile.Id == Guid.Empty) profile.Id = Guid.NewGuid();
                Validate(profile);
                _profiles.Add(profile.Clone());
                PersistUnlocked();
            }
            ProfilesChanged?.Invoke();
        }

        public void Update(OcProfile profile)
        {
            lock (_sync)
            {
                int idx = _profiles.FindIndex(p => p.Id == profile.Id);
                if (idx < 0) throw new InvalidOperationException("Profile not found");
                Validate(profile);
                _profiles[idx] = profile.Clone();
                PersistUnlocked();
            }
            ProfilesChanged?.Invoke();
        }

        public bool Remove(Guid id)
        {
            bool removed;
            lock (_sync)
            {
                removed = _profiles.RemoveAll(p => p.Id == id) > 0;
                if (removed) PersistUnlocked();
            }
            if (removed) ProfilesChanged?.Invoke();
            return removed;
        }

        public void ReplaceAll(IEnumerable<OcProfile> profiles)
        {
            lock (_sync)
            {
                _profiles.Clear();
                foreach (var p in profiles)
                {
                    var c = p.Clone();
                    if (c.Id == Guid.Empty) c.Id = Guid.NewGuid();
                    Validate(c);
                    _profiles.Add(c);
                }
                if (_profiles.Count == 0)
                    _profiles.AddRange(CreateDefaultTemplates());
                PersistUnlocked();
            }
            ProfilesChanged?.Invoke();
        }

        public void ResetToDefaults()
        {
            ReplaceAll(CreateDefaultTemplates());
        }

        public void ExportToFile(string filePath)
        {
            var dto = new OcProfileExportDocument
            {
                Version = 1,
                ExportedUtc = DateTime.UtcNow,
                Profiles = Profiles.ToList()
            };
            File.WriteAllText(filePath, JsonSerializer.Serialize(dto, JsonOpts));
        }

        public void ImportFromFile(string filePath, bool replaceExisting)
        {
            string json = File.ReadAllText(filePath);
            var dto = JsonSerializer.Deserialize<OcProfileExportDocument>(json, JsonOpts)
                      ?? throw new InvalidOperationException("Invalid profile file");

            var incoming = dto.Profiles ?? new List<OcProfile>();
            if (incoming.Count == 0)
                throw new InvalidOperationException("No profiles in file");

            if (replaceExisting)
            {
                ReplaceAll(incoming);
                return;
            }

            lock (_sync)
            {
                foreach (var p in incoming)
                {
                    var c = p.Clone();
                    // don't smash IDs when merging imports
                    if (_profiles.Any(x => x.Id == c.Id))
                        c.Id = Guid.NewGuid();
                    Validate(c);
                    _profiles.Add(c);
                }
                PersistUnlocked();
            }
            ProfilesChanged?.Invoke();
        }

        private void LoadOrCreateDefaults()
        {
            lock (_sync)
            {
                if (File.Exists(_path))
                {
                    try
                    {
                        string json = File.ReadAllText(_path);
                        var dto = JsonSerializer.Deserialize<OcProfileExportDocument>(json, JsonOpts);
                        if (dto?.Profiles is { Count: > 0 })
                        {
                            _profiles.Clear();
                            foreach (var p in dto.Profiles)
                            {
                                var c = p.Clone();
                                if (c.Id == Guid.Empty) c.Id = Guid.NewGuid();
                                _profiles.Add(c);
                            }
                            return;
                        }
                    }
                    catch { /* oops — just fall through to defaults */ }
                }

                _profiles.Clear();
                _profiles.AddRange(CreateDefaultTemplates());
                PersistUnlocked();
            }
        }

        private void PersistUnlocked()
        {
            var dto = new OcProfileExportDocument
            {
                Version = 1,
                ExportedUtc = DateTime.UtcNow,
                Profiles = _profiles.Select(p => p.Clone()).ToList()
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(dto, JsonOpts));
        }

        private static void Validate(OcProfile p)
        {
            if (string.IsNullOrWhiteSpace(p.ProfileName))
                throw new ArgumentException("profile_name required");
            if (p.MaxTemp < p.MinTemp)
                throw new ArgumentException("max_temp must be >= min_temp");
            if (p.PowerLimitPercent is int pl && (pl < 50 || pl > 150))
                throw new ArgumentException("power_limit_percent out of range");
        }
    }

    public sealed class OcProfileExportDocument
    {
        public int Version { get; set; } = 1;
        public DateTime ExportedUtc { get; set; }
        public List<OcProfile> Profiles { get; set; } = new();
    }
}
