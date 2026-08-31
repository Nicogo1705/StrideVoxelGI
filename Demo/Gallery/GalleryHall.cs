using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Extensions;
using Stride.Graphics.GeometricPrimitives;
using Stride.Rendering;
using Stride.Rendering.Lights;

namespace Demo.Gallery;

/// <summary>
/// Builds the room: the shell, the alcoves, the window and the light. The exhibits themselves are
/// in <see cref="GalleryExhibits"/>.
/// </summary>
/// <remarks>
/// One nave, two rows of ten alcoves, and a tall window at the far end. The hall is deliberately
/// underlit: the ceiling has no fixtures at all, so what you see of the floor and the walls has
/// bounced off something first. Walking towards the window, the light on the stone goes from warm
/// to cold without a single lamp changing - which is the whole argument for tracing the bounce.
/// </remarks>
public sealed class GalleryHall
{
    public const float HalfWidth = 7f;
    public const float HalfLength = 22f;
    public const float Height = 6.2f;

    /// <summary>An alcove: how far it is recessed, how wide its opening is, how tall it stands.</summary>
    public const float AlcoveDepth = 2.2f;
    public const float AlcoveWidth = 2.8f;
    public const float AlcoveHeight = 3.2f;
    public const float PlinthHeight = 0.95f;

    /// <summary>The back of the alcoves: the hall's real half-width, before the outer skin.</summary>
    public const float OuterHalfWidth = HalfWidth + AlcoveDepth;

    /// <summary>Every wall in the hall is this thick.</summary>
    private const float Thickness = 0.6f;

    /// <summary>Alcove centres along Z, ten a side, with a gap in the middle for the aisle.</summary>
    public static readonly float[] Bays = { -17.5f, -13.5f, -9.5f, -5.5f, -1.5f, 2.5f, 6.5f, 10.5f, 14.5f, 18.5f };

    private readonly GraphicsDevice device;
    private readonly Scene scene;

    private readonly MeshDraw cube;
    private readonly MeshDraw sphere;
    private readonly MeshDraw cylinder;

    public GalleryHall(GraphicsDevice device, Scene scene, GalleryPalette palette)
    {
        this.device = device;
        this.scene = scene;
        Palette = palette;

        cube = GeometricPrimitive.Cube.New(device).ToMeshDraw();
        sphere = GeometricPrimitive.Sphere.New(device, 1f, 24).ToMeshDraw();
        cylinder = GeometricPrimitive.Cylinder.New(device, 1f, 1f, 24).ToMeshDraw();
    }

    public GalleryPalette Palette { get; }

    /// <summary>The scene everything is added to.</summary>
    public Scene Scene => scene;

    /// <summary>The slab of night behind the window, and what hangs in it.</summary>
    public Entity? Sky { get; private set; }
    public Entity? Moon { get; private set; }

    /// <summary>Adds a box. Size is the full extent, position its centre.</summary>
    public Entity Box(string name, Vector3 position, Vector3 size, Material material, Entity? parent = null)
        => Add(name, position, size, material, cube, parent);

    public Entity Ball(string name, Vector3 position, float diameter, Material material, Entity? parent = null)
        => Add(name, position, new Vector3(diameter), material, sphere, parent);

    public Entity Post(string name, Vector3 position, float diameter, float height, Material material, Entity? parent = null)
        => Add(name, position, new Vector3(diameter, height, diameter), material, cylinder, parent);

    /// <summary>A light with no fixture: the source is the geometry, and the voxels carry it.</summary>
    public Entity Lamp(string name, Vector3 position, Vector3 size, Color3 colour, float intensity, Entity? parent = null)
        => Box(name, position, size, Palette.Emissive(colour, intensity), parent);

    private Entity Add(string name, Vector3 position, Vector3 size, Material material, MeshDraw draw, Entity? parent)
    {
        var model = new Model { material, new Mesh { Draw = draw } };
        var entity = new Entity(name) { new ModelComponent(model) };

        entity.Transform.Position = position;
        entity.Transform.Scale = size;

        if (parent is null)
            scene.Entities.Add(entity);
        else
            parent.AddChild(entity);

        return entity;
    }

