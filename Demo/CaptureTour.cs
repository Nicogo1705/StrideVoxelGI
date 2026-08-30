using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering.Voxels.VoxelGI;
using StrideVoxelGI;

namespace Demo;

/// <summary>
/// Drives the demo with no keyboard: opens the profiler, walks the camera around the voxel volume
/// and writes a PNG at each stop, then closes the game.
/// </summary>
/// <remarks>
/// Synthesized key presses (SendKeys, keybd_event) do not reach Stride's input, so a capture pass
/// running the demo from outside cannot press P or Ctrl+S. This is the way in: enabled by
/// <c>--capture</c> / <c>--profiler</c> on the command line, absent otherwise, and the demo behaves
/// exactly as before without them.
/// </remarks>
public class CaptureTour : SyncScript
{
    /// <summary>Profiler page to open before the first shot.</summary>
    public VoxelGIProfilerPage Profiler { get; set; } = VoxelGIProfilerPage.Off;

    /// <summary>
    /// Walk the camera and capture. Off leaves the camera where the scene put it and only opens the
    /// profiler, which is what you want when a human is going to fly around.
    /// </summary>
    public bool Tour { get; set; } = true;

    /// <summary>How many stops to visit, out of the ones the tour defines.</summary>
    public int Shots { get; set; } = 5;

    /// <summary>
    /// Frames to hold at each stop before capturing. The clipmaps revoxelize one level per frame
    /// after a move, so the image needs a few of them to settle.
    /// </summary>
    public int SettleFrames { get; set; } = 90;

    /// <summary>
    /// Seconds to hold at each stop, on top of <see cref="SettleFrames"/>. The profiler rebuilds
    /// its report on a timer (about twice a second) and enabling it turns VSync off, so a fast
    /// machine burns the frame budget long before the first report exists - a stop captured on the
    /// frame count alone shows an empty table.
    /// </summary>
    public float SettleSeconds { get; set; } = 2.5f;

    /// <summary>Where the PNGs go. Empty keeps the debug component's own screenshot directory.</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>Close the game once the last shot is written.</summary>
    public bool ExitWhenDone { get; set; } = true;

    /// <summary>
    /// Switch to a different quality preset at every stop instead of moving the camera, so the tiers
    /// can be compared from one viewpoint - and so the rebuild each switch triggers is exercised.
    /// </summary>
    public bool CycleQuality { get; set; }

    /// <summary>Clipmap rings to ask for before capturing. Zero keeps the scene's.</summary>
    public int ClipMapLevels { get; set; }

    /// <summary>Resize the volume before capturing, in world units. Zero keeps the scene's.</summary>
    public float VolumeSize { get; set; }

    /// <summary>Turn the indirect light off before capturing, to tell it apart from direct light.</summary>
    public bool DisableGI { get; set; }

    /// <summary>Debug view to switch to before capturing: off, cones or raw voxels.</summary>
    public VoxelGIDebugView? View { get; set; }

    /// <summary>Ask RenderDoc to capture the frame each stop is shot on, when it is hosting us.</summary>
    public bool RenderDoc { get; set; }

    /// <summary>
    /// Save what the reduced-resolution GI pass wrote, next to each screenshot. It is the buffer the
    /// shaded pixels read, so it is where to look first when the image has artefacts the full-rate
    /// path does not.
    /// </summary>
    public bool DumpGIBuffer { get; set; }

    /// <summary>
    /// Seconds to let the game settle before the first stop is even set up. The first frames are
    /// not the game running: effect permutations are still compiling, the clipmaps have not been
    /// filled, and the profiler has no report yet. Capturing into that measures the loading screen.
    /// </summary>
    public float WarmupSeconds { get; set; } = 5f;

    /// <summary>
    /// Distance ahead of the camera that the tour treats as its subject. Zero uses the centre of
    /// the scene's drawn geometry instead, which is what keeps two runs comparable.
    /// </summary>
    public float PivotDistance { get; set; }

