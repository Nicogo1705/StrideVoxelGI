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

    /// <summary>Toggles voxelizing every clipmap ring each frame. Rebuilds the storage.</summary>
    [DataMember(61)]
    public Keys UpdateAllKey { get; set; } = Keys.T;

    /// <summary>Cycles how radiance is carried down the mipmap chain. Rebuilds the volume.</summary>
    /// <remarks>
    /// On the numpad because the letters are spent, and because the two that were left - W and A -
    /// are the physical keys an AZERTY layout labels Z and Q, which is what this hall's visitor
    /// walks with. The numpad is the same everywhere.
    /// </remarks>
    [DataMember(62)]
    public Keys LightFalloffKey { get; set; } = Keys.NumPad5;

    /// <summary>Cycles voxelization multisampling. Rebuilds the volume.</summary>
    [DataMember(63)]
    public Keys VoxelizationMSAAKey { get; set; } = Keys.NumPad4;

    /// <summary>Cycles the quality preset.</summary>
    [DataMember(70)]
    public Keys CycleQualityKey { get; set; } = Keys.Q;

    /// <summary>Lowers the bounce intensity; hold Shift to raise it.</summary>
    [DataMember(80)]
    public Keys BounceKey { get; set; } = Keys.B;

    /// <summary>Lowers the specular intensity; hold Shift to raise it.</summary>
    [DataMember(90)]
    public Keys SpecularKey { get; set; } = Keys.C;

    /// <summary>Lowers how much bounce is re-injected for the next one; Shift raises it.</summary>
    /// <remarks>
    /// The loop gain, and the only knob that can make this renderer diverge. Voxelization shades
    /// each surface with the previous frame's indirect light and writes it back, so the room feeds
    /// itself; at 1 a closed, bright, saturated room can run away, and it runs away in the colour
    /// of its largest surface - which is why a hall with a red carpet goes red before it goes
    /// white. Bounce raises what you see and the gain at the same time, so a room that blows out
    /// when Bounce goes up is not overexposed, it is diverging: this is the one to bring down.
    /// </remarks>
    [DataMember(90)]
    public Keys SecondBounceKey { get; set; } = Keys.Y;

    /// <summary>Steps the volume's clipmap ring count; hold Shift for more rings.</summary>
    /// <remarks>
    /// The setting behind "why is there no detail eight metres away". Rings buy voxel size, and
    /// they pay for it with the extent of the finest one: five rings over a 96-unit volume leave
    /// 4.7cm voxels inside a box only six metres across, and a sharp cone runs out of that box long
    /// before it runs out of steps. Fewer rings widen the box and coarsen the voxel, and no setting
    /// gives both - which is worth being able to feel rather than read.
    /// </remarks>
    [DataMember(91)]
    public Keys ClipLevelsKey { get; set; } = Keys.U;

    /// <summary>Shrinks the voxelized volume; hold Shift to grow it.</summary>
    [DataMember(91)]
    public Keys VolumeSizeKey { get; set; } = Keys.I;

    /// <summary>Lowers the specular intensity by <see cref="SpecularStep"/>.</summary>
    /// <remarks>
    /// Numpad / and *, so the reflections sit under the same finger as the bounce on - and +. The
    /// two are worth moving together: they are the diffuse and specular halves of the same voxel
    /// data, and a room balanced on one alone reads wrong the moment the other is touched.
    /// </remarks>
    [DataMember(91)]
    public Keys SpecularDownKey { get; set; } = Keys.Divide;

    /// <inheritdoc cref="SpecularDownKey"/>
    [DataMember(91)]
    public Keys SpecularUpKey { get; set; } = Keys.Multiply;

    /// <summary>Steps the mip level shown by <see cref="VoxelGIDebugView.Raw"/>; Shift for up.</summary>
    [DataMember(92)]
    public Keys RawMipKey { get; set; } = Keys.X;

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

    /// <summary>Shortens the reflection horizon; hold Shift to lengthen it.</summary>
    [DataMember(93)]
    public Keys SpecularRangeKey { get; set; } = Keys.J;

    /// <summary>Fewer steps along a reflection; hold Shift for more.</summary>
    [DataMember(93)]
    public Keys SpecularStepsKey { get; set; } = Keys.H;

    /// <summary>Next profiler result page, when the list does not fit on one.</summary>
    [DataMember(95)]
    public Keys ProfilerPageKey { get; set; } = Keys.N;

    /// <summary>Cycles the GI resolution divisor: 1, 2, 4.</summary>
    [DataMember(99)]
    public Keys CycleGIResolutionKey { get; set; } = Keys.R;

    /// <summary>
    /// Cycles the reflection cone's aperture: the tier's own, then 0.5, 0.25, 0.1. It is the one
    /// setting that decides whether the metals in a scene read as metal, and it is worth a look
    /// before concluding that voxel GI cannot do reflections.
    /// </summary>
    [DataMember(99)]
    public Keys CycleSpecularConeKey { get; set; } = Keys.M;

    /// <summary>
    /// Anchors the volume where it stands, and lets it follow again. A volume that follows the
    /// camera re-centres every frame, so its rings re-snap as you walk and the indirect light
    /// visibly swims - right for an open world, wrong for one room.
    /// </summary>
    [DataMember(100)]
    public Keys ToggleFollowKey { get; set; } = Keys.K;

    /// <summary>Steps the clipmap resolution down through 256/128/64/32; Shift steps up. Rebuilds.</summary>
    [DataMember(102)]
    public Keys ClipResolutionKey { get; set; } = Keys.NumPad7;

    /// <summary>Cycles the voxel directionality: isotropic, paired, anisotropic. Rebuilds.</summary>
    [DataMember(103)]
    public Keys DirectionalityKey { get; set; } = Keys.NumPad8;

    /// <summary>Switches between 6 and 12 diffuse cones. Rebuilds.</summary>
    [DataMember(104)]
    public Keys DiffuseConesKey { get; set; } = Keys.NumPad9;

    /// <summary>Fewer steps along each diffuse cone; hold Shift for more. Rebuilds.</summary>
    [DataMember(105)]
    public Keys DiffuseStepsKey { get; set; } = Keys.NumPad6;

    /// <summary>
    /// Lowers the roughness above which the reflection march is skipped; Shift raises it, and at
    /// 1 nothing is skipped. The cheap half of the reflection budget: what the cone costs is set
    /// by its steps, what pays for it is how few surfaces trace one.
    /// </summary>
    [DataMember(106)]
    public Keys SpecularCutoffKey { get; set; } = Keys.NumPad3;

    /// <summary>
    /// Toggles <see cref="VoxelGIVolume.AutoFreeze"/>: voxelize only when something changed,
    /// instead of every frame or never.
    /// </summary>
    [DataMember(107)]
    public Keys AutoFreezeKey { get; set; } = Keys.NumPad0;

    /// <summary>Where screenshots go. Relative paths resolve next to the executable.</summary>
    [DataMember(97)]
    public string ScreenshotDirectory { get; set; } = "Screenshots";

    /// <summary>
    /// Require Ctrl for every hotkey here. A game that walks on WASD or ZQSD has no free letters
    /// left, and a debug overlay must not eat a movement key; with this on, the whole set moves to
    /// Ctrl and nothing collides.
    /// </summary>
    [DataMember(101)]
    public bool RequireControl { get; set; }

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

    /// <summary>How much <see cref="SpecularUpKey"/> and <see cref="SpecularDownKey"/> move.</summary>
    [DataMember(151)]
    public float SpecularStep { get; set; } = 0.25f;

    /// <summary>How much one press moves the voxelization thickening.</summary>
    [DataMember(153)]
    public float OpacifyStep { get; set; } = 0.5f;

    /// <summary>How much one press moves the reflection aperture.</summary>
    [DataMember(154)]
    public float SpecularConeStep { get; set; } = 0.2f;

    /// <summary>How many steps one press adds to or removes from a reflection.</summary>
    [DataMember(155)]
    public int SpecularStepsStep { get; set; } = 64;

    /// <summary>How far, in world units, one press moves the reflection horizon.</summary>
    [DataMember(156)]
    public float SpecularRangeStep { get; set; } = 4f;



    public override void Start()
    {
        Target ??= Entity.Get<VoxelGIVolume>();
        detachedFollow ??= FollowCandidate;
    }

    /// <summary>
    /// -1 for the key alone, +1 with Shift, null when it was not pressed.
    /// </summary>
    /// <remarks>
    /// Every number in this overlay moves by a pair rather than a cycle. A cycle is fine for three
    /// named states and wrong for a value you are searching for: it makes you walk through every
    /// other setting to come back one notch, and it cannot stop between two of them. Shift as the
    /// second half of the pair keeps one letter per setting instead of two.
    /// </remarks>
    private float? Nudge(Keys key)
    {
        if (!Input.IsKeyPressed(key))
            return null;

        return Input.IsKeyDown(Keys.LeftShift) || Input.IsKeyDown(Keys.RightShift) ? 1f : -1f;
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

        // One gate for the whole set, so a binding never half-applies.
        if (RequireControl && !(Input.IsKeyDown(Keys.LeftCtrl) || Input.IsKeyDown(Keys.RightCtrl)))
        {
            DrawOverlay(target);
            return;
        }

        if (Input.IsKeyPressed(ToggleGIKey))
            target.GIEnabled = !target.GIEnabled;

        if (Nudge(CycleViewKey) is { } view)
            target.DebugView = Step(target.DebugView, view);

        if (Input.IsKeyPressed(FreezeKey))
            target.Voxelize = !target.Voxelize;

        if (Input.IsKeyPressed(UpdateAllKey))
            target.UpdateAllClipmapsEveryFrame = !target.UpdateAllClipmapsEveryFrame;

        if (Nudge(LightFalloffKey) is { } falloff)
            target.LightFalloff = Step(target.LightFalloff, falloff);

        if (Nudge(VoxelizationMSAAKey) is { } msaa)
            target.VoxelizationMSAA = Step(target.VoxelizationMSAA, msaa);

        if (Nudge(CycleQualityKey) is { } quality)
            target.Quality = Step(target.Quality, quality);

        // The cones read mip 1 and up, the debug beam reads mip 0 - so an empty mip chain looks
        // like working voxels and black GI. Stepping the level is how you tell them apart.
        if (Nudge(RawMipKey) is { } mip)
        {
            target.DebugMipmap = Math.Max(0, target.DebugMipmap + (int)mip);
            target.RefreshDebugView();
        }

        if (Nudge(CycleOpacifyKey) is { } opacify)
            target.Opacify = MathF.Max(0f, target.Opacify + opacify * OpacifyStep);

        if (Nudge(BounceKey) is { } bounce)
            target.BounceIntensity = MathF.Max(0f, target.BounceIntensity + bounce * BounceStep);

        if (Nudge(SpecularKey) is { } specular)
            target.SpecularIntensity = MathF.Max(0f, target.SpecularIntensity + specular * SpecularStep);

        if (Nudge(SecondBounceKey) is { } second)
            target.SecondBounce = Math.Clamp(target.SecondBounce + second * 0.1f, 0f, 1f);

        if (Nudge(ClipLevelsKey) is { } levels)
            target.ClipMapLevels = Math.Max(1, target.ClipMapLevels + (int)levels);

        if (Nudge(VolumeSizeKey) is { } volume)
            target.VolumeSize = MathF.Max(1f, target.VolumeSize * (volume > 0f ? 1.5f : 1f / 1.5f));

        // Down to zero, which is the unlimited march this all started with - worth being able to
        // reach for a comparison, and worth having to walk to rather than land on.
        if (Nudge(SpecularRangeKey) is { } range)
            target.SpecularRange = MathF.Max(0f, target.EffectiveSpecularRange + range * SpecularRangeStep);

        if (Nudge(SpecularStepsKey) is { } steps)
            target.SpecularSteps = Math.Max(16, target.EffectiveSpecularSteps + (int)steps * SpecularStepsStep);

        if (Input.IsKeyPressed(ScreenshotKey) && (Input.IsKeyDown(Keys.LeftCtrl) || Input.IsKeyDown(Keys.RightCtrl)))
            SaveScreenshot();

        if (Input.IsKeyPressed(ToggleFollowKey))
        {
            (target.Follow, detachedFollow) = (detachedFollow, target.Follow);
            target.MarkDirty();
        }

        if (Nudge(CycleGIResolutionKey) is { } divisor)
            target.GIResolutionDivisor = Math.Clamp(target.EffectiveGIResolutionDivisor + (int)divisor, 1, 4);

        if (Nudge(CycleSpecularConeKey) is { } aperture)
            target.SpecularConeRatio = MathF.Max(0.01f, target.EffectiveSpecularConeRatio + aperture * SpecularConeStep);

        if (Nudge(ClipResolutionKey) is { } resolution)
            target.ClipResolution = Step(target.ClipResolution, resolution);

        if (Nudge(DirectionalityKey) is { } directions)
            target.Directionality = Step(target.Directionality, directions);

        if (Input.IsKeyPressed(DiffuseConesKey))
            target.DiffuseCones = target.DiffuseCones >= 12 ? 6 : 12;

        if (Nudge(DiffuseStepsKey) is { } diffuseSteps)
            target.DiffuseSteps = Math.Max(1, target.DiffuseSteps + (int)diffuseSteps);

        if (Nudge(SpecularCutoffKey) is { } cutoff)
            target.SpecularRoughnessCutoff = Math.Clamp(target.EffectiveSpecularRoughnessCutoff + cutoff * 0.1f, 0.1f, 1f);

        if (Input.IsKeyPressed(AutoFreezeKey))
            target.AutoFreeze = !target.AutoFreeze;

        if (Nudge(CycleProfilerKey) is { } profiler)
            ProfilerPage = Step(profilerPage, profiler);

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

    /// <summary>The next value of an enum in declaration order, wrapping, in either direction.</summary>
    private static T Step<T>(T value, float direction) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var index = Array.IndexOf(values, value);
        return values[(index + (int)direction + values.Length) % values.Length];
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

        // Under RequireControl the bare letter does nothing, so the overlay must not offer it: a
        // readout that names a key you have to guess a modifier for is worse than no readout.
        string Chord(object key) => RequireControl ? $"Ctrl+{key}" : key.ToString()!;

        // Grouped by what you are doing, not by when each was added: the state of the volume, then
        // the light it produces, then the reflections, then the cost, then the instruments. Someone
        // hunting a look reads the middle; someone hunting a bug reads the bottom.
        string Pair(object key) => $"{Chord(key)} +-";

        Print($"[{Chord(ToggleGIKey)}] Voxel GI      : {(target.GIEnabled ? "ON" : "OFF")}");
        Print($"[{Chord(FreezeKey)}] Voxelization  : {(target.Voxelize ? "live" : "frozen")}");
        Print($"[{Chord(AutoFreezeKey)}] Auto-freeze : {(target.AutoFreeze ? (target.IsFrozen ? "on, frozen" : "on, live") : "off")}");
        Print($"[{Chord(UpdateAllKey)}] Ring updates  : {(target.UpdateAllClipmapsEveryFrame ? "all rings, every frame" : "one ring per frame")}");
        Print($"[{Pair(LightFalloffKey)}] Mip falloff: {target.LightFalloff}");
        Print($"[{Pair(VoxelizationMSAAKey)}] Voxel MSAA : {target.VoxelizationMSAA}");
        Print($"[{Chord(ToggleFollowKey)}] Volume        : {(target.Follow is null ? "anchored" : "follows the camera")}");
        Print($"[{Pair(CycleQualityKey)}] Quality    : {target.Quality}");
        Print($"[{Pair(ClipResolutionKey)}] Voxels    : {(int)target.ClipResolution}^3");
        Print($"[{Pair(DirectionalityKey)}] Directions: {target.Directionality}");
        Print($"[{Chord(DiffuseConesKey)}] Diffuse cones : {target.DiffuseCones}");
        Print($"[{Pair(DiffuseStepsKey)}] Diffuse steps : {target.DiffuseSteps}");
        Print($"[{Pair(VolumeSizeKey)}] Volume size: {target.VolumeSize:0.#} units, voxel {target.VoxelSize:0.###}");
        Print($"[{Pair(ClipLevelsKey)}] Clip levels: {target.EffectiveClipMapLevels}/{target.ClipMapLevels}, finest ring {target.VolumeSize / (1 << (target.EffectiveClipMapLevels - 1)):0.#} units{(target.IsFrozen ? ", frozen" : "")}");
        Print("");

        Print($"[{Pair(BounceKey)}] Bounce     : {target.BounceIntensity:0.00}");
        Print($"[{Pair(SpecularKey)}] Specular   : {target.SpecularIntensity:0.00}");
        Print($"[{Pair(SecondBounceKey)}] Re-inject  : {target.SecondBounce:0.00}  (loop gain)");
        Print($"[{Pair(CycleOpacifyKey)}] Opacify    : {target.Opacify:0.0}");
        Print("");

        Print($"[{Pair(CycleSpecularConeKey)}] Reflect cone  : {target.EffectiveSpecularConeRatio:0.00}");
        Print($"[{Pair(SpecularStepsKey)}] Reflect steps : {target.EffectiveSpecularSteps}");
        Print($"[{Pair(SpecularRangeKey)}] Reflect range : {(target.EffectiveSpecularRange > 0f ? $"{target.EffectiveSpecularRange:0.#} units" : "unlimited")}");
        Print($"[{Pair(SpecularCutoffKey)}] Reflect cutoff: {(target.EffectiveSpecularRoughnessCutoff >= 1f ? "off (trace all)" : $"roughness {target.EffectiveSpecularRoughnessCutoff:0.0}")}");
        Print("");

        Print($"[{Pair(CycleGIResolutionKey)}] GI resolution : 1/{target.EffectiveGIResolutionDivisor} of the screen");
        Print("");

        Print($"[{Pair(CycleViewKey)}] Voxel view : {target.DebugView}");
        Print($"[{Pair(RawMipKey)}] Raw mip    : {target.DebugMipmap}{(target.DebugView == VoxelGIDebugView.Raw ? "" : "  (raw view only)")}");
        Print($"[{Pair(CycleProfilerKey)}] Profiler   : {profilerPage}");
        Print($"[{Chord(ProfilerPageKey)}] Profiler page : {(profilerPage == VoxelGIProfilerPage.Off ? "- (profiler off)" : "next, +Shift first")}");
        // A host that saves screenshots itself sets the key to None, and the line goes with it.
        if (ScreenshotKey != Keys.None)
            Print($"[Ctrl+{ScreenshotKey}] Screenshot  : {screenshotStatus}");
        Print("");
        Print("    +- : the key alone lowers it, with Shift raises it");
    }

    /// <summary>
    /// What <see cref="ToggleFollowKey"/> attaches the volume to when the scene starts it anchored.
    /// </summary>
    /// <remarks>
    /// The toggle swaps Follow with what it is holding, so a volume that begins with Follow null
    /// has nothing to swap in and the key silently does nothing. An interior that fits inside its
    /// volume wants to start anchored - no movement means no per-ring snapping and no wobble - so
    /// the camera transform has to arrive by another route for the comparison to stay available.
    /// </remarks>
    [DataMember(5)]
    public TransformComponent? FollowCandidate { get; set; }

    private TransformComponent? detachedFollow;

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