    /// <summary>The shell, the piers between the bays, the window and the night behind it.</summary>
    public void BuildShell()
    {
        var outer = OuterHalfWidth + Thickness;
        var length = HalfLength * 2 + Thickness * 2;

        // The floor and the ceiling run the full width, alcoves included: stopping them at the nave
        // left the top of every bay open to the sky.
        Box("Floor", new Vector3(0, -Thickness / 2, 0), new Vector3(outer * 2, Thickness, length), Palette.Floor);
        Box("Ceiling", new Vector3(0, Height + Thickness / 2, 0), new Vector3(outer * 2, Thickness, length), Palette.Stone);

        foreach (var side in new[] { -1f, 1f })
        {
            Box($"Wall{side}", new Vector3(side * (OuterHalfWidth + Thickness / 2), Height / 2, 0), new Vector3(Thickness, Height, length), Palette.Plaster);
            BuildPiers(side);
        }

        Box("WallBack", new Vector3(0, Height / 2, HalfLength + Thickness / 2), new Vector3(outer * 2, Height, Thickness), Palette.Plaster);

        BuildWindowWall(Thickness);
        BuildAisle();
    }

    /// <summary>
    /// The nave's own wall on one side: solid between the openings, and a lintel over each of them.
    /// </summary>
    /// <remarks>
    /// The bays used to be framed by a jamb apiece and nothing in between, which left a finger of
    /// open air between every pair of alcoves and a two-metre hole at each end of the hall - you
    /// could see the void through them. A pier fills the whole gap and the whole alcove depth, so
    /// the wall is closed and a bay is a recess cut into it.
    /// </remarks>
    private void BuildPiers(float side)
    {
        const float openingHalf = AlcoveWidth / 2;
        var x = side * (HalfWidth + AlcoveDepth / 2);

        // The solid runs are the gaps between the openings, plus the two ends of the hall.
        var edges = new List<float> { -HalfLength - Thickness };
        foreach (var bay in Bays)
        {
            edges.Add(bay - openingHalf);
            edges.Add(bay + openingHalf);
        }

        edges.Add(HalfLength + Thickness);

        for (var i = 0; i < edges.Count; i += 2)
        {
            var (from, to) = (edges[i], edges[i + 1]);
            if (to - from > 0.01f)
                Box($"Pier{side}_{i}", new Vector3(x, Height / 2, (from + to) / 2), new Vector3(AlcoveDepth, Height, to - from), Palette.Plaster);
        }

        // An alcove stops at its own ceiling; the wall has to carry on from there to the hall's. It
        // starts at the top of that ceiling rather than its underside, and runs wider than the
        // opening: a lintel flush with the alcove's slab put two coplanar faces in the nave wall,
        // both facing the visitor, which is a band of z-fighting along the top of every bay. Solids
        // may overlap - what must never be shared is a visible face.
        const float slab = 0.2f;
        foreach (var bay in Bays)
            Box($"Lintel{side}_{bay}", new Vector3(x, (AlcoveHeight + slab + Height) / 2, bay), new Vector3(AlcoveDepth, Height - AlcoveHeight - slab, AlcoveWidth + 0.4f), Palette.Plaster);
    }

