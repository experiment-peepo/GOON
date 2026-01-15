using SharpGen.Runtime;
using Vortice.DXGI;

using ResultCode = Vortice.DXGI.ResultCode;

using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    long            renderRequestAt, lastRenderAt;
    volatile bool   canIdle;
    volatile bool   isIdleRunning;
    internal object lockRenderLoops = new();

    internal void RenderRequest(VideoFrame frame = null, bool forceClear = false)
    {
        lock (lockRenderLoops)
        {
            renderRequestAt = DateTime.UtcNow.Ticks;

            if ((frame != null || forceClear))
                Frames.SetRendererFrame(frame);

            if (!SwapChain.CanPresent || !canIdle || isIdleRunning)
                return;

            isIdleRunning = true;
        }

        Task.Run(RenderIdleLoop);
    }
    internal void RenderIdleStart(bool force = false)
    {
        lock (lockRenderLoops)
        {
            canIdle = true;
            if (force)
                renderRequestAt = DateTime.UtcNow.Ticks;

            // TBR: Check if last timestamp?* to start idle
            if (renderRequestAt > lastRenderAt)
                RenderRequest();
        }
    }
    internal void RenderIdleStop()
    {
        canIdle = false;
        while (isIdleRunning)
            { canIdle = false; Thread.Sleep(1); }
    }
    void RenderIdleLoop()
    {
        int rechecks = 1000; // Awake for ~5sec when Idle
        while (SwapChain.CanPresent)
        {
            while (renderRequestAt <= lastRenderAt && rechecks-- > 0)
            {
                if (!canIdle || !SwapChain.CanPresent)
                    { rechecks = 0; break; }

                Thread.Sleep(5); // might not TimeBeginPeriod1 (can drop fps or slow down cancelation)
            }

            if (rechecks < 1)
                break;

            rechecks = 1000;
            RenderIdle();
        }

        lock (lockRenderLoops) // To avoid race condition*?
        {
            isIdleRunning = false;
            if (renderRequestAt > lastRenderAt && canIdle && SwapChain.CanPresent)
                RenderRequest();
        }
    }
    bool RenderIdle()
    {
        try
        {
            lastRenderAt = DateTime.UtcNow.Ticks;

            if (!SwapChain.CanPresent)
                return true;

            lock (lockRenderLoops)
            {
                bool acquired = false;
                if (D3DImage != null && D3DImage.IsEnabled) { D3DImage.Acquire(); acquired = true; }

                try
                {
                    bool needsClear = true;
                    if (VideoProcessor == VideoProcessors.D3D11)
                    {
                        D3ProcessRequests();

                        if (VideoProcessor != VideoProcessors.D3D11)
                        {
                            return RenderIdle();
                        }

                        if (!d3CanPresent)
                        {
                            return true;
                        }

                        if (Frames.RendererFrame != null)
                            { D3Render(Frames.RendererFrame, false); needsClear = false; }
                    }
                    else
                    {
                        FLProcessRequests();

                        if (VideoProcessor == VideoProcessors.D3D11)
                        {
                            return RenderIdle();
                        }

                    
                        if (Frames.RendererFrame != null)
                            { FLRender(Frames.RendererFrame); needsClear = false; }
                    }

                    if (needsClear)
                    {
                        if (!Config.Video.ClearScreen)
                        {
                            return true;
                        }

                        //SubsDispose();
                        context.OMSetRenderTargets(CurrentRtv);
                        context.ClearRenderTargetView(CurrentRtv, ucfg.flBackColor);
                    }

                    if (acquired)
                    {
                        context.Flush();
                        return true;
                    }
                }
                finally
                {
                    if (acquired) D3DImage.Release();
                }
            }

            SwapChain.Present(1, PresentFlags.None);

            return true;
        }
        catch (SharpGenException e)
        {
            Log.Error($"[RenderIdle] Device Lost ({e.ResultCode.NativeApiCode} ({e.ResultCode}) | {device.DeviceRemovedReason} | {e.Message})");
            ResetLocal();

            return false;
        }
        catch (Exception e)
        {
            Log.Error($"[RenderIdle] Failed ({e.Message})");

            return false;
        }
    }

    internal bool RefreshPlay(bool secondField) // TODO secondfield embedded*
    {   // Tries to keep ~60fps refreshes within/during playback
        if (lastRenderAt >= renderRequestAt)
            return false;

        RenderIdle();

        return true;
    }
    internal bool RenderPlay(VideoFrame frame, bool secondField)
    {
        try
        {
            lastRenderAt = DateTime.UtcNow.Ticks;

            if (!SwapChain.CanPresent)
                return true;

            lock (lockRenderLoops)
            {
                bool acquired = false;
                long startTicks = 0;
                if (D3DImage != null && D3DImage.IsEnabled) 
                { 
                    startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                    D3DImage.Acquire(); 
                    acquired = true; 
                }

                try
                {
                    if (VideoProcessor == VideoProcessors.D3D11)
                    {
                        D3ProcessRequests();

                        if (VideoProcessor != VideoProcessors.D3D11)
                        {
                            return RenderPlay(frame, secondField);
                        }

                        if (!d3CanPresent)
                        {
                            return true;
                        }

                        D3Render(frame, secondField);
                    }
                    else
                    {
                        FLProcessRequests();

                        if (VideoProcessor == VideoProcessors.D3D11)
                        {
                            return RenderPlay(frame, secondField);
                        }

                        FLRender(frame);
                    }

                    Frames.SetRendererFrame(frame);
                    if (acquired)
                    {
                        context.Flush();
                        D3DImage.FrameCount++;
                        player.Video.D3DImageLatencyMs = (double)(System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    }
                }
                finally
                {
                    if (acquired) D3DImage.Release();
                }
            }

            return true;
        }
        catch (SharpGenException e)
        {
            Log.Error($"[RenderPlay] Device Lost ({e.ResultCode.NativeApiCode} ({e.ResultCode}) | {device.DeviceRemovedReason} | {e.Message})");
            ResetLocal(pausePlayer: false);

            return false;
        }
        catch (Exception e)
        {
            Log.Error($"[RenderPlay] Failed ({e.Message})");

            return false;
        }

    }
    internal bool PresentPlay()
    {
        try
        {
            if (D3DImage != null && D3DImage.IsEnabled)
                return true;

            if (SwapChain.CanPresent) // TODO: dont present if we didnt render (or d3d11 check can present too)
                SwapChain.Present().CheckError();

            return true;
        }
        catch (SharpGenException e)
        {
            if (e.ResultCode == ResultCode.WasStillDrawing) // For DoNotWait (any reason to still support it with Config?)
            {
                Log.Info($"[V] Frame Dropped (GPU)");
                return false;
            }

            Log.Error($"[PresentPlay] {e.ResultCode.NativeApiCode} ({e.ResultCode}) | {device.DeviceRemovedReason} | {e.Message}");
            ResetLocal(pausePlayer: false);

            return false;
        }
        catch (Exception e)
        {
            Log.Error($"[PresentPlay] Failed ({e.Message})");
            throw; // Force Playback Stop
        }
    }

    public void ClearScreen(bool force = false, bool rendererFrame = true)
    {
        if (force)
        {
            lock (lockDevice)
            {
                if (SwapChain.Disposed)
                    return;

                bool acquired = false;
                if (D3DImage != null && D3DImage.IsEnabled) { D3DImage.Acquire(); acquired = true; }

                try
                {
                    if (rendererFrame)
                        Frames.SetRendererFrame(null);
                    SubsDispose();
                    context.OMSetRenderTargets(CurrentRtv);
                    context.ClearRenderTargetView(CurrentRtv, ucfg.flBackColor);
                    if (acquired)
                    {
                        context.Flush();
                        return;
                    }
                }
                finally
                {
                    if (acquired) D3DImage.Release();
                }

                SwapChain.Present(1, PresentFlags.None);
            }
        }
        else if (Config.Video.ClearScreen)
            RenderRequest(null, true);
    }
}
