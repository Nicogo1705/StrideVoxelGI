using System;
using System.Globalization;
using System.Linq;
using Demo;
using Demo.Shell;
using Stride.Rendering.Voxels.Grid;
using Stride.BepuPhysics.Definitions.Colliders.Voxels;
using Demo.Gallery;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using StrideVoxelGI;

// The whole demo: open the game, which loads the scene named in GameSettings. Anything clever
// belongs in the scene or in a script, not here — this file exists so `dotnet run` has a Main,
// and so the store can launch the demo the same way on every operating system.

// Driving the demo without a keyboard, for captures and for before/after measurements:
//
//   --capture              walk the camera through a few viewpoints, save a PNG at each, then exit
//   --profiler=gpu|cpu|fps open Stride's profiler at startup - gpu is what the voxel passes cost
//   --shots=N              stops to visit (default 5)
//   --settle=N             frames to hold at each stop before capturing (default 90)
//   --warmup=N             seconds to let the game load and compile before the first stop (default 5)
//   --dump-gi              also save the reduced-resolution GI buffer at each stop
//   --gallery              walk a hall of twenty exhibits instead of the Cornell box
//   --rdc                  ask RenderDoc to capture the frame each stop is shot on
//   --volume=N             resize the voxel volume, in world units
//   --levels=N             clipmap rings to ask for
//   --no-gi                turn the indirect light off before capturing
//   --quality-cycle        hold the camera still and switch tier at each stop instead
//   --view=cones|raw       switch to a voxel debug view before capturing
//   --out=DIR              where the PNGs go (default Screenshots next to the executable)
//   --res=WxH              render at this size instead of the one in GameSettings
//   --pivot=N              distance ahead of the camera the tour treats as its subject
//   --quality=low|medium|high|ultra   switch the volume to that preset first
//   --divisor=1|2|4        trace the diffuse cones at 1/N of the screen and upsample
//   --igpu                 keep whatever GPU Windows hands out instead of asking for the best one
//
// Synthesized key presses do not reach Stride's input, so P and Ctrl+S are out of reach for
// anything running the demo from the outside. Without these arguments nothing below happens and
// the demo is exactly what it was.
var capture = args.Contains("--capture");
var profiler = ParseProfiler(Option("--profiler"));
CaptureTour? tour = capture || profiler != VoxelGIProfilerPage.Off
    ? new CaptureTour
    {
        Tour = capture,
        Profiler = profiler,
        Shots = ParseInt(Option("--shots"), 5),
        SettleFrames = ParseInt(Option("--settle"), 90),
        WarmupSeconds = ParseFloat(Option("--warmup"), 5f),
        DumpGIBuffer = args.Contains("--dump-gi"),
        RenderDoc = args.Contains("--rdc"),
        VolumeSize = ParseFloat(Option("--volume"), 0),
        ClipMapLevels = ParseInt(Option("--levels"), 0),
        DisableGI = args.Contains("--no-gi"),
        CycleQuality = args.Contains("--quality-cycle"),
        View = ParseView(Option("--view")),
        OutputDirectory = Option("--out"),
        PivotDistance = ParseFloat(Option("--pivot"), 0),
        Quality = ParseQuality(Option("--quality")),
        GIResolutionDivisor = ParseInt(Option("--divisor"), 0),
    }
    : null;

string? Option(string name)
{
    var prefix = name + "=";
    return args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
}

static int ParseInt(string? value, int fallback) => int.TryParse(value, out var parsed) ? parsed : fallback;

static float ParseFloat(string? value, float fallback) =>
    float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

static VoxelGIDebugView? ParseView(string? value) => value?.ToLowerInvariant() switch
{
    "off" => VoxelGIDebugView.Off,
    "cones" => VoxelGIDebugView.Cones,
    "raw" => VoxelGIDebugView.Raw,
    _ => null,
};

static VoxelGIQuality? ParseQuality(string? value) => value?.ToLowerInvariant() switch
{
    "low" => VoxelGIQuality.Low,
    "medium" => VoxelGIQuality.Medium,
    "high" => VoxelGIQuality.High,
    "ultra" => VoxelGIQuality.Ultra,
    _ => null,
};

