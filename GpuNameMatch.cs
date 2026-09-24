using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FPSOverlay
{
    /// <summary>
    /// Matches a user-selected GPU name to a vendor adapter.
    /// Vendor words are ignored so "NVIDIA GeForce RTX 4070" can meet "RTX 4070".
    /// A shorter or different model does not win.
    /// </summary>
    public static class GpuNameMatch
    {
        public enum Vendor
        {
            Unknown,
            Nvidia,
            Amd,
            Intel
        }

        private static readonly string[] Noise =
        {
            "NVIDIA", "GEFORCE", "AMD", "RADEON", "INTEL", "GRAPHICS", "DESKTOP",
            "(TM)", "(R)"
        };

        public static bool IsUnknown(string? name)
            => string.IsNullOrWhiteSpace(name)
               || name.Contains("Unknown", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Bilinmeyen", StringComparison.OrdinalIgnoreCase);

        public static Vendor VendorOf(string? name)
        {
            if (IsUnknown(name))
                return Vendor.Unknown;
            if (ContainsAny(name, "NVIDIA", "GEFORCE"))
                return Vendor.Nvidia;
            if (ContainsAny(name, "AMD", "RADEON"))
                return Vendor.Amd;
            if (ContainsAny(name, "INTEL", "ARC", "UHD", "IRIS"))
                return Vendor.Intel;
            return Vendor.Unknown;
        }

        public static bool ShouldUseProvider(Vendor selected, Vendor provider)
            => selected == Vendor.Unknown || selected == provider;

        public static string Core(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "";
            string s = name.ToUpperInvariant().Replace("™", " ", StringComparison.Ordinal).Replace("®", " ", StringComparison.Ordinal);
            foreach (string token in Noise)
                s = s.Replace(token, " ", StringComparison.Ordinal);
            return Regex.Replace(s, @"[^A-Z0-9]+", "");
        }

        public static bool Matches(string? selected, string? candidate)
        {
            if (IsUnknown(selected) || IsUnknown(candidate))
                return false;
            return SameModel(Core(selected), Core(candidate));
        }

        /// <summary>
        /// The single adapter whose model key equals the selection.
        /// Two equal keys return null so the caller writes nothing.
        /// </summary>
        public static string? PickBest(string? selected, IEnumerable<string> candidates)
        {
            if (IsUnknown(selected) || candidates == null)
                return null;
            string selectedKey = ModelKey(Core(selected));
            string? match = null;
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;
                if (!SameModel(selectedKey, ModelKey(Core(candidate))))
                    continue;
                if (match != null)
                    return null;
                match = candidate;
            }
            return match;
        }

        /// <summary>
        /// Index of <see cref="PickBest"/>. Unknown selection keeps the first adapter.
        /// A known selection with no match, or more than one match, returns null.
        /// </summary>
        public static int? IndexOfBest(string? selected, IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0)
                return null;
            if (IsUnknown(selected))
                return 0;

            string selectedKey = ModelKey(Core(selected));
            int found = -1;
            for (int i = 0; i < names.Count; i++)
            {
                if (!SameModel(selectedKey, ModelKey(Core(names[i]))))
                    continue;
                if (found >= 0)
                    return null;
                found = i;
            }
            return found >= 0 ? found : null;
        }

        /// <summary>
        /// Same model after vendor noise is removed.
        /// Laptop/mobile/notebook/gpu and a trailing "Fan" label are not a different card.
        /// Ti, Super, XT, and a different number stay different.
        /// </summary>
        private static bool SameModel(string leftCoreOrKey, string rightCoreOrKey)
        {
            string left = ModelKey(leftCoreOrKey);
            string right = ModelKey(rightCoreOrKey);
            return left.Length >= 4 && left == right;
        }

        private static string ModelKey(string core)
        {
            if (string.IsNullOrEmpty(core))
                return "";
            core = Regex.Replace(core, @"FAN\d*$", "");
            string[] suffixes = { "NOTEBOOK", "LAPTOP", "MOBILE", "GPU" };
            bool stripped = true;
            while (stripped)
            {
                stripped = false;
                foreach (string suffix in suffixes)
                {
                    if (core.Length > suffix.Length && core.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        core = core[..^suffix.Length];
                        stripped = true;
                        break;
                    }
                }
            }
            return core;
        }

        private static bool ContainsAny(string? name, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            foreach (string token in tokens)
            {
                if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
