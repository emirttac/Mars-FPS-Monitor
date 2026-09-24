namespace FPSOverlay
{
    public enum FrameProviderKind
    {
        Dxgi,
        D3d9,
        DxgKrnl
    }

    public readonly struct FrameObserveResult
    {
        public bool CountFrame { get; init; }
        public bool ResetSamples { get; init; }
    }

    /// <summary>
    /// One present equals one frame. Flip and Blit are ignored because they accompany Present.
    /// D3D9 outranks DXGI, which outranks DxgKrnl, so the same frame is not counted twice.
    /// </summary>
    public sealed class FrameSourceSelector
    {
        public const int DxgiPresentId = 42;
        public const int D3d9PresentId = 1;
        public const int DxgKrnlPresentId = 184;

        private FrameProviderKind? _locked;
        private bool _sawD3d9;
        private bool _sawDxgi;
        private bool _sawDxg;

        public FrameObserveResult Observe(FrameProviderKind kind, int eventId)
        {
            if (!IsPresent(kind, eventId))
                return default;

            Note(kind);

            if (_locked == null)
            {
                _locked = kind;
                return new FrameObserveResult { CountFrame = true };
            }

            if (Priority(kind) > Priority(_locked.Value))
            {
                _locked = kind;
                return new FrameObserveResult { CountFrame = true, ResetSamples = true };
            }

            if (kind == _locked)
                return new FrameObserveResult { CountFrame = true };

            return default;
        }

        /// <summary>Call once per one-second window so a silent source can fall back.</summary>
        public void CompleteWindow()
        {
            if (_locked == FrameProviderKind.D3d9 && !_sawD3d9)
                _locked = null;
            else if (_locked == FrameProviderKind.Dxgi && !_sawDxgi)
                _locked = null;
            else if (_locked == FrameProviderKind.DxgKrnl && !_sawDxg)
                _locked = null;

            if (_sawD3d9)
                _locked = FrameProviderKind.D3d9;
            else if (_sawDxgi)
                _locked = FrameProviderKind.Dxgi;
            else if (_sawDxg)
                _locked = FrameProviderKind.DxgKrnl;

            _sawD3d9 = false;
            _sawDxgi = false;
            _sawDxg = false;
        }

        public void Reset()
        {
            _locked = null;
            _sawD3d9 = false;
            _sawDxgi = false;
            _sawDxg = false;
        }

        public static bool IsPresent(FrameProviderKind kind, int eventId) => kind switch
        {
            FrameProviderKind.Dxgi => eventId == DxgiPresentId,
            FrameProviderKind.D3d9 => eventId == D3d9PresentId,
            FrameProviderKind.DxgKrnl => eventId == DxgKrnlPresentId,
            _ => false
        };

        private static int Priority(FrameProviderKind kind) => kind switch
        {
            FrameProviderKind.D3d9 => 3,
            FrameProviderKind.Dxgi => 2,
            FrameProviderKind.DxgKrnl => 1,
            _ => 0
        };

        private void Note(FrameProviderKind kind)
        {
            switch (kind)
            {
                case FrameProviderKind.D3d9:
                    _sawD3d9 = true;
                    break;
                case FrameProviderKind.Dxgi:
                    _sawDxgi = true;
                    break;
                case FrameProviderKind.DxgKrnl:
                    _sawDxg = true;
                    break;
            }
        }
    }
}