static VoxelGIProfilerPage ParseProfiler(string? value) => value?.ToLowerInvariant() switch
{
    "fps" => VoxelGIProfilerPage.Fps,
    "cpu" => VoxelGIProfilerPage.Cpu,
    "gpu" => VoxelGIProfilerPage.Gpu,
    _ => VoxelGIProfilerPage.Off,
};

using var game = new Game();

// A Debug build turns on the D3D11 debug layer, and Stride answers every validation message by
// dumping the whole render scope tree. Voxelization trips one such message each frame - it rebinds
// the clipmap for writing while the previous frame's lighting pass still has it bound for reading,
// which D3D resolves on its own - so the console fills with the same tree forever and nothing else
// is readable. The demo has no use for the debug layer; run the engine's own tests to get it back.
game.GraphicsDeviceManager.DeviceCreationFlags = DeviceCreationFlags.None;

// The engine picks the fastest GPU on its own now (fork branch gpu-best-adapter: the device
// picker used to skip adapters with no display output, which on a hybrid laptop is precisely
// the discrete GPU - every screen is wired to the integrated one). --igpu asks for the
// battery-friendly adapter instead, for measuring on the small GPU on purpose; it must be set
// before anything touches the graphics adapter list.
if (args.Contains("--igpu"))
    GraphicsAdapterFactory.GpuPreference = GpuPreference.MinimumPower;

// GameSettings pins the back buffer at 1920x1080, and the window on this machine is not that: a
// 1440p or 4K screen stretches the frame up, which reads as a soft, pixelated hall and puts the
// aliasing back on every mullion that FXAA had just taken off. The Cornell box keeps the fixed
// size - its screenshots are what the store shows, and a comparison shot is worth nothing if the
// resolution moves under it - but the gallery is walked around rather than photographed, so it
// takes the monitor's own resolution unless --res says otherwise.
var (renderWidth, renderHeight) = ParseSize(Option("--res"));
if (renderWidth == 0 && args.Contains("--gallery"))
    (renderWidth, renderHeight) = NativeSize();

if (renderWidth > 0 && renderHeight > 0)
{
    game.GraphicsDeviceManager.PreferredBackBufferWidth = renderWidth;
    game.GraphicsDeviceManager.PreferredBackBufferHeight = renderHeight;
}

static (int Width, int Height) ParseSize(string? value)
{
    var parts = value?.Split('x', 'X');
    return parts is { Length: 2 } && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height)
        ? (width, height)
        : (0, 0);
}

/// <summary>
/// The primary output's size on the desktop, or nothing if there is no adapter to ask.
/// </summary>
/// <remarks>
/// Its bounds, not its CurrentDisplayMode: reading the mode makes Stride create a throwaway D3D11
/// device to resolve it against, and a Debug build asks for a debug device, which is not installed
/// on most machines - so the console gets a red "Failed to create Direct3D device" for a number
/// the window rectangle already knows.
/// </remarks>
static (int Width, int Height) NativeSize()
{
    try
    {
        if (GraphicsAdapterFactory.DefaultAdapter is { } adapter)
        {
            var outputs = adapter.Outputs;
            if (outputs.Length > 0 && outputs[0].DesktopBounds is { Width: > 0 } bounds)
                return (bounds.Width, bounds.Height);
        }
    }
    catch (Exception)
    {
        // Headless, or an adapter that will not answer: keep what GameSettings asked for.
    }

    return (0, 0);
}

