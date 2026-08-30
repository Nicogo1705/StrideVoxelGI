// Copyright (c) 2026 Nicogo. Distributed under the MIT license.
using System;
using System.IO;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Input;
using Stride.Profiling;

namespace StrideVoxelGI;

/// <summary>
/// A page of Stride's built-in profiler, as cycled by <see cref="VoxelGIDebug.CycleProfilerKey"/>.
/// </summary>
public enum VoxelGIProfilerPage
{
    /// <summary>The profiler is off and the debug overlay has the corner to itself.</summary>
    Off,

    /// <summary>Frame rate only.</summary>
    Fps,

    /// <summary>CPU events.</summary>
    Cpu,

    /// <summary>GPU events - what the voxel passes actually cost on this machine.</summary>
    Gpu,
}

/// <summary>
/// Hotkeys and an on-screen readout for a <see cref="VoxelGIVolume"/>. Drop it next to the volume
/// (or point <see cref="Target"/> at one) to get the before/after toggle, the voxel views and the
/// quality tiers without opening the property grid.
/// <para>
/// It is a debug tool, not part of the renderer: delete it from your scene and the GI is unchanged.
/// </para>
/// </summary>
[Display("Voxel GI Debug", Expand = ExpandRule.Once)]
[ComponentCategory("Lights")]
public class VoxelGIDebug : SyncScript
{
    /// <summary>The volume to drive. Defaults to one on this entity.</summary>
    [DataMember(10)]
    public VoxelGIVolume? Target { get; set; }

    /// <summary>Draw the readout in the corner of the screen.</summary>
    [DataMember(20)]
    public bool ShowOverlay { get; set; } = true;

    /// <summary>Top-left corner of the readout, in pixels.</summary>
    [DataMember(30)]
    public Int2 OverlayPosition { get; set; } = new Int2(16, 16);

    /// <summary>Toggles the indirect light on and off.</summary>
    [DataMember(40)]
    public Keys ToggleGIKey { get; set; } = Keys.G;

    /// <summary>Cycles off / ray-marched voxels / raw storage slice.</summary>
    [DataMember(50)]
    public Keys CycleViewKey { get; set; } = Keys.V;

    /// <summary>Freezes and unfreezes voxelization.</summary>
    [DataMember(60)]
    public Keys FreezeKey { get; set; } = Keys.F;

    /// <summary>Cycles the quality preset.</summary>
    [DataMember(70)]
    public Keys CycleQualityKey { get; set; } = Keys.Q;

    /// <summary>Lowers the bounce intensity by <see cref="BounceStep"/>.</summary>
    [DataMember(80)]
    public Keys BounceDownKey { get; set; } = Keys.Subtract;

    /// <summary>Raises the bounce intensity by <see cref="BounceStep"/>.</summary>
    [DataMember(90)]
    public Keys BounceUpKey { get; set; } = Keys.Add;

    /// <summary>Steps the mip level shown by <see cref="VoxelGIDebugView.Raw"/> down and up.</summary>
    [DataMember(92)]
    public Keys MipDownKey { get; set; } = Keys.PageDown;

    /// <inheritdoc cref="MipDownKey"/>
    [DataMember(93)]
    public Keys MipUpKey { get; set; } = Keys.PageUp;

    /// <summary>Cycles the voxelization thickening: 0, 1, 2, 4.</summary>
    [DataMember(95)]
    public Keys CycleOpacifyKey { get; set; } = Keys.O;

    /// <summary>Saves a PNG of the frame next to the executable. Hold Ctrl.</summary>
    [DataMember(96)]
    public Keys ScreenshotKey { get; set; } = Keys.S;

    /// <summary>
    /// Cycles Stride's built-in profiler: off, FPS, CPU events, GPU events. The GPU page is the
    /// one that tells you what the voxel passes actually cost on this machine - guesswork about an
    /// optimization is worth less than one glance at it.
    /// </summary>
    [DataMember(94)]
    public Keys CycleProfilerKey { get; set; } = Keys.P;

    /// <summary>Next profiler result page, when the list does not fit on one.</summary>
    [DataMember(95)]
    public Keys ProfilerPageKey { get; set; } = Keys.N;

    /// <summary>Cycles the GI resolution divisor: 1, 2, 4.</summary>
    [DataMember(99)]
    public Keys CycleGIResolutionKey { get; set; } = Keys.R;

    /// <summary>Where screenshots go. Relative paths resolve next to the executable.</summary>
    [DataMember(97)]
    public string ScreenshotDirectory { get; set; } = "Screenshots";

