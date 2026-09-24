using System;
using System.Runtime.InteropServices;

namespace FPSOverlay
{
    /// <summary>
    /// Lazy AMD GPU temperature fallback via ADL (atiadlxx.dll).
    /// Only used when LibreHardwareMonitor returns no usable core/edge temp.
    /// Separate context from <see cref="AmdGpuOverclockProvider"/> so OC lifecycle stays independent.
    /// </summary>
    internal sealed class AmdGpuTemperatureReader : IDisposable
    {
        private static readonly object Gate = new();
        private static AmdGpuTemperatureReader? _instance;
        private static bool _initFailed;

        private IntPtr _context;
        private int _adapterIndex = -1;
        private string _adapterName = "";
        private bool _disposed;

        public static float TryReadCoreCelsius(string? preferredAdapterName = null)
        {
            try
            {
                var reader = GetOrCreate();
                return reader?.ReadCoreCelsius(preferredAdapterName) ?? 0f;
            }
            catch
            {
                return 0f;
            }
        }

        public static GpuThermalSample TryReadThermalSample(string? preferredAdapterName = null)
        {
            try
            {
                var reader = GetOrCreate();
                return reader?.ReadThermalSample(preferredAdapterName) ?? GpuThermalSample.Invalid;
            }
            catch
            {
                return GpuThermalSample.Invalid;
            }
        }

        public static void Shutdown()
        {
            lock (Gate)
            {
                _instance?.Dispose();
                _instance = null;
                _initFailed = false;
            }
        }

        private static AmdGpuTemperatureReader? GetOrCreate()
        {
            lock (Gate)
            {
                if (_instance != null) return _instance;
                if (_initFailed) return null;

                try
                {
                    _instance = new AmdGpuTemperatureReader();
                    if (_instance._adapterIndex < 0)
                    {
                        _instance.Dispose();
                        _instance = null;
                        _initFailed = true;
                        return null;
                    }
                    return _instance;
                }
                catch
                {
                    _instance?.Dispose();
                    _instance = null;
                    _initFailed = true;
                    return null;
                }
            }
        }

        private AmdGpuTemperatureReader()
        {
            var alloc = new AdlNative.ADL_Main_Memory_Alloc(AdlNative.MallocCallback);
            int result = AdlNative.ADL2_Main_Control_Create(alloc, 1, out _context);
            if (result != AdlNative.ADL_OK || _context == IntPtr.Zero)
                throw new InvalidOperationException($"ADL2_Main_Control_Create failed ({result})");

            result = AdlNative.ADL2_Adapter_NumberOfAdapters_Get(_context, out int count);
            if (result != AdlNative.ADL_OK || count <= 0)
                throw new InvalidOperationException("No ADL adapters");

            int size = Marshal.SizeOf<AdlNative.AdapterInfo>() * count;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var info = new AdlNative.AdapterInfo { Size = Marshal.SizeOf<AdlNative.AdapterInfo>() };
                    IntPtr p = IntPtr.Add(buffer, i * Marshal.SizeOf<AdlNative.AdapterInfo>());
                    Marshal.StructureToPtr(info, p, false);
                }

                result = AdlNative.ADL2_Adapter_AdapterInfo_Get(_context, buffer, size);
                if (result != AdlNative.ADL_OK)
                    throw new InvalidOperationException($"ADL2_Adapter_AdapterInfo_Get failed ({result})");

