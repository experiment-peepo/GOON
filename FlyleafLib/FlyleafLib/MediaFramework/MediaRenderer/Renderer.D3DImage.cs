using System;
using System.Runtime.InteropServices;
using nint = System.IntPtr;
using SharpGen.Runtime;

using Vortice.Direct3D9;
using Vortice.Direct3D11;
using Vortice.DXGI;

using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;
using ID3D11RenderTargetView = Vortice.Direct3D11.ID3D11RenderTargetView;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    public D3DImageManager D3DImage { get; private set; }

    internal void D3DImageInit()
    {
        D3DImage = new D3DImageManager(this);
    }

    internal void D3DImageDispose()
    {
        D3DImage?.Dispose();
        D3DImage = null;
    }

    internal ID3D11RenderTargetView CurrentRtv => (D3DImage != null && D3DImage.IsEnabled) ? D3DImage.SharedRtv11 : SwapChain.BackBufferRtv;
    internal ID3D11VideoProcessorOutputView CurrentVpov => (D3DImage != null && D3DImage.IsEnabled) ? D3DImage.SharedVpov11 : SwapChain.VPOV;
}

public class D3DImageManager : IDisposable
{
    public bool IsEnabled { get; set; }

    public ID3D11Texture2D      SharedTexture11 { get; private set; }
    public ID3D11RenderTargetView SharedRtv11   { get; private set; }
    public IDXGIKeyedMutex      KeyedMutex11    { get; private set; }
    public ID3D11VideoProcessorOutputView SharedVpov11 { get; private set; }
    public IDirect3DTexture9    SharedTexture9  { get; private set; }
    public Vortice.Direct3D9.IDirect3DSurface9 SharedSurface9  { get; private set; }
    public nint                 SharedHandle    { get; private set; }
    public long                 FrameCount      { get; internal set; }
    public bool                 Disposed        { get; private set; }

    private object      lockD3D = new();
    Renderer            renderer;
    IDirect3D9Ex        d3d9;
    IDirect3DDevice9Ex  device9;

    static Vortice.Direct3D11.VideoProcessorOutputViewDescription vpovd = new() { ViewDimension = Vortice.Direct3D11.VideoProcessorOutputViewDimension.Texture2D };

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    public D3DImageManager(Renderer renderer)
    {
        this.renderer = renderer;
    }

    private void InitD3D9()
    {
        if (device9 != null) return;
        try
        {
            d3d9 = D3D9.Direct3DCreate9Ex();

            // Find matching D3D9 adapter for the D3D11 device
            uint adapterIndex = 0;
            var d3d11Luid = renderer.DXGIAdapter.Description.Luid;
            
            for (uint i = 0; i < d3d9.AdapterCount; i++)
            {
                try {
                    if (d3d9.GetAdapterLuid(i) == d3d11Luid)
                    {
                        adapterIndex = i;
                        renderer.Log.Debug($"Matched D3D9 adapter index {i} for LUID {d3d11Luid}");
                        break;
                    }
                } catch { }
            }

            Vortice.Direct3D9.PresentParameters pp = new()
            {
                Windowed = true,
                SwapEffect = Vortice.Direct3D9.SwapEffect.Discard,
                DeviceWindowHandle = nint.Zero,
                PresentationInterval = Vortice.Direct3D9.PresentInterval.Default
            };

            nint focusWindow = GetDesktopWindow();
            try {
                device9 = d3d9.CreateDeviceEx(adapterIndex, DeviceType.Hardware, focusWindow, CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded | CreateFlags.PureDevice, pp);
            } catch {
                device9 = d3d9.CreateDeviceEx(adapterIndex, DeviceType.Hardware, focusWindow, CreateFlags.SoftwareVertexProcessing | CreateFlags.Multithreaded, pp);
            }
        }
        catch (Exception ex)
        {
            renderer.Log.Error($"Failed to initialize D3D9 for D3DImage: {ex.Message}");
        }
    }