    /// <summary>
    /// Quality preset to switch the volume to before the first shot. Null keeps whatever the scene
    /// saved - which is what a capture of the scene as shipped wants.
    /// </summary>
    public VoxelGIQuality? Quality { get; set; }

    /// <summary>
    /// Resolution divisor for the diffuse cones, or zero to keep the preset's. Set it to compare
    /// the same stops with the cones traced at full, quarter and sixteenth pixel count.
    /// </summary>
    public int GIResolutionDivisor { get; set; }

    private readonly List<(string Name, Vector3 Position, Quaternion Rotation)> stops = new();
    private VoxelGIDebug? debug;
    private int stop;
    private int frames;
    private float seconds;
    private float warmup;
    private bool ready;
    private bool done;

    public override void Start()
    {
        debug = FindInScene<VoxelGIDebug>();

        if (debug is not null && !string.IsNullOrWhiteSpace(OutputDirectory))
            debug.ScreenshotDirectory = OutputDirectory;
    }

    public override void Update()
    {
        if (done)
            return;

        // The first frames are too early twice over: the scene's transforms are still identity, so
        // the stops would be built around the wrong point, and the engine is still compiling what
        // it needs to draw the first real frame.
        if (!ready)
        {
            warmup += (float)Game.UpdateTime.Elapsed.TotalSeconds;
            if (warmup < WarmupSeconds || frames++ < 30)
                return;

            frames = 0;
            ready = true;

            if ((Quality is not null || GIResolutionDivisor > 0 || DisableGI || View is not null || VolumeSize > 0 || ClipMapLevels > 0)
                && FindInScene<VoxelGIVolume>() is { } volume)
            {
                if (Quality is { } quality)
                    volume.Quality = quality;
                if (GIResolutionDivisor > 0)
                    volume.GIResolutionDivisor = GIResolutionDivisor;
                if (VolumeSize > 0)
                    volume.VolumeSize = VolumeSize;
                if (ClipMapLevels > 0)
                    volume.ClipMapLevels = ClipMapLevels;
                if (DisableGI)
                    volume.GIEnabled = false;
                if (View is { } view)
                    volume.DebugView = view;

                Log.Info($"quality: {volume.Quality}, GI divisor: {volume.EffectiveGIResolutionDivisor}, volume {volume.VolumeSize:0.#}, voxel {volume.VoxelSize:0.####}, {volume.EffectiveClipMapLevels}/{volume.ClipMapLevels} clip levels");
            }

            if (debug is not null)
                debug.ProfilerPage = Profiler;
            if (Tour)
                BuildStops();
            return;
        }

        if (!Tour || stops.Count == 0)
        {
            done = true;
            return;
        }

        // On arrival, before the hold: a preset change rebuilds the volume and revoxelizes, and the
        // frame on screen is still the previous one - switching at capture time photographs the
        // stop before this one.
        if (frames == 0 && CycleQuality && stop > 0 && FindInScene<VoxelGIVolume>() is { } tierVolume
            && Enum.TryParse<VoxelGIQuality>(stops[stop].Name, true, out var tier))
        {
            tierVolume.Quality = tier;
            Log.Info($"quality: {tier}, voxel {tierVolume.VoxelSize:0.###}, {tierVolume.EffectiveClipMapLevels}/{tierVolume.ClipMapLevels} clip levels");
        }

        seconds += (float)Game.UpdateTime.Elapsed.TotalSeconds;

        if (frames++ < SettleFrames || seconds < SettleSeconds)
        {
            // Hold the stop, and keep the pose: nothing else moves the camera, but a stop that
            // drifted would be captured mid-move.
            var (_, position, rotation) = stops[stop];
            Entity.Transform.Position = position;
            Entity.Transform.Rotation = rotation;
            return;
        }

        Capture(stops[stop].Name);
        frames = 0;
        seconds = 0;
        stop++;

        if (stop < stops.Count)
            return;

        done = true;

        // IGame, which the script sees, does not carry Exit.
        if (ExitWhenDone && Game is GameBase game)
            game.Exit();
    }

