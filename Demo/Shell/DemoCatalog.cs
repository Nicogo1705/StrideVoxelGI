using System;
using System.Collections.Generic;
using Stride.Engine;

namespace Demo.Shell;

/// <summary>
/// One entry on the home screen: what it is called, what it shows, and how to build it.
/// </summary>
/// <param name="Name">Shown large on the card.</param>
/// <param name="Tagline">One line under the name. What the demo is <em>for</em>, not what it contains.</param>
/// <param name="Controls">Keys the demo answers to, listed while it runs.</param>
/// <param name="Build">
/// Fills the (already emptied) root scene, and drives the camera it is handed rather than creating
/// one. There is exactly one camera in this game: a slot may hold only one, and a slot vacated by
/// an entity that has been removed stays claimed by it, so swapping cameras between demos means
/// swapping the one thing the engine will not let you swap.
/// </param>
public sealed record DemoEntry(string Name, string Tagline, string[] Controls, Action<Game, Entity> Build);

/// <summary>
/// The three demos, in the order they make sense to look at: the box that shows the effect exists,
/// the hall that shows it holding up at scale, and the grid that shows where it is going next.
/// </summary>
public static class DemoCatalog
{
    /// <summary>Index of the demo whose entities come from the loaded scene rather than from code.</summary>
    public const int CornellBox = 0;

    /// <summary>Index of the gallery.</summary>
    public const int Gallery = 1;

    /// <summary>Index of the voxel grid demo, which also owns a compositor pass.</summary>
    public const int VoxelGrid = 2;

    /// <summary>
    /// The shell's own tools, the same in every demo. Listed first so the eye finds them in the
    /// same place whatever is running.
    /// </summary>
    private const string ShellTools = "F1  menu      F2  profiler (off / fps / cpu / gpu)      F3  next page      F4  post: bloom / AA      F5  screenshot";

    /// <summary>The flight controls, shared by every demo that is flown rather than walked.</summary>
    private const string Flight = "Right mouse  look      WASD / ZQSD  fly      E / Space  up      C  down      Shift  fast";

    /// <summary>The voxel GI overlay's keys, under Ctrl in every demo that runs it.</summary>
    private const string GISettings = "Ctrl + key  GI settings, listed top left";

    public static IReadOnlyList<DemoEntry> Entries { get; } =
    [
        new DemoEntry(
            "Cornell box",
            "The reference scene: colour bleeding from two walls, and nothing else to explain it.",
            [ShellTools, Flight, GISettings],
            DemoScenes.BuildCornellBox),

        new DemoEntry(
            "The cabinet of lights",
            "Twenty exhibits in one hall - the same bounce, asked to hold up at scale.",
            [
                ShellTools,
                "WASD / ZQSD  walk      Shift  run      Space  jump      Tab  free the mouse",
                "G  ghost mode (Space / C  rise / sink)      L  the ball of light      E  work the case",
                GISettings,
            ],
            DemoScenes.BuildGallery),

        new DemoEntry(
            "Voxel grid",
            "A field traced cell by cell instead of meshed, collided against without triangles.",
            [
                ShellTools,
                Flight,
                "Left mouse  dig      F  fill      F11  collider wireframe",
                "V  the traced pass, over the model      B  drawn: cubes / MC / SN",
                "C  collider: box / MC / SN / sphere      X  match the collider to what is drawn",
            ],
            DemoScenes.BuildVoxelGrid),
    ];
}