    /// <summary>
    /// Ignore the hotkeys while the right mouse button is held. Fly-through cameras claim the letter
    /// keys for movement under that button, and several of them are hotkeys here too - strafing left
    /// would otherwise also cycle the quality preset, on every AZERTY keyboard and on any QWERTY one
    /// bound to ZQSD. Turn it off if your camera does not work that way.
    /// </summary>
    [DataMember(98)]
    public bool SuspendWhileLookingAround { get; set; } = true;

    /// <summary>How much one bounce key press moves <see cref="VoxelGIVolume.BounceIntensity"/>.</summary>
    [DataMember(100)]
    public float BounceStep { get; set; } = 0.25f;

    public override void Start()
    {
        Target ??= Entity.Get<VoxelGIVolume>();
    }

    public override void Update()
    {
        var target = Target;
        if (target == null)
            return;

        // The camera owns the keyboard while the look button is held; leave its keys alone and only
        // draw the readout.
        if (SuspendWhileLookingAround && Input.HasMouse && Input.IsMouseButtonDown(MouseButton.Right))
        {
            DrawOverlay(target);
            return;
        }

        if (Input.IsKeyPressed(ToggleGIKey))
            target.GIEnabled = !target.GIEnabled;

        if (Input.IsKeyPressed(CycleViewKey))
            target.DebugView = target.DebugView switch
            {
                VoxelGIDebugView.Off => VoxelGIDebugView.Cones,
                VoxelGIDebugView.Cones => VoxelGIDebugView.Raw,
                _ => VoxelGIDebugView.Off,
            };

        if (Input.IsKeyPressed(FreezeKey))
            target.Voxelize = !target.Voxelize;

        if (Input.IsKeyPressed(CycleQualityKey))
            target.Quality = target.Quality switch
            {
                VoxelGIQuality.Low => VoxelGIQuality.Medium,
                VoxelGIQuality.Medium => VoxelGIQuality.High,
                VoxelGIQuality.High => VoxelGIQuality.Ultra,
                _ => VoxelGIQuality.Low,
            };

        // The cones read mip 1 and up, the debug beam reads mip 0 - so an empty mip chain looks
        // like working voxels and black GI. Stepping the level is how you tell them apart.
        if (Input.IsKeyPressed(MipDownKey))
        {
            target.DebugMipmap = Math.Max(0, target.DebugMipmap - 1);
            target.RefreshDebugView();
        }

        if (Input.IsKeyPressed(MipUpKey))
        {
            target.DebugMipmap++;
            target.RefreshDebugView();
        }

        if (Input.IsKeyPressed(CycleOpacifyKey))
            target.Opacify = target.Opacify switch { < 0.5f => 1f, < 1.5f => 2f, < 3f => 4f, _ => 0f };

        if (Input.IsKeyPressed(BounceDownKey))
            target.BounceIntensity = MathF.Max(0f, target.BounceIntensity - BounceStep);

        if (Input.IsKeyPressed(BounceUpKey))
            target.BounceIntensity += BounceStep;

        if (Input.IsKeyPressed(ScreenshotKey) && (Input.IsKeyDown(Keys.LeftCtrl) || Input.IsKeyDown(Keys.RightCtrl)))
            SaveScreenshot();

        if (Input.IsKeyPressed(CycleGIResolutionKey))
            target.GIResolutionDivisor = target.EffectiveGIResolutionDivisor switch
            {
                1 => 2,
                2 => 4,
                _ => 1,
            };

        if (Input.IsKeyPressed(CycleProfilerKey))
            CycleProfiler();

        // Shift+N goes back to the first page; the engine clamps to the last one on its own.
        if (profilerPage != VoxelGIProfilerPage.Off && Input.IsKeyPressed(ProfilerPageKey))
            GameProfiler.CurrentResultPage = Input.IsKeyDown(Keys.LeftShift) || Input.IsKeyDown(Keys.RightShift)
                ? 1
                : GameProfiler.CurrentResultPage + 1;

        DrawOverlay(target);
    }

    /// <summary>
    /// The profiler page on screen. Assigning it opens or closes the profiler exactly as the
    /// hotkey does - which is the only way in for anything driving the demo from outside, since
    /// synthesized key presses do not reach Stride's input.
    /// </summary>
    [DataMemberIgnore]
    public VoxelGIProfilerPage ProfilerPage
    {
        get => profilerPage;
        set
        {
            profilerPage = value;
            ApplyProfilerPage();
        }
    }

    private VoxelGIProfilerPage profilerPage = VoxelGIProfilerPage.Off;