    private void Capture(string name)
    {
        if (RenderDoc && RenderDocCapture.TriggerNextFrame())
            Log.Info($"renderdoc: capturing the frame for {name}");

        var fileName = $"tour-{stop + 1}-{name}.png";
        var path = debug?.CaptureScreenshot(fileName);

        // Printed so whatever launched the demo can pick the files up without guessing names.
        Log.Info(path is null ? $"capture failed: {fileName}" : $"capture: {path}");

        if (DumpGIBuffer)
            SaveGIBuffer($"tour-{stop + 1}-{name}-gi.png");
    }

    /// <summary>
    /// Writes the GI pass's own buffer out as a PNG. Its colour is HDR radiance and its alpha a
    /// depth, so neither survives a straight save: the radiance is tone mapped to something the eye
    /// can read and the alpha dropped.
    /// </summary>
    private void SaveGIBuffer(string fileName)
    {
        var state = SceneSystem.SceneInstance?.VisibilityGroups?.FirstOrDefault()?.Tags.Get(VoxelGIResolver.Current);
        if (state?.Texture is not { } texture)
        {
            Log.Info("gi buffer: nothing to dump, the pass has not run");
            return;
        }

        try
        {
            using var source = texture.GetDataAsImage(Game.GraphicsContext.CommandList);
            var pixels = source.PixelBuffer[0];
            var halves = pixels.GetPixels<System.Half>();

            using var output = Image.New2D(texture.Width, texture.Height, 1, PixelFormat.R8G8B8A8_UNorm);
            var bytes = output.PixelBuffer[0].GetPixels<byte>();

            for (var i = 0; i < texture.Width * texture.Height; i++)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    // Reinhard, so a bright bounce stays distinguishable from a blown-out one.
                    var value = (float)halves[i * 4 + channel];
                    value = value / (1.0f + value);
                    bytes[i * 4 + channel] = (byte)MathUtil.Clamp(value * 255.0f, 0, 255);
                }

                bytes[i * 4 + 3] = byte.MaxValue;
            }

            output.PixelBuffer[0].SetPixels(bytes);

