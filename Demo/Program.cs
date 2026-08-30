using System;
using System.Linq;
using Demo;
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
//   --rdc                  ask RenderDoc to capture the frame each stop is shot on
//   --volume=N             resize the voxel volume, in world units
//   --levels=N             clipmap rings to ask for
//   --no-gi                turn the indirect light off before capturing
//   --quality-cycle        hold the camera still and switch tier at each stop instead
//   --view=cones|raw       switch to a voxel debug view before capturing
//   --out=DIR              where the PNGs go (default Screenshots next to the executable)
//   --pivot=N              distance ahead of the camera the tour treats as its subject
//   --quality=low|medium|high|ultra   switch the volume to that preset first
//   --divisor=1|2|4        trace the diffuse cones at 1/N of the screen and upsample
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

    var scene = game.SceneSystem.SceneInstance?.RootScene;
    if (scene is null)
        return;

    var cameras = scene.Entities.Where(entity => entity.Get<CameraComponent>() is not null).ToList();

    foreach (var entity in cameras)
    {
        if (entity.Get<BasicCameraController>() is null)
            entity.Add(new BasicCameraController());
    }

    // The capture pass rides the first camera: it is the one the scene frames the box with, and
    // the tour orbits from wherever that camera stands.
    if (tour is not null && cameras.FirstOrDefault() is { } camera)
        camera.Add(tour);
});

game.Run();