    private void CycleProfiler()
    {
        ProfilerPage = profilerPage switch
        {
            VoxelGIProfilerPage.Off => VoxelGIProfilerPage.Fps,
            VoxelGIProfilerPage.Fps => VoxelGIProfilerPage.Cpu,
            VoxelGIProfilerPage.Cpu => VoxelGIProfilerPage.Gpu,
            _ => VoxelGIProfilerPage.Off,
        };
    }

    private void ApplyProfilerPage()
    {
        if (profilerPage == VoxelGIProfilerPage.Off)
        {
            GameProfiler.DisableProfiling();
            return;
        }

        GameProfiler.EnableProfiling();
        GameProfiler.CurrentResultPage = 1;
        GameProfiler.FilteringMode = profilerPage switch
        {
            VoxelGIProfilerPage.Cpu => GameProfilingResults.CpuEvents,
            VoxelGIProfilerPage.Gpu => GameProfilingResults.GpuEvents,
            _ => GameProfilingResults.Fps,
        };
    }

    private void DrawOverlay(VoxelGIVolume target)
    {
        if (!ShowOverlay)
            return;

        // The profiler draws its report in the same corner; two overlapping walls of text help
        // no one. While it is up, yield the screen - P still cycles it, N pages through it.
        if (profilerPage != VoxelGIProfilerPage.Off)
            return;

        var line = OverlayPosition;
        void Print(string text)
        {
            DebugText.Print(text, line);
            line.Y += 18;
        }

        Print($"[{ToggleGIKey}] Voxel GI      : {(target.GIEnabled ? "ON" : "OFF")}");
        Print($"[{CycleViewKey}] Voxel view    : {target.DebugView}");
        Print($"[{FreezeKey}] Voxelization  : {(target.Voxelize ? "live" : "frozen")}");
        Print($"[{CycleQualityKey}] Quality       : {target.Quality} ({target.Preset.Resolution}^3, {target.Preset.DiffuseCones} cones)");
        Print($"[-/+ numpad] Bounce   : {target.BounceIntensity:0.00}");
        Print($"[{CycleOpacifyKey}] Opacify       : {target.Opacify:0.0}");
        Print($"[PgDn/PgUp] Raw mip    : {target.DebugMipmap}");
        Print($"[Ctrl+{ScreenshotKey}] Screenshot  : {screenshotStatus}");
        Print($"[{CycleGIResolutionKey}] GI resolution : 1/{target.EffectiveGIResolutionDivisor} of the screen");
        Print($"[{CycleProfilerKey}] Profiler      : {profilerPage}");
        Print($"    Volume        : {target.VolumeSize:0.#} units, voxel {target.VoxelSize:0.###}, {target.EffectiveClipMapLevels}/{target.ClipMapLevels} clip level(s){(target.IsFrozen ? ", frozen" : "")}");
    }

    private string screenshotStatus = "ready";

    private void SaveScreenshot() => CaptureScreenshot();

    /// <summary>
    /// Writes the back buffer to a PNG and returns the path, or null if it could not be written.
    /// This is the frame the GPU last presented, so it carries the overlay too - capture with
    /// <see cref="ShowOverlay"/> off for a clean image.
    /// </summary>
    /// <param name="fileName">
    /// Name of the file inside <see cref="ScreenshotDirectory"/>. Defaults to a timestamp.
    /// </param>
    public string? CaptureScreenshot(string? fileName = null)
    {
        try
        {
            var directory = Path.IsPathRooted(ScreenshotDirectory)
                ? ScreenshotDirectory
                : Path.Combine(AppContext.BaseDirectory, ScreenshotDirectory);
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, fileName ?? $"voxelgi-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");

            using var image = GraphicsDevice.Presenter.BackBuffer.GetDataAsImage(Game.GraphicsContext.CommandList);

            // The back buffer's alpha is whatever the last pass happened to leave there, usually
            // zero. PNG keeps it, so the capture opens as a near-transparent, washed-out image once
            // a viewer composites it over white. Nothing on screen is translucent - force it opaque.
            var pixels = image.PixelBuffer[0];
            if (pixels.PixelSize == 4)
            {
                var bytes = pixels.GetPixels<byte>();
                for (int i = 3; i < bytes.Length; i += 4)
                    bytes[i] = byte.MaxValue;
                pixels.SetPixels(bytes);
            }

            using (var stream = File.Create(path))
                image.Save(stream, ImageFileType.Png);

            screenshotStatus = Path.GetFileName(path);
            return path;
        }
        catch (Exception e)
        {
            // A failed capture is not worth taking the demo down for; say so in the overlay.
            screenshotStatus = $"failed - {e.Message}";
            return null;
        }
    }
}