            var directory = System.IO.Path.Combine(AppContext.BaseDirectory, debug?.ScreenshotDirectory ?? "Screenshots");
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, fileName);

            using (var stream = System.IO.File.Create(path))
                output.Save(stream, ImageFileType.Png);

            Log.Info($"gi buffer: {path} ({texture.Width}x{texture.Height})");
        }
        catch (Exception e)
        {
            Log.Info($"gi buffer: failed - {e.Message}");
        }
    }

    /// <summary>
    /// Builds the stops from where the scene put the camera: the first is that view untouched, the
    /// rest turn and step around what it is looking at. Anchoring on the scene's own framing means
    /// the tour follows the camera when the scene changes, instead of hard-coding coordinates that
    /// go stale.
    /// </summary>
    /// <remarks>
    /// The subject is the bounds of what the scene actually draws. Neither the volume's entity nor
    /// its size will do: a volume set to follow the camera sits *on* it, and its clipmap extent
    /// changes with the quality preset - which would move the stops between two runs meant to be
    /// compared, and did, dropping a camera inside a wall on Ultra. The stops also stay inside the
    /// room: a Cornell box has no outside worth photographing.
    /// </remarks>
    private void BuildStops()
    {
        var eye = Entity.Transform.WorldMatrix.TranslationVector;
        var rotation = Entity.Transform.Rotation;
        var forward = Vector3.TransformNormal(-Vector3.UnitZ, Matrix.RotationQuaternion(rotation));

        stops.Add(("scene", eye, rotation));

        // Comparing tiers means holding the camera still and changing only the preset.
        if (CycleQuality)
        {
            foreach (var tier in new[] { VoxelGIQuality.Low, VoxelGIQuality.Medium, VoxelGIQuality.High, VoxelGIQuality.Ultra })
                stops.Add((tier.ToString().ToLowerInvariant(), eye, rotation));

            return;
        }

        var (centre, _) = SceneBounds();
        var pivot = PivotDistance > 0 ? eye + forward * PivotDistance : centre;

        // Turning in place: each wall colours the bounce differently, and that is what there is to
        // look at here. In place is also the one move that cannot end up inside something.
        stops.Add(("yaw-left", eye, rotation * Quaternion.RotationAxis(Vector3.UnitY, -MathUtil.DegreesToRadians(35))));
        stops.Add(("yaw-right", eye, rotation * Quaternion.RotationAxis(Vector3.UnitY, MathUtil.DegreesToRadians(35))));

        // A short step in, not a long one: the tall block stands between the camera and the centre
        // of the room, and 40% of the way there puts the lens against it.
        var close = Vector3.Lerp(eye, pivot, 0.22f);
        stops.Add(("close", close, rotation));

        // Tilting down, not rising: a step up scaled from the scene's bounds clears the ceiling,
        // and the roof of a Cornell box is not the subject. In place cannot leave the room.
        var right = Vector3.TransformNormal(Vector3.UnitX, Matrix.RotationQuaternion(rotation));
        stops.Add(("pitch-down", eye, rotation * Quaternion.RotationAxis(right, -MathUtil.DegreesToRadians(25))));

        Trim();
    }

    /// <summary>
    /// World-space bounds of everything the scene draws, as a centre and a radius. Read off the
    /// models rather than the render system, which has not filled in its own bounds this early.
    /// </summary>
    private (Vector3 Centre, float Radius) SceneBounds()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var any = false;

        foreach (var model in FindAllInScene<ModelComponent>())
        {
            if (model.Model is null)
                continue;

            var box = model.Model.BoundingBox;
            var world = model.Entity.Transform.WorldMatrix;

            for (var corner = 0; corner < 8; corner++)
            {
                var local = new Vector3(
                    (corner & 1) == 0 ? box.Minimum.X : box.Maximum.X,
                    (corner & 2) == 0 ? box.Minimum.Y : box.Maximum.Y,
                    (corner & 4) == 0 ? box.Minimum.Z : box.Maximum.Z);

                var point = Vector3.TransformCoordinate(local, world);
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
                any = true;
            }
        }

        if (!any)
            return (Entity.Transform.WorldMatrix.TranslationVector, 10f);

        return ((min + max) * 0.5f, (max - min).Length() * 0.5f);
    }

    private void Trim()
    {
        if (Shots > 0 && stops.Count > Shots)
            stops.RemoveRange(Shots, stops.Count - Shots);
    }

    private static Quaternion LookAt(Vector3 eye, Vector3 target)
    {
        var world = Matrix.Invert(Matrix.LookAtRH(eye, target, Vector3.UnitY));
        world.Decompose(out _, out Quaternion rotation, out _);
        return rotation;
    }

    private IEnumerable<T> FindAllInScene<T>() where T : EntityComponent
    {
        var scene = SceneSystem.SceneInstance?.RootScene;
        return scene is null ? Array.Empty<T>() : Walk(scene.Entities);

        static IEnumerable<T> Walk(IEnumerable<Entity> entities)
        {
            foreach (var entity in entities)
            {
                if (entity.Get<T>() is { } component)
                    yield return component;

                foreach (var fromChild in Walk(entity.GetChildren()))
                    yield return fromChild;
            }
        }
    }

    private T? FindInScene<T>() where T : EntityComponent
    {
        var scene = SceneSystem.SceneInstance?.RootScene;
        return scene is null ? null : Find(scene.Entities);

        static T? Find(IEnumerable<Entity> entities)
        {
            foreach (var entity in entities)
            {
                if (entity.Get<T>() is { } component)
                    return component;

                if (Find(entity.GetChildren()) is { } fromChild)
                    return fromChild;
            }

            return null;
        }
    }
}