    public void EnsureSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || Disposed) return;

        if (SharedTexture11 != null && 
            SharedTexture11.Description.Width == width && 
            SharedTexture11.Description.Height == height)
            return;

        lock (lockD3D)
        {
            if (Disposed) return;
            if (SharedTexture11 != null && 
                SharedTexture11.Description.Width == width && 
                SharedTexture11.Description.Height == height)
                return;

            DisposeTextures();
            InitD3D9();
            if (device9 == null) return;

            ((IVP)renderer).UpdateSize(width, height);
            renderer.VPRequest(VPRequestType.Resize);

        try
        {
            Texture2DDescription desc = new Texture2DDescription()
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Vortice.DXGI.Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                CPUAccessFlags = Vortice.Direct3D11.CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.Shared // Removed SharedKeyedMutex for D3D9 compatibility
            };

            SharedTexture11 = renderer.Device.CreateTexture2D(desc);
            SharedRtv11 = renderer.Device.CreateRenderTargetView(SharedTexture11);
            // KeyedMutex11 = SharedTexture11.QueryInterfaceOrNull<IDXGIKeyedMutex>(); // Not compatible with D3D9 interop

            if (renderer.vd != null)
                SharedVpov11 = renderer.vd.CreateVideoProcessorOutputView(SharedTexture11, renderer.ve, vpovd);

            using (var resource = SharedTexture11.QueryInterface<IDXGIResource>())
            {
                SharedHandle = resource.SharedHandle;
                renderer.Log.Debug($"Retrieved SharedHandle: {SharedHandle}");
            }

            // Update D2D target if available
            if (renderer.context2d != null)
            {
                using var surface = SharedTexture11.QueryInterface<IDXGISurface>();
                var bitmapProps = new Vortice.Direct2D1.BitmapProperties1
                {
                    PixelFormat = new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                    BitmapOptions = Vortice.Direct2D1.BitmapOptions.Target | Vortice.Direct2D1.BitmapOptions.CannotDraw
                };
                try {
                    var bitmap = renderer.context2d.CreateBitmapFromDxgiSurface(surface, bitmapProps);
                    renderer.context2d.Target = bitmap;
                    bitmap.Dispose(); // context2d keeps a reference
                    renderer.Log.Debug("Updated D2D target to SharedTexture11");
                } catch (Exception d2dex) {
                    renderer.Log.Warn($"Failed to update D2D target: {d2dex.Message}");
                }
            }

            // Create D3D9 shared texture
            Vortice.Direct3D9.Format format9 = Vortice.Direct3D9.Format.A8R8G8B8; // Matches B8G8R8A8_UNorm
            nint sharedHandle = SharedHandle;
            
            renderer.Log.Debug($"Creating D3D9 Shared Texture: {width}x{height} Format:{format9} Handle:{sharedHandle}");
            SharedTexture9 = device9.CreateTexture((uint)width, (uint)height, 1, Vortice.Direct3D9.Usage.RenderTarget, format9, Pool.Default, ref sharedHandle);
            SharedSurface9 = SharedTexture9.GetSurfaceLevel(0);
            
            renderer.Log.Info($"D3DImage Shared Textures Created Successfully: {width}x{height}");
        }
        catch (Exception ex)
        {
            renderer.Log.Error($"Failed to create shared textures: {ex.Message} (Width:{width} Height:{height})");
            if (ex is SharpGenException sgEx)
                renderer.Log.Error($"SharpGen Error: {sgEx.ResultCode.NativeApiCode} ({sgEx.ResultCode})");
            DisposeTextures();
        }
        }
    }

    public void Acquire() => KeyedMutex11?.AcquireSync(0, 1000);
    public void Release() => KeyedMutex11?.ReleaseSync(1);

    public void AcquireWPF() => KeyedMutex11?.AcquireSync(1, 1000);
    public void ReleaseWPF() => KeyedMutex11?.ReleaseSync(0);

    public void DisposeTextures()
    {
        lock (lockD3D)
        {
            KeyedMutex11?.Dispose();
            KeyedMutex11 = null;
            SharedVpov11?.Dispose();
            SharedVpov11 = null;
            SharedRtv11?.Dispose();
            SharedRtv11 = null;
            SharedTexture11?.Dispose();
            SharedTexture11 = null;
            SharedSurface9?.Dispose();
            SharedSurface9 = null;
            SharedTexture9?.Dispose();
            SharedTexture9 = null;
            SharedHandle = nint.Zero;
        }
    }

    public void Dispose()
    {
        lock (lockD3D)
        {
            if (Disposed) return;
            Disposed = true;
        }
        
        DisposeTextures();
        device9?.Dispose();
        device9 = null;
        d3d9?.Dispose();
        d3d9 = null;
    }
}
