using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace FPSOverlay
{
    public class FpsMonitor : IDisposable
    {
        public int CurrentFps { get; private set; } = 0;
        public float OnePercentLowFps { get; private set; } = 0;
        public float CurrentFrametimeMs { get; private set; } = 0;
        
        private readonly object _frametimeLock = new object();
        private Queue<float> _frametimes = new Queue<float>();
        public float[] GetFrametimesSnapshot()
        {
            lock (_frametimeLock)
            {
                return _frametimes.ToArray();
            }
        }
        
        private int _activePid = 0;
        private CancellationTokenSource? _cts;
        private Task? _etwTask;
        private TraceEventSession? _session;

        private static readonly Guid DXGI_PROVIDER = new Guid(0xCA11C036, 0x0102, 0x4A2D, 0xA6, 0xAD, 0xF0, 0x3C, 0xFE, 0xD5, 0xD3, 0xC9);
        private static readonly Guid D3D9_PROVIDER = new Guid(0x783ACA0A, 0x790E, 0x4D7F, 0x84, 0x51, 0xAA, 0x85, 0x05, 0x11, 0xC6, 0xB9);
        private static readonly Guid DXGKRNL_PROVIDER = new Guid(0x802EC45A, 0x1E99, 0x4B83, 0x99, 0x20, 0x87, 0xC9, 0x82, 0x77, 0xBA, 0x9D);

        private const int DXGKRNL_EVENT_PRESENT_INFO = 184; // present = 0x00B8
        private const int DXGKRNL_EVENT_FLIP_INFO    = 168; // flip = 0x00A8
        private const int DXGKRNL_EVENT_BLIT_INFO    = 166; // blit = 0x00A6

        private bool _etwFailed;

        public FpsMonitor()
        {
            _cts = new CancellationTokenSource();
            _etwTask = Task.Run(() => EtwLoop(_cts.Token), _cts.Token);
            Task.Run(() => CalculationLoop(_cts.Token), _cts.Token);
        }

        private int _dxgiCount = 0;
        private int _d3d9Count = 0;
        private int _dxgKrnlCount = 0;
        private double _startTs = 0;
        private int _lastPid = 0;
        
        private void EtwLoop(CancellationToken token)
        {
            try
            {
                if (TraceEventSession.GetActiveSessionNames().Contains("Mars_FPS_Monitor_Session"))
                {
                    var existing = new TraceEventSession("Mars_FPS_Monitor_Session");
                    existing.Dispose();
                }

                using (_session = new TraceEventSession("Mars_FPS_Monitor_Session"))
                {
                    _session.StopOnDispose = true;
                    _session.EnableProvider(DXGI_PROVIDER, TraceEventLevel.Informational);
                    _session.EnableProvider(D3D9_PROVIDER, TraceEventLevel.Informational);
                    _session.EnableProvider(DXGKRNL_PROVIDER, TraceEventLevel.Informational, 0x8000000 | 0x1);

                    _session.Source.Dynamic.All += (TraceEvent data) =>
                    {
                        if (token.IsCancellationRequested) return;

                        int target = _activePid;
                        int pid = data.ProcessID;
                        if (target == 0 || pid != target) return;

                        bool isValid = false;
                        bool isDxgi = false;
                        bool isD3D9 = false;
                        bool isDxgKrnl = false;

                        if (data.ProviderGuid == DXGI_PROVIDER && (int)data.ID == 42)
                        {
                            isValid = true;
                            isDxgi = true;
                        }
                        else if (data.ProviderGuid == D3D9_PROVIDER && (int)data.ID == 1)
                        {
                            isValid = true;
                            isD3D9 = true;
                        }
                        else if (data.ProviderGuid == DXGKRNL_PROVIDER)
                        {
                            if ((int)data.ID == DXGKRNL_EVENT_PRESENT_INFO || 
                                (int)data.ID == DXGKRNL_EVENT_FLIP_INFO || 
                                (int)data.ID == DXGKRNL_EVENT_BLIT_INFO)
                            {
                                isValid = true;
                                isDxgKrnl = true;
                            }
                        }

                        if (!isValid) return;

                        double ts = data.TimeStampRelativeMSec / 1000.0;

                        if (pid != _lastPid)
                        {
                            _lastPid = pid;
                            _dxgiCount = 0;
                            _d3d9Count = 0;
                            _dxgKrnlCount = 0;
                            _startTs = ts;
                        }

                        if (isDxgi) Interlocked.Increment(ref _dxgiCount);
                        if (isD3D9) Interlocked.Increment(ref _d3d9Count);
                        if (isDxgKrnl) Interlocked.Increment(ref _dxgKrnlCount);

                        // frametime math go brrr
                        if (_lastFrameTs > 0)
                        {
                            float frameTimeMs = (float)((ts - _lastFrameTs) * 1000.0);
                            if (frameTimeMs > 0 && frameTimeMs < 1000)
                            {
                                CurrentFrametimeMs = frameTimeMs;
                                lock (_frametimeLock)
                                {
                                    _frametimes.Enqueue(frameTimeMs);
                                    if (_frametimes.Count > 100)
                                    {
                                        _frametimes.Dequeue();
                                    }
                                }
                            }
                        }
                        _lastFrameTs = ts;

                        double elapsed = ts - _startTs;
                        if (elapsed >= 1.0)
                        {
                            int frameCount = 0;
                            if (_d3d9Count > 0) frameCount = _d3d9Count;
                            else if (_dxgiCount > 0) frameCount = _dxgiCount;
                            else if (_dxgKrnlCount > 0)
                            {
                                float potential = (float)_dxgKrnlCount / (float)elapsed;
                                if (potential >= 20.0f) frameCount = _dxgKrnlCount;
                            }

                            if (frameCount > 0)
                                CurrentFps = (int)((float)frameCount / (float)elapsed);
                            else
                                CurrentFps = 0;

                            CalculateOnePercentLow();

                            _dxgiCount = 0;
                            _d3d9Count = 0;
                            _dxgKrnlCount = 0;
                            _startTs = ts;
                        }
                    };

                    _session.Source.Process();
                }
            }
            catch
            {
                // no admin = ETW sad → -1 so UI can yell ADMIN PLS
                _etwFailed = true;
                CurrentFps = -1;
            }
        }

        private double _lastFrameTs = 0;

        private void CalculateOnePercentLow()
        {
            float[] times;
            lock (_frametimeLock)
            {
                if (_frametimes.Count < 10) return;
                times = _frametimes.ToArray();
            }

            Array.Sort(times);
            Array.Reverse(times); // fattest frame times first = 1% low vibes
            
            int index = (int)(times.Length * 0.01);
            if (index >= times.Length) index = times.Length - 1;
            
            float targetFrameTime = times[index];
            if (targetFrameTime > 0)
            {
                OnePercentLowFps = 1000.0f / targetFrameTime;
            }
        }

        private void CalculationLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Thread.Sleep(250);
                if (_activePid == 0) 
                {
                    if (!_etwFailed)
                    {
                        CurrentFps = 0;
                        OnePercentLowFps = 0;
                        CurrentFrametimeMs = 0;
                    }
                }
            }
        }

        public void RefreshFps()
        {
            IntPtr hwnd = Win32Api.GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                Win32Api.GetWindowThreadProcessId(hwnd, out uint pid);
                int currentForegroundPid = (int)pid;
                
                if (currentForegroundPid != 0 && currentForegroundPid != Process.GetCurrentProcess().Id)
                {
                    _activePid = currentForegroundPid;
                }
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            if (_session != null)
            {
                try { _session.StopOnDispose = true; _session.Dispose(); } catch { }
            }
        }
    }
}

