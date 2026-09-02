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

    public static IReadOnlyList<DemoEntry> Entries { get; } =
    [
        new DemoEntry(
            "Cornell box",
            "The reference scene: colour bleeding from two walls, and nothing else to explain it.",
            ["F1  menu      F2  profiler (off / fps / cpu / gpu)      N  next page", "Right mouse + WASD  fly", "G  voxel debug views"],
            DemoScenes.BuildCornellBox),

        new DemoEntry(
            "The cabinet of lights",
            "Twenty exhibits in one hall - the same bounce, asked to hold up at scale.",
            ["F1  menu      F2  profiler (off / fps / cpu / gpu)      N  next page", "WASD  walk", "G  ghost mode", "L  the ball of light", "Tab  free the mouse"],
            DemoScenes.BuildGallery),

        new DemoEntry(
            "Voxel grid",
            "A field traced cell by cell instead of meshed, collided against without triangles.",
            ["F1  menu      F2  profiler (off / fps / cpu / gpu)      N  next page", "Right mouse + WASD  fly", "Left mouse  dig      F  fill", "V  traced grid on/off", "B  drawn: cubes / MC / SN", "C  collider: box / MC / SN / sphere", "X  match the collider to what is drawn", "F11  collider wireframe"],
            DemoScenes.BuildVoxelGrid),
    ];
}