    /// <summary>
    /// The far wall, built around a tall opening. Outside it, a cold slab and a moon: the only
    /// light in the hall that is not warm, and the reason the far end reads blue.
    /// </summary>
    private void BuildWindowWall(float thickness)
    {
        const float openingHalfWidth = 3f;
        const float sill = 1.1f;
        const float head = 4.6f;
        var z = -HalfLength - thickness / 2;

        // The piers reach the outer skin, not the nave's corner: stopping them at HalfWidth left a
        // gap either side of the window looking straight out of the building.
        foreach (var side in new[] { -1f, 1f })
        {
            var pierWidth = OuterHalfWidth + thickness - openingHalfWidth;
            var x = side * (openingHalfWidth + pierWidth / 2);
            Box($"WindowPier{side}", new Vector3(x, Height / 2, z), new Vector3(pierWidth, Height, thickness), Palette.Plaster);
        }

        Box("WindowSill", new Vector3(0, sill / 2, z), new Vector3(openingHalfWidth * 2, sill, thickness), Palette.Stone);
        Box("WindowHead", new Vector3(0, (Height + head) / 2, z), new Vector3(openingHalfWidth * 2, Height - head, thickness), Palette.Plaster);

        // Mullions: they are what turns a hole into a window, and they draw the shadow that tells
        // you the far light is directional.
        for (var i = -1; i <= 1; i++)
            Box($"Mullion{i}", new Vector3(i * 1.5f, (sill + head) / 2, z), new Vector3(0.12f, head - sill, thickness * 0.8f), Palette.Steel);

        Sky = Box("NightSky", new Vector3(0, 3.5f, z - 5f), new Vector3(26, 16, 0.4f), Palette.Emissive(new Color3(0.16f, 0.26f, 0.48f), 0.6f));
        Moon = Ball("Moon", new Vector3(-3.4f, 5.4f, z - 4.6f), 1.5f, Palette.Emissive(new Color3(0.85f, 0.88f, 1f), 3f));
        Box("Roofline", new Vector3(2.5f, 0.6f, z - 3.6f), new Vector3(9, 3.6f, 0.5f), Palette.Soot);
    }

    /// <summary>The aisle: a runner, two benches, and the only warm lamps in the room.</summary>
    private void BuildAisle()
    {
        // Ten centimetres, not four, and the reason is the voxel rather than the upholstery. At 4cm
        // the runner was thinner than one 4.7cm cell and lay in the same row as the floor it sits
        // on, so the two wrote into the same voxels - and the pale checker, being the far larger
        // surface, won nearly all of them. What survived was the runner's own side faces, which put
        // red into the cells along its edges and nowhere else: in a reflection the carpet showed up
        // as two thin red lines drawn on a white floor. Two voxel rows of its own is what it takes
        // for a red carpet to be red in the voxels, and what a bounce carries is what the voxels
        // hold, not what the screen shows.
        Box("Runner", new Vector3(0, 0.05f, 0), new Vector3(3.4f, 0.10f, HalfLength * 2 - 3), Palette.Crimson);

        // Two emissive spheres from one model, drawn by instancing rather than by two entities.
        //
        // A test with an unambiguous answer: instancing transforms vertices in the vertex shader,
        // and if the voxelization pass does not run that transform it writes every instance at the
        // base transform instead of its own. On ordinary geometry that failure is invisible until
        // you go looking - the meshes render correctly, they are simply absent from the lighting.
        // Emissive geometry states it out loud: whichever instance the voxels contain lights the
        // floor under it, and whichever they do not, does not. Two lamps, two pools of light, or
        // the answer is no.
        InstancedLamps();

        // A mirror in the open, on the axis, at eye height. It is an exhibit and an instrument at
        // once: every polished thing in this hall is sunk in an alcove two metres deep, which is a
        // terrible place to work out what a reflection is actually made of. Here the position is
        // known, the sightlines are the whole nave, and whatever the cone brings back has nowhere
        // to hide. 0.09 is polished enough to resolve the room and rough enough that the cone still
        // opens - a true mirror would be the most short-sighted surface in the building.
        Ball("Orb", new Vector3(0, 2.0f, 0), 2.4f, Palette.Metal(new Color3(0.90f, 0.90f, 0.92f), 0.09f));

        foreach (var z in new[] { -8f, 8f })
        {
            var bench = Box($"Bench{z}", new Vector3(0, 0.44f, z), new Vector3(1.1f, 0.12f, 2.4f), Palette.Stone);
            Post($"BenchLeg{z}a", new Vector3(0, 0.22f, z - 0.9f), 0.5f, 0.44f, Palette.Bronze, bench.GetParent());
            Post($"BenchLeg{z}b", new Vector3(0, 0.22f, z + 0.9f), 0.5f, 0.44f, Palette.Bronze, bench.GetParent());
        }

        // Four warm slots in the cornice, aimed at nothing: they light the ceiling, the ceiling
        // lights the room. Take them away and the hall is black even with the alcoves lit.
        foreach (var z in new[] { -14f, -5f, 5f, 14f })
        {
            foreach (var side in new[] { -1f, 1f })
                Lamp($"Cornice{z}{side}", new Vector3(side * (HalfWidth - 0.35f), Height - 0.45f, z), new Vector3(0.25f, 0.1f, 2.2f), new Color3(1f, 0.82f, 0.6f), 2.2f);
        }
    }

