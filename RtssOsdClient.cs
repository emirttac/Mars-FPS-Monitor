using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using Microsoft.Win32;

namespace FPSOverlay
{
    /// <summary>
    /// CapFrameX-style RTSS shared-memory OSD client.
    /// Does not redistribute RTSS — finds / launches an installed copy, feeds text,
    /// and hides the RTSS UI window so Mars stays the front-facing product.
    /// </summary>
    public sealed class RtssOsdClient : IDisposable
    {
        public const string OsdOwnerName = "MarsFPSMonitor";
        private const string SharedMemoryName = "RTSSSharedMemoryV2";
        private const uint SignatureRtss = 0x52545353; // 'RTSS' little-endian as DWORD
        private const uint MinVersion = 0x00020000;
        private const uint Version27 = 0x00020007;

        private readonly object _gate = new();
        private bool _disposed;
        private long _lastHideTicks;
        private long _lastLaunchAttemptTicks;
        private volatile bool _launchInProgress;

        public bool IsAvailable { get; private set; }
        public string StatusMessage { get; private set; } = "RTSS not connected";

        public bool EnsureRunning(bool allowLaunch)
        {
            if (_disposed) return false;

            if (IsSharedMemoryPresent())
            {
                IsAvailable = true;
                StatusMessage = "RTSS connected";
                HideRtssWindows();
                return true;
            }

            IsAvailable = false;

            if (!allowLaunch)
            {
                StatusMessage = "RTSS not running";
                return false;
            }

            string? exe = FindRtssExecutable();
            if (exe == null)
            {
                StatusMessage = "RTSS not installed";
                return false;
            }

            long now = Environment.TickCount64;
            if (_launchInProgress || now - _lastLaunchAttemptTicks < 4000)
            {
                StatusMessage = "RTSS starting…";
                return false;
            }

            _lastLaunchAttemptTicks = now;
            _launchInProgress = true;
            StatusMessage = "RTSS starting…";

            // Never block the UI thread with Sleep waits.
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (!IsRtssProcessAlive())
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = exe,
                            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        Process.Start(psi);
                    }

                    for (int i = 0; i < 25; i++)
                    {
                        System.Threading.Thread.Sleep(120);
                        if (IsSharedMemoryPresent())
                        {
                            IsAvailable = true;
                            StatusMessage = "RTSS connected";
                            HideRtssWindows();
                            return;
                        }
                    }

