using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FPSOverlay
{
    public sealed class FanCurveDocument
    {
        public int Version { get; set; } = 1;
        public DateTime SavedUtc { get; set; }
        public List<FanCurve> Curves { get; set; } = new();
        public List<FanChannelAssignment> Assignments { get; set; } = new();
    }

    /// <summary>Silent / Balanced / Performance presets plus user curve edits.</summary>
    public sealed class FanCurveStore
    {
        private readonly string _path;
        private readonly List<FanCurve> _curves = new();
        private readonly List<FanChannelAssignment> _assignments = new();
        private readonly object _sync = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        public FanCurveStore(string? path = null)
        {
            _path = path ?? AppPaths.FanCurvesPath;
            LoadOrCreateDefaults();
        }

        public event Action? Changed;

        public IReadOnlyList<FanCurve> Curves
        {
            get { lock (_sync) return _curves.Select(c => c.Clone()).ToList(); }
        }

        public IReadOnlyList<FanChannelAssignment> Assignments
        {
            get { lock (_sync) return _assignments.Select(CloneAssignment).ToList(); }
        }

        public static Guid SilentId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000011");
        public static Guid BalancedId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000012");
        public static Guid PerformanceId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000013");

        public static List<FanCurve> CreateDefaultCurves() => new()
        {
            new FanCurve
            {
                Id = SilentId,
                Name = "Silent",
                HysteresisC = 2,
                TempSource = FanTempSource.MaxCpuGpu,
                Points = new List<FanCurvePoint>
                {
                    new() { TempC = 30, PwmPercent = 25 },
                    new() { TempC = 45, PwmPercent = 30 },
                    new() { TempC = 60, PwmPercent = 40 },
                    new() { TempC = 75, PwmPercent = 60 },
                    new() { TempC = 90, PwmPercent = 85 }
                }
            },
            new FanCurve
            {
                Id = BalancedId,
                Name = "Balanced",
                HysteresisC = 2,
                TempSource = FanTempSource.MaxCpuGpu,
                Points = new List<FanCurvePoint>
                {
                    new() { TempC = 30, PwmPercent = 30 },
                    new() { TempC = 50, PwmPercent = 45 },
                    new() { TempC = 65, PwmPercent = 60 },
                    new() { TempC = 78, PwmPercent = 80 },
                    new() { TempC = 90, PwmPercent = 100 }
                }
            },
            new FanCurve
            {
                Id = PerformanceId,
                Name = "Performance",
                HysteresisC = 2,
                TempSource = FanTempSource.MaxCpuGpu,
                Points = new List<FanCurvePoint>
                {
                    new() { TempC = 30, PwmPercent = 40 },
                    new() { TempC = 45, PwmPercent = 55 },
                    new() { TempC = 55, PwmPercent = 70 },
                    new() { TempC = 70, PwmPercent = 90 },
                    new() { TempC = 82, PwmPercent = 100 }
                }
            }
        };

        public FanCurve? GetById(Guid id)
        {
            lock (_sync)
                return _curves.FirstOrDefault(c => c.Id == id)?.Clone();
        }

        public void Update(FanCurve curve)
        {
            lock (_sync)
            {
                Validate(curve);
                int idx = _curves.FindIndex(c => c.Id == curve.Id);
                if (idx < 0)
                    _curves.Add(curve.Clone());
                else
                    _curves[idx] = curve.Clone();
                PersistUnlocked();
            }
            Changed?.Invoke();
        }

        public void ReplaceAll(IEnumerable<FanCurve> curves)
        {
            lock (_sync)
            {
                _curves.Clear();
                foreach (var c in curves)
                {
                    var clone = c.Clone();
                    if (clone.Id == Guid.Empty) clone.Id = Guid.NewGuid();
                    Validate(clone);
                    _curves.Add(clone);
                }
                if (_curves.Count == 0)
                    _curves.AddRange(CreateDefaultCurves());
                PersistUnlocked();
            }
            Changed?.Invoke();
        }

        public void ResetToDefaults()
        {
            lock (_sync)
            {
                _curves.Clear();
                _curves.AddRange(CreateDefaultCurves());
                PersistUnlocked();
            }
            Changed?.Invoke();
        }

        public void UpsertAssignment(FanChannelAssignment assignment)
        {
            lock (_sync)
            {
                int idx = _assignments.FindIndex(a =>
                    string.Equals(a.ChannelId, assignment.ChannelId, StringComparison.OrdinalIgnoreCase));
                var copy = CloneAssignment(assignment);
                if (idx < 0) _assignments.Add(copy);
                else _assignments[idx] = copy;
                PersistUnlocked();
            }
            Changed?.Invoke();
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
                        var dto = JsonSerializer.Deserialize<FanCurveDocument>(json, JsonOpts);
                        if (dto?.Curves is { Count: > 0 })
                        {
                            _curves.Clear();
                            foreach (var c in dto.Curves)
                            {
                                var clone = c.Clone();
                                if (clone.Id == Guid.Empty) clone.Id = Guid.NewGuid();
                                if (clone.Points.Count >= 2)
                                    _curves.Add(clone);
                            }
                            _assignments.Clear();
                            if (dto.Assignments != null)
                                _assignments.AddRange(dto.Assignments.Select(CloneAssignment));
                            if (_curves.Count > 0)
                                return;
                        }
                    }
                    catch { }
                }

                _curves.Clear();
                _curves.AddRange(CreateDefaultCurves());
                PersistUnlocked();
            }
        }

        private void PersistUnlocked()
        {
            var dto = new FanCurveDocument
            {
                Version = 1,
                SavedUtc = DateTime.UtcNow,
                Curves = _curves.Select(c => c.Clone()).ToList(),
                Assignments = _assignments.Select(CloneAssignment).ToList()
            };
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(dto, JsonOpts));
        }

        private static FanChannelAssignment CloneAssignment(FanChannelAssignment a) => new()
        {
            ChannelId = a.ChannelId,
            Mode = a.Mode,
            CurveId = a.CurveId,
            ManualPwmPercent = a.ManualPwmPercent
        };

        private static void Validate(FanCurve curve)
        {
            if (string.IsNullOrWhiteSpace(curve.Name))
                throw new ArgumentException("curve name required");
            if (curve.Points == null || curve.Points.Count < 2)
                throw new ArgumentException("curve needs at least 2 points");
            foreach (var p in curve.Points)
            {
                p.TempC = Math.Clamp(p.TempC, 0, 120);
                p.PwmPercent = Math.Clamp(p.PwmPercent, 0, 100);
            }
            curve.Points = curve.Points.OrderBy(p => p.TempC).ToList();
        }
    }
}