// One exception, and it earns its place: the asset compiler resolves a scene's script tags against
// the assemblies it has loaded, and the executable's own is not always among them. Stride 4.3 drops
// BasicCameraController out of the scene with
//
//   Unable to resolve tag [!Demo.BasicCameraController,Demo]
//
// and replaces it with an inert object, leaving the camera frozen - which is precisely when you
// most want to fly around and look. Attaching the controller here skips type resolution entirely
// and costs nothing when the scene already carries one.
game.Script.AddTask(async () =>
{
    await game.Script.NextFrame();

    // WinForms builds the form at 800x600 and never sets StartPosition, so it lands wherever
    // Windows' cascade puts it - and the back buffer is then grown to the desktop's own resolution
    // keeping that top-left corner. A window as wide as the screen, with its origin a few hundred
    // pixels in, spills onto whatever monitor sits to the right. Centring on the primary output
    // fixes it and keeps behaving when --res asks for something smaller: a full-size window centres
    // to (0, 0) on its own.
    var (screenWidth, screenHeight) = NativeSize();
    if (screenWidth > 0 && screenHeight > 0)
    {
        var bounds = game.Window.ClientBounds;
        game.Window.Position = new Int2(
            Math.Max(0, (screenWidth - bounds.Width) / 2),
            Math.Max(0, (screenHeight - bounds.Height) / 2));
    }

    var scene = game.SceneSystem.SceneInstance?.RootScene;
    if (scene is null)
        return;

    // The game starts on an empty scene carrying the home screen, and the shell loads whichever
    // demo is chosen - or the one named on the command line, so every measurement path keeps the
    // flow it always had.
    var requested = args.Contains("--gallery") ? DemoCatalog.Gallery
                  : args.Contains("--voxelgrid") ? DemoCatalog.VoxelGrid
                  : capture || profiler != VoxelGIProfilerPage.Off ? DemoCatalog.CornellBox
                  : (int?)null;

    // The traced voxel pass joins the compositor now, switched off: adding a renderer once a frame
    // is in flight modifies the list being walked to draw it.
    VoxelGridDemo.StartSurface = Option("--surface") switch
    {
        "cubes" => VoxelSurfaceForm.Cubes,
        "sn" => VoxelSurfaceForm.SurfaceNets,
        _ => VoxelSurfaceForm.MarchingCubes,
    };

    VoxelGridDemo.StartColliderForm = Option("--collider") switch
    {
        "box" => VoxelChildForm.Box,
        "sphere" => VoxelChildForm.Sphere,
        "sn" => VoxelChildForm.TriangleSurfaceNets,
        _ => VoxelChildForm.TriangleMarchingCubes,
    };
    // The grid's size and resolution, because the pair is the whole cost model of a dense field and
    // it is worth being able to move it without a rebuild. Cubic in the sample count: 129 is eight
    // times the field 65 is, not twice.
    if (int.TryParse(Option("--samples"), out var samples) && samples > 1)
        VoxelGridDemo.Samples = samples;
    if (float.TryParse(Option("--cell"), NumberStyles.Float, CultureInfo.InvariantCulture, out var cell) && cell > 0)
        VoxelGridDemo.CellSize = cell;

    DemoShell.ExitAfterShots = !args.Contains("--stay");
    VoxelGridDemo.StartWithWireframe = args.Contains("--wireframe");
    // Off by default now that the grid draws as a model.
    //
    // Both paths draw the same field, and at nearly the same depth: run together they fight over
    // every pixel of the surface and it comes out as speckle. V still turns the traced pass on, and
    // seeing them disagree is the point of being able to.
    VoxelGridDemo.StartWithTrace = args.Contains("--trace");

    // --no-model leaves the grid out of the mesh path, so the traced pass can be judged on its own.
    VoxelGridDemo.StartWithModel = !args.Contains("--no-model");
    if (float.TryParse(Environment.GetEnvironmentVariable("STRIDE_VOXEL_DEBUG"), NumberStyles.Float, CultureInfo.InvariantCulture, out var debugView))
        VoxelGridDemo.DebugView = debugView;

    if (args.Contains("--dig"))
        VoxelGridDemo.AutoDigAfterFrames = 200;

    VoxelGridDemo.InstallPass(game);

    // Screen space reflections read a normals buffer that only a scene with meshes produces, and the
    // game now starts on one with none. Off until a demo asks for it - the gallery does.
    if (PostEffectsToggle.FindPostEffects(game.SceneSystem.GraphicsCompositor?.Game) is { } startupEffects)
        startupEffects.LocalReflections.Enabled = false;

    // A camera from frame zero: a frame drawn with none walks into the post chain without a depth
    // buffer. On its own entity, so leaving the menu removes it with everything else.
    scene.Entities.Add(new Entity(DemoShell.MenuCameraName)
    {
        new CameraComponent
        {
            VerticalFieldOfView = 60f,
            NearClipPlane = 0.1f,
            FarClipPlane = 100f,
            Slot = game.SceneSystem.GraphicsCompositor.Cameras[0].ToSlotId(),
        },
    });

    scene.Entities.Add(new Entity("Shell")
    {
        new DemoShell
        {
            StartWith = requested,
            Profiler = profiler,
            ShotDirectory = Option("--shot"),
            AutoBackAfterFrames = ParseInt(Option("--auto-back"), 0),
            Tour = tour,
        },
    });
});

game.Run();