                    StatusMessage = "RTSS not ready";
                }
                catch (Exception ex)
                {
                    StatusMessage = "RTSS launch failed: " + ex.Message;
                }
                finally
                {
                    _launchInProgress = false;
                }
            });

            return false;
        }

        public bool UpdateOsd(string text, int osdX = int.MinValue, int osdY = int.MinValue, int zoom = 0)
        {
            if (_disposed) return false;
            text ??= "";

            lock (_gate)
            {
                try
                {
                    using var mmf = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.ReadWrite);
                    using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);

                    uint signature = accessor.ReadUInt32(0);
                    uint version = accessor.ReadUInt32(4);
                    if (signature != SignatureRtss || version < MinVersion)
                    {
                        IsAvailable = false;
                        StatusMessage = "RTSS shared memory invalid";
                        return false;
                    }

                    uint appEntrySize = accessor.ReadUInt32(8);
                    uint appArrOffset = accessor.ReadUInt32(12);
                    uint appArrSize = accessor.ReadUInt32(16);
                    uint osdEntrySize = accessor.ReadUInt32(20);
                    uint osdArrOffset = accessor.ReadUInt32(24);
                    uint osdArrSize = accessor.ReadUInt32(28);
                    if (osdEntrySize == 0 || osdArrSize < 2)
                        return false;

                    bool useBusy = version >= 0x00020014;
                    if (useBusy)
                    {
                        int busy = accessor.ReadInt32(36);
                        accessor.Write(36, busy | 1);
                    }

                    try
                    {
                        // Push Display placement onto hooked 3D apps (color/size live in hypertext).
                        if (appEntrySize >= 332 && appArrSize > 0 &&
                            (osdX != int.MinValue || osdY != int.MinValue || zoom > 0))
                        {
                            for (uint a = 0; a < appArrSize; a++)
                            {
                                long app = appArrOffset + (long)a * appEntrySize;
                                uint pid = accessor.ReadUInt32(app);
                                if (pid == 0)
                                    continue;

                                // dwOSDX @ 316, dwOSDY @ 320, dwOSDPixel @ 324 within classic APP_ENTRY
                                if (osdX != int.MinValue)
                                    accessor.Write(app + 316, osdX);
                                if (osdY != int.MinValue)
                                    accessor.Write(app + 320, osdY);
                                if (zoom > 0)
                                    accessor.Write(app + 324, (uint)zoom);
                            }
                        }

                        bool wrote = false;
                        for (int pass = 0; pass < 2 && !wrote; pass++)
                        {
                            for (uint i = 1; i < osdArrSize; i++)
                            {
                                long entry = osdArrOffset + (long)i * osdEntrySize;
                                string owner = ReadAnsi(accessor, entry + 256, 256);

                                if (pass == 1 && string.IsNullOrEmpty(owner))
                                {
                                    WriteAnsi(accessor, entry + 256, 256, OsdOwnerName);
                                    owner = OsdOwnerName;
                                }

                                if (!string.Equals(owner, OsdOwnerName, StringComparison.Ordinal))
                                    continue;

                                byte[] ansi = Encoding.Default.GetBytes(text);
                                if (version >= Version27 && osdEntrySize >= 256 + 256 + 4096)
                                {
                                    WriteAnsiBytes(accessor, entry + 512, 4095, ansi);
                                    WriteAnsi(accessor, entry, 256, "");
                                }
                                else
                                {
                                    WriteAnsiBytes(accessor, entry, 255, ansi);
                                }

                                uint frame = accessor.ReadUInt32(32);
                                accessor.Write(32, frame + 1);
                                wrote = true;
                                break;
                            }
                        }

                        IsAvailable = wrote;
                        StatusMessage = wrote ? "RTSS OSD active" : "RTSS OSD slots full";
                        if (wrote)
                            HideRtssWindows();
                        return wrote;
                    }
                    finally
                    {
                        if (useBusy)
                        {
                            int busy = accessor.ReadInt32(36);
                            accessor.Write(36, busy & ~1);
                        }
                    }
                }
                catch (FileNotFoundException)
                {
                    IsAvailable = false;
                    StatusMessage = "RTSS not running";
                    return false;
                }
                catch (Exception ex)
                {
                    IsAvailable = false;
                    StatusMessage = "RTSS write failed: " + ex.Message;
                    return false;
                }
            }
        }

        public void ReleaseOsd()
        {
            lock (_gate)
            {
                try
                {
                    using var mmf = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.ReadWrite);
                    using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);

                    uint signature = accessor.ReadUInt32(0);
                    uint version = accessor.ReadUInt32(4);
                    if (signature != SignatureRtss || version < MinVersion)
                        return;

                    uint osdEntrySize = accessor.ReadUInt32(20);
                    uint osdArrOffset = accessor.ReadUInt32(24);
                    uint osdArrSize = accessor.ReadUInt32(28);

                    for (uint i = 1; i < osdArrSize; i++)
                    {
                        long entry = osdArrOffset + (long)i * osdEntrySize;
                        string owner = ReadAnsi(accessor, entry + 256, 256);
                        if (!string.Equals(owner, OsdOwnerName, StringComparison.Ordinal))
                            continue;

                        WriteAnsi(accessor, entry, 256, "");
                        if (version >= Version27 && osdEntrySize >= 512 + 4096)
                            WriteAnsi(accessor, entry + 512, 4096, "");
                        WriteAnsi(accessor, entry + 256, 256, "");
                        uint frame = accessor.ReadUInt32(32);
                        accessor.Write(32, frame + 1);
                        break;
                    }
                }
                catch { }
            }

            IsAvailable = false;
            StatusMessage = "RTSS OSD released";
        }

        public void HideRtssWindows()
        {
            long now = Environment.TickCount64;
            if (now - _lastHideTicks < 1000)
                return;
            _lastHideTicks = now;

            try
            {
                foreach (var proc in Process.GetProcessesByName("RTSS"))
                {
                    try
                    {
                        int pid = proc.Id;
                        Win32Api.EnumWindows((hWnd, _) =>
                        {
                            Win32Api.GetWindowThreadProcessId(hWnd, out uint wPid);
                            if (wPid != (uint)pid)
                                return true;

                            if (Win32Api.IsWindowVisible(hWnd))
                                Win32Api.ShowWindow(hWnd, Win32Api.SW_HIDE);
                            return true;
                        }, IntPtr.Zero);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static string? FindRtssExecutable()
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "RivaTuner Statistics Server", "RTSS.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "RivaTuner Statistics Server", "RTSS.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "MSI Afterburner", "RTSS", "RTSS.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "MSI Afterburner", "RTSS.exe"),
            };

            foreach (string c in candidates)
            {
                if (File.Exists(c))
                    return c;
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Unwinder\RTSS")
                                ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Unwinder\RTSS");
                string? dir = key?.GetValue("InstallDir") as string
                              ?? key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    string exe = Path.Combine(dir, "RTSS.exe");
                    if (File.Exists(exe))
                        return exe;
                }
            }
            catch { }

            return null;
        }

        public static bool IsRtssInstalled() => FindRtssExecutable() != null;

        private static bool IsRtssProcessAlive()
        {
            try
            {
                return Process.GetProcessesByName("RTSS").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSharedMemoryPresent()
        {
            try
            {
                using var mmf = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.Read);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadAnsi(MemoryMappedViewAccessor accessor, long offset, int maxLen)
        {
            var buf = new byte[maxLen];
            accessor.ReadArray(offset, buf, 0, maxLen);
            int end = Array.IndexOf(buf, (byte)0);
            if (end < 0) end = maxLen;
            return Encoding.Default.GetString(buf, 0, end);
        }

        private static void WriteAnsi(MemoryMappedViewAccessor accessor, long offset, int maxLen, string text)
        {
            WriteAnsiBytes(accessor, offset, maxLen - 1, Encoding.Default.GetBytes(text ?? ""));
        }

        private static void WriteAnsiBytes(MemoryMappedViewAccessor accessor, long offset, int maxChars, byte[] ansi)
        {
            int n = Math.Min(ansi.Length, maxChars);
            if (n > 0)
                accessor.WriteArray(offset, ansi, 0, n);
            accessor.Write(offset + n, (byte)0);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseOsd();
            // Leave RTSS running — user may use it elsewhere; only clear our OSD slot.
            GC.SuppressFinalize(this);
        }
    }
}