    /// <summary>
    /// One alcove: a recess with its own floor, its own little source, and a glass front when the
    /// case is sealed. Everything an exhibit needs to exist as a place rather than an object.
    /// </summary>
    public Entity BuildAlcove(int index, float side, float z, bool sealedCase, Color3 lightColour, float lightIntensity)
    {
        const float depth = AlcoveDepth;
        const float width = AlcoveWidth;
        const float height = AlcoveHeight;
        const float plinth = PlinthHeight;

        var x = side * (HalfWidth + depth / 2);
        var root = new Entity($"Alcove{index:00}");
        root.Transform.Position = new Vector3(x, 0, z);
        scene.Entities.Add(root);

        Box("Back", new Vector3(side * (depth / 2 - 0.1f), height / 2, 0), new Vector3(0.2f, height, width), Palette.Alcove, root);
        Box("Top", new Vector3(0, height, 0), new Vector3(depth, 0.2f, width), Palette.Alcove, root);
        Box("Plinth", new Vector3(0, plinth / 2, 0), new Vector3(depth, plinth, width), Palette.Stone, root);

        // The source is a slot in the alcove's own ceiling, hidden from the visitor by the lintel:
        // you see what it lights, never the lamp.
        Lamp("Slot", new Vector3(-side * 0.35f, height - 0.16f, 0), new Vector3(0.7f, 0.08f, width - 0.8f), lightColour, lightIntensity, root);

        // Stopping short of the lintel: at full height the pane ended inside the alcove's ceiling
        // slab, and a transparent surface sharing a face with an opaque one flickers.
        if (sealedCase)
            Box("Glass", new Vector3(-side * (depth / 2 - 0.05f), (plinth + height) / 2 - 0.06f, 0), new Vector3(0.06f, height - plinth - 0.12f, width - 0.2f), Palette.Glass, root);

        return root;
    }

    /// <summary>Two instances of one emissive sphere, four metres apart down the aisle.</summary>
    private void InstancedLamps()
    {
        var model = new Model
        {
            Palette.Emissive(new Color3(0.45f, 1f, 0.75f), 3f),
            new Mesh { Draw = sphere },
        };

        var entity = new Entity("InstancedLamps") { new ModelComponent(model) };

        // The component's own transform is left at the origin and the instances carry the whole
        // placement, so nothing about where they sit can come from the entity by accident - if one
        // of them lights the floor, it is because its instance matrix reached the voxelizer.
        var instances = new InstancingUserArray();
        instances.UpdateWorldMatrices(new[]
        {
            Matrix.Scaling(0.6f) * Matrix.Translation(-2.2f, 1.6f, -4f),
            Matrix.Scaling(0.6f) * Matrix.Translation(2.2f, 1.6f, -4f),
        });

        entity.Add(new InstancingComponent { Type = instances });
        scene.Entities.Add(entity);
    }

    /// <summary>Where the visitor stands to read an alcove's plaque.</summary>
    public static Vector3 FocusOf(float side, float z) => new Vector3(side * (HalfWidth - 1.6f), 1.6f, z);
}
