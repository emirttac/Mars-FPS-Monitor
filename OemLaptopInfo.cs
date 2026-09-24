using System;
using System.Management;

namespace FPSOverlay
{
    public enum OemLaptopVendor
    {
        Unknown = 0,
        Asus,
        Lenovo,
        Hp,
        Dell,
        Msi
    }

    /// <summary>One-shot Win32 manufacturer / model probe for OEM fan backends.</summary>
    public static class OemLaptopInfo
    {
        private static readonly object Gate = new();
        private static bool _probed;
        private static OemLaptopVendor _vendor;
        private static string _manufacturer = "";
        private static string _model = "";

        public static OemLaptopVendor Vendor
        {
            get { Ensure(); return _vendor; }
        }

        public static string Manufacturer
        {
            get { Ensure(); return _manufacturer; }
        }

        public static string Model
        {
            get { Ensure(); return _model; }
        }

        public static bool Is(OemLaptopVendor vendor) => Vendor == vendor;

        private static void Ensure()
        {
            lock (Gate)
            {
                if (_probed) return;
                _probed = true;
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        @"root\CIMV2",
                        "SELECT Manufacturer, Model FROM Win32_ComputerSystem");
                    searcher.Options.Timeout = TimeSpan.FromSeconds(2);
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        _manufacturer = (mo["Manufacturer"] as string)?.Trim() ?? "";
                        _model = (mo["Model"] as string)?.Trim() ?? "";
                        break;
                    }
                }
                catch (Exception ex)
                {
                    OcDebugLog.LogError("[FAN] OEM manufacturer probe failed", ex);
                }

                _vendor = Classify(_manufacturer, _model);
                OcDebugLog.Log($"[FAN] OEM chassis · vendor={_vendor} · '{_manufacturer}' · '{_model}'");
            }
        }

        internal static OemLaptopVendor Classify(string manufacturer, string model)
        {
            string m = (manufacturer ?? "") + " " + (model ?? "");
            if (Contains(m, "asus", "rog", "tuf gaming", "vivobook", "zenbook", "strix"))
                return OemLaptopVendor.Asus;
            if (Contains(m, "lenovo", "legion", "ideapad", "thinkpad", "loq", "yoga"))
                return OemLaptopVendor.Lenovo;
            if (Contains(m, "hewlett", "hp ", " hp", "omen", "pavilion", "victus"))
                return OemLaptopVendor.Hp;
            if (Contains(m, "dell", "alienware", "xps", "g15", "g16", "g3", "g5", "g7"))
                return OemLaptopVendor.Dell;
            if (Contains(m, "micro-star", "msi", "gs66", "ge76", "raider", "stealth", "vector"))
                return OemLaptopVendor.Msi;
            return OemLaptopVendor.Unknown;
        }

        private static bool Contains(string haystack, params string[] needles)
        {
            foreach (var n in needles)
            {
                if (haystack.Contains(n, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
