using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Controls.WPF;

public class FlyleafD3DImage : Image, IDisposable
{
    public Player Player
    {
        get => (Player)GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }
    public static readonly DependencyProperty PlayerProperty =
        DependencyProperty.Register("Player", typeof(Player), typeof(FlyleafD3DImage), new PropertyMetadata(null, OnPlayerChanged));

    private D3DImage d3dImage;
    private bool disposed;
    private long lastFrameCount = -1;

    public FlyleafD3DImage()
    {
        d3dImage = new D3DImage();
        this.Source = d3dImage;
        this.Stretch = Stretch.Uniform;
        
        this.Loaded    += (s, e) => { FlyleafLib.Logger.Log("[V] Control Loaded", LogLevel.Debug); StartRendering(); d3dImage.IsFrontBufferAvailableChanged += OnIsFrontBufferAvailableChanged; };
        this.Unloaded  += (s, e) => { FlyleafLib.Logger.Log("[V] Control Unloaded", LogLevel.Debug); StopRendering();  d3dImage.IsFrontBufferAvailableChanged -= OnIsFrontBufferAvailableChanged; };
    }

    private void OnIsFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (d3dImage.IsFrontBufferAvailable) StartRendering();
        else StopRendering();
    }

    private static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (FlyleafD3DImage)d;
        FlyleafLib.Logger.Log($"[V] Player Changed: {e.OldValue != null} -> {e.NewValue != null}", LogLevel.Debug);
        if (e.OldValue is Player oldPlayer)
        {
            oldPlayer.Renderer.D3DImage.IsEnabled = false;
        }

        if (e.NewValue is Player newPlayer)
        {
            newPlayer.Renderer.D3DImage.IsEnabled = true;
            control.StartRendering();
        }
    }

    private void StartRendering()
    {
        if (Player == null) {
            FlyleafLib.Logger.Log("[V] StartRendering skipped: Player is null", LogLevel.Debug);
            return;
        }

        FlyleafLib.Logger.Log("[V] StartRendering", LogLevel.Debug);
        CompositionTarget.Rendering -= OnRendering; // Ensure no double registration
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        CompositionTarget.Rendering -= OnRendering;
    }

    private int renderCount = 0;
    private void OnRendering(object? sender, EventArgs e)
    {
        if (disposed) return;
        var p = Player;
        if (p == null) return;
        
        try
        {
            var renderer = p.Renderer;
            if (renderer == null || renderer.Disposed) return;

            var manager = renderer.D3DImage;
            if (manager == null || !manager.IsEnabled || !d3dImage.IsFrontBufferAvailable) return;

            if (manager.FrameCount == lastFrameCount) return;
            lastFrameCount = manager.FrameCount;

            var control = this;
            int width  = (int)control.ActualWidth;
            int height = (int)control.ActualHeight;

            if (width <= 0 || height <= 0)
            {
                var window = Window.GetWindow(control);
                if (window != null && window.ActualWidth > 0 && window.ActualHeight > 0)
                {
                    width  = (int)window.ActualWidth;
                    height = (int)window.ActualHeight;
                }
                else
                {
                    width  = 1920;
                    height = 1080;
                }
            }

            manager.EnsureSize(width, height);
            
            var surface = manager.SharedSurface9;
            if (surface == null || surface.NativePointer == nint.Zero) return;

            d3dImage.Lock();
            try
            {
                manager.AcquireWPF();
                try
                {
                    d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface.NativePointer);
                    d3dImage.AddDirtyRect(new Int32Rect(0, 0, d3dImage.PixelWidth, d3dImage.PixelHeight));
                }
                finally
                {
                    manager.ReleaseWPF();
                }
            }
            finally
            {
                d3dImage.Unlock();
            }
            
            if (renderCount++ % 300 == 0)
                FlyleafLib.Logger.Log($"[V] OnRendering | {width}x{height} | Status:{p.Status}", LogLevel.Debug);
        }
        catch (ObjectDisposedException) { StopRendering(); }
        catch (Exception ex)
        {
            if (renderCount % 60 == 0) FlyleafLib.Logger.Log($"[V] Error in OnRendering: {ex.Message}", LogLevel.Error);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        StopRendering();
        if (Player != null) Player.Renderer.D3DImage.IsEnabled = false;
        disposed = true;
    }
}