                for (int i = 0; i < count; i++)
                {
                    IntPtr p = IntPtr.Add(buffer, i * Marshal.SizeOf<AdlNative.AdapterInfo>());
                    var info = Marshal.PtrToStructure<AdlNative.AdapterInfo>(p);

                    AdlNative.ADL2_Adapter_Active_Get(_context, info.AdapterIndex, out int active);
                    if (active == 0) continue;

                    // Prefer discrete / Overdrive-capable adapters.
                    AdlNative.ADL2_Overdrive_Caps(_context, info.AdapterIndex, out int odSupported, out _, out _);
                    if (odSupported != 0)
                    {
                        _adapterIndex = info.AdapterIndex;
                        _adapterName = info.AdapterName ?? "";
                        break;
                    }

                    if (_adapterIndex < 0)
                    {
                        _adapterIndex = info.AdapterIndex;
                        _adapterName = info.AdapterName ?? "";
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private float ReadCoreCelsius(string? preferredAdapterName)
        {
            EnsureAdapter(preferredAdapterName);
            if (_adapterIndex < 0) return 0f;

            if (TryReadOdnCelsius(AdlNative.ODN_TEMP_EDGE, out float edge) && edge > 0)
                return edge;

            if (TryReadOd5Celsius(out float od5) && od5 > 0)
                return od5;

            // Some ASICs only expose hotspot via ODN.
            if (TryReadOdnCelsius(AdlNative.ODN_TEMP_HOTSPOT, out float hotspot) && hotspot > 0)
                return hotspot;

            return 0f;
        }

        private GpuThermalSample ReadThermalSample(string? preferredAdapterName)
        {
            EnsureAdapter(preferredAdapterName);
            if (_adapterIndex < 0) return GpuThermalSample.Invalid;

            float? core = null;
            float? hotspot = null;

            if (TryReadOdnCelsius(AdlNative.ODN_TEMP_EDGE, out float edge) && edge > 0)
                core = edge;
            else if (TryReadOd5Celsius(out float od5) && od5 > 0)
                core = od5;

            if (TryReadOdnCelsius(AdlNative.ODN_TEMP_HOTSPOT, out float hs) && hs > 0)
                hotspot = hs;

            if (core is null or <= 0 && hotspot is > 0)
                core = hotspot;

            if (core is null or <= 0)
                return GpuThermalSample.Invalid;

            return new GpuThermalSample { CoreTempC = core, HotspotTempC = hotspot };
        }

        private void EnsureAdapter(string? preferredAdapterName)
        {
            if (string.IsNullOrWhiteSpace(preferredAdapterName))
                return;
            if (IsUnknownLabel(preferredAdapterName))
                return;

            // If current adapter already matches, keep it.
            if (NamesMatch(_adapterName, preferredAdapterName))
                return;

            // Re-scan for a better name match without recreating the whole ADL context.
            try
            {
                int result = AdlNative.ADL2_Adapter_NumberOfAdapters_Get(_context, out int count);
                if (result != AdlNative.ADL_OK || count <= 0) return;

                int size = Marshal.SizeOf<AdlNative.AdapterInfo>() * count;
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        var info = new AdlNative.AdapterInfo { Size = Marshal.SizeOf<AdlNative.AdapterInfo>() };
                        IntPtr p = IntPtr.Add(buffer, i * Marshal.SizeOf<AdlNative.AdapterInfo>());
                        Marshal.StructureToPtr(info, p, false);
                    }

                    result = AdlNative.ADL2_Adapter_AdapterInfo_Get(_context, buffer, size);
                    if (result != AdlNative.ADL_OK) return;

                    for (int i = 0; i < count; i++)
                    {
                        IntPtr p = IntPtr.Add(buffer, i * Marshal.SizeOf<AdlNative.AdapterInfo>());
                        var info = Marshal.PtrToStructure<AdlNative.AdapterInfo>(p);
                        AdlNative.ADL2_Adapter_Active_Get(_context, info.AdapterIndex, out int active);
                        if (active == 0) continue;
                        if (!NamesMatch(info.AdapterName, preferredAdapterName)) continue;

                        _adapterIndex = info.AdapterIndex;
                        _adapterName = info.AdapterName ?? "";
                        return;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch { }
        }

        private bool TryReadOdnCelsius(int temperatureType, out float celsius)
        {
            celsius = 0f;
            try
            {
                int r = AdlNative.ADL2_OverdriveN_Temperature_Get(_context, _adapterIndex, temperatureType, out int milli);
                if (r != AdlNative.ADL_OK) return false;
                // ODN returns millidegrees on most drivers; accept already-Celsius if tiny.
                float c = milli > 200 ? milli / 1000f : milli;
                if (c <= 0 || c >= 120) return false;
                celsius = c;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadOd5Celsius(out float celsius)
        {
            celsius = 0f;
            try
            {
                var temp = new AdlNative.ADLTemperature
                {
                    Size = Marshal.SizeOf<AdlNative.ADLTemperature>()
                };
                int r = AdlNative.ADL2_Overdrive5_Temperature_Get(_context, _adapterIndex, 0, ref temp);
                if (r != AdlNative.ADL_OK) return false;
                float c = temp.Temperature > 200 ? temp.Temperature / 1000f : temp.Temperature;
                if (c <= 0 || c >= 120) return false;
                celsius = c;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool NamesMatch(string? a, string? b) => GpuNameMatch.Matches(a, b);

        private static bool IsUnknownLabel(string? name) => GpuNameMatch.IsUnknown(name);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_context != IntPtr.Zero)
            {
                try { AdlNative.ADL2_Main_Control_Destroy(_context); } catch { }
                _context = IntPtr.Zero;
            }
            _adapterIndex = -1;
        }
    }
}
