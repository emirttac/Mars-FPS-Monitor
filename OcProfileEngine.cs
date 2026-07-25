using System;
using System.Collections.Generic;
using System.Linq;

namespace FPSOverlay
{
    public sealed class OcProfileDecision
    {
        public OcProfile Profile { get; init; } = OcProfileStore.SafeStock;
        public string Reason { get; init; } = "";
        public bool Changed { get; init; }
    }

    /// <summary>
    /// Dynamic profile picker: match GPU core temp to bands w/ hysteresis + cooldown.
    /// Sensors weird or hotspot critical? fail-closed to Safe/Off. safety first besties.
    /// </summary>
    public sealed class OcProfileEngine
    {
        private readonly OcProfileStore _store;
        private Guid _currentId = OcProfileStore.SafeStock.Id;
        private DateTime _lastChangeUtc = DateTime.MinValue;
        private bool _initialized;

        public float HotspotForceSafeC { get; set; } = 95f;
        public float UpgradeHysteresisC { get; set; } = 4f;
        public int ModeChangeCooldownSec { get; set; } = 5;

        public OcProfileEngine(OcProfileStore store)
        {
            _store = store;
        }

        public Guid CurrentProfileId => _currentId;

        public void Reset()
        {
            _currentId = OcProfileStore.SafeStock.Id;
            _lastChangeUtc = DateTime.MinValue;
            _initialized = false;
        }

        public OcProfileDecision Evaluate(GpuThermalSample sample, DateTime utcNow)
        {
            if (!sample.IsValid)
                return Transition(OcProfileStore.SafeStock, utcNow, "sensor invalid -> Safe/Off", force: true);

            if (sample.HotspotTempC is float hs && hs >= HotspotForceSafeC)
                return Transition(OcProfileStore.SafeStock, utcNow, $"hotspot {hs:F0}C -> Safe/Off", force: true);

            float core = sample.CoreTempC!.Value;
            var profiles = _store.Profiles;
            OcProfile desired = FindBestMatch(profiles, core) ?? OcProfileStore.SafeStock;

            if (!_initialized)
            {
                _initialized = true;
                return Transition(desired, utcNow, $"initial @ {core:F1}C -> {desired.ProfileName}", force: true);
            }

            OcProfile current = ResolveCurrent(profiles);

            if (desired.Id == current.Id)
                return new OcProfileDecision
                {
                    Profile = current,
                    Reason = $"hold {current.ProfileName} @ {core:F1}C",
                    Changed = false
                };

            bool safer = Rank(desired) < Rank(current);
            if (!safer)
            {
                // more OC only if temp is deep in cooler band (hysteresis = no flicker)
                float upgradeGate = desired.MaxTemp - UpgradeHysteresisC;
                if (core > upgradeGate)
                {
                    return new OcProfileDecision
                    {
                        Profile = current,
                        Reason = $"hysteresis hold {current.ProfileName} (need ≤{upgradeGate:F0}C for {desired.ProfileName})",
                        Changed = false
                    };
                }
            }

            if (InCooldown(utcNow))
            {
                return new OcProfileDecision
                {
                    Profile = current,
                    Reason = $"cooldown — want {desired.ProfileName}, stay {current.ProfileName}",
                    Changed = false
                };
            }

            string why = safer
                ? $"downgrade {current.ProfileName} -> {desired.ProfileName} @ {core:F1}C"
                : $"upgrade {current.ProfileName} -> {desired.ProfileName} @ {core:F1}C";

            return Transition(desired, utcNow, why, force: false);
        }

        /// <summary>
        /// Highest core offset among profiles whose [min_temp, max_temp] contains this temp.
        /// </summary>
        public static OcProfile? FindBestMatch(IReadOnlyList<OcProfile> profiles, float tempC)
        {
            var matches = profiles.Where(p => p.ContainsTemp(tempC)).ToList();
            if (matches.Count == 0) return null;
            return matches
                .OrderByDescending(p => p.CoreOffsetMhz)
                .ThenByDescending(p => p.MemoryOffsetMhz)
                .First();
        }

        private OcProfile ResolveCurrent(IReadOnlyList<OcProfile> profiles)
        {
            if (_currentId == OcProfileStore.SafeStock.Id)
                return OcProfileStore.SafeStock;
            return profiles.FirstOrDefault(p => p.Id == _currentId) ?? OcProfileStore.SafeStock;
        }

        private static int Rank(OcProfile p)
            => p.Id == OcProfileStore.SafeStock.Id ? 0 : (p.CoreOffsetMhz * 1000 + p.MemoryOffsetMhz);

        private bool InCooldown(DateTime utcNow)
            => utcNow - _lastChangeUtc < TimeSpan.FromSeconds(ModeChangeCooldownSec);

        private OcProfileDecision Transition(OcProfile next, DateTime utcNow, string reason, bool force)
        {
            if (next.Id == _currentId && _initialized)
                return new OcProfileDecision { Profile = next, Reason = reason, Changed = false };

            if (!force && _initialized && InCooldown(utcNow) && next.Id != _currentId)
                return new OcProfileDecision
                {
                    Profile = ResolveCurrent(_store.Profiles),
                    Reason = "cooldown block",
                    Changed = false
                };

            bool changed = next.Id != _currentId;
            _currentId = next.Id;
            if (changed) _lastChangeUtc = utcNow;
            return new OcProfileDecision { Profile = next.Clone(), Reason = reason, Changed = changed };
        }
    }
}
