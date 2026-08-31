using System;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Rendering;

namespace Demo.Gallery;

/// <summary>
/// The twenty pieces. Ten sealed, ten that answer to a key, each about one thing light does.
/// </summary>
/// <remarks>
/// Written out one by one rather than generated: twenty variations on one niche is a test scene,
/// and this is meant to be walked through. They alternate sides in the order you meet them, and go
/// from the plainest demonstration to the ones that need the first few to make sense.
/// </remarks>
public static class GalleryExhibits
{
    private static readonly Color3 Warm = new(1f, 0.84f, 0.62f);
    private static readonly Color3 Cool = new(0.7f, 0.82f, 1f);

    public static void Build(GalleryHall hall)
    {
        var p = hall.Palette;
        var index = 0;

        // ------------------------------------------------------------- sealed cases

        Sealed(hall, ref index, -1, GalleryHall.Bays[0], Warm, 3.2f,
            "The colour that runs", "A red wall, a green wall, a white ball between them. Nothing paints it.",
            (h, root) =>
            {
                h.Box("Red", new Vector3(0, 1.6f, -0.95f), new Vector3(1.6f, 1.3f, 0.08f), p.Crimson, root);
                h.Box("Green", new Vector3(0, 1.6f, 0.95f), new Vector3(1.6f, 1.3f, 0.08f), p.Viridian, root);
                h.Ball("Subject", new Vector3(0, 1.3f, 0), 0.7f, p.Chalk, root);
            });

        Sealed(hall, ref index, 1, GalleryHall.Bays[0], Warm, 3.6f,
            "The hole", "Absolute black does not exist. This one returns two percent, and still reads as an object.",
            (h, root) =>
            {
                h.Box("Card", new Vector3(0, 0.96f, 0), new Vector3(1.4f, 0.02f, 1.4f), p.Chalk, root);
                h.Ball("Void", new Vector3(0, 1.42f, 0), 0.9f, p.Soot, root);
            });

        Sealed(hall, ref index, -1, GalleryHall.Bays[1], Warm, 2.8f,
            "The mirror", "A polished ball shows nothing of itself. Only the room, inside out.",
            (h, root) => h.Ball("Chrome", new Vector3(0, 1.425f, 0), 0.95f, p.Chrome, root));

        Sealed(hall, ref index, 1, GalleryHall.Bays[1], Warm, 2.8f,
            "Five roughnesses", "The same ball five times. All that changes is how wide a cone looks at it.",
            (h, root) =>
            {
                // Smaller and further apart than they were. At 0.42 across on a 0.5 pitch there were
                // eight centimetres between neighbours, so the polished end of the row reflected
                // nothing but the other balls - each of them a dark mirror reflecting the next - and
                // came out black while the rough end, integrating a wide cone that reached the slot
                // above, came out bright. The row read backwards. Twenty-six centimetres of air lets
                // every one of them see the case it is standing in, which is the comparison the
                // plaque is actually making.
                for (var i = 0; i < 5; i++)
                    h.Ball($"Rough{i}", new Vector3(0, 1.16f, -1.2f + i * 0.6f), 0.34f, p.Metal(new Color3(0.8f), i * 0.22f), root);
            });

        Sealed(hall, ref index, -1, GalleryHall.Bays[2], Cool, 2.6f,
            "The blade", "A slit, and light becomes an object you can lay on a table.",
            (h, root) =>
            {
                // The blinds reach the walls of the case and leave twenty centimetres between them:
                // a gap of a metre - which is what two narrow panels at the middle left - is not a
                // slit, and the slit is the whole exhibit.
                h.Box("BlindA", new Vector3(0, 2.1f, -0.7f), new Vector3(1.8f, 1.6f, 1.2f), p.Soot, root);
                h.Box("BlindB", new Vector3(0, 2.1f, 0.7f), new Vector3(1.8f, 1.6f, 1.2f), p.Soot, root);
                h.Box("Table", new Vector3(0, 0.98f, 0), new Vector3(1.8f, 0.06f, 2.2f), p.Chalk, root);
            });

        Sealed(hall, ref index, 1, GalleryHall.Bays[2], Warm, 3.2f,
            "Contact", "Three cubes set down. What glues them to the floor is the shadow they cast on each other.",
            (h, root) =>
            {
                h.Box("A", new Vector3(0, 1.2f, -0.6f), new Vector3(0.5f), p.Chalk, root);
                h.Box("B", new Vector3(0, 1.2f, 0.1f), new Vector3(0.5f), p.Chalk, root);
                h.Box("C", new Vector3(0, 1.65f, 0.1f), new Vector3(0.4f), p.Chalk, root);
            });

        Sealed(hall, ref index, -1, GalleryHall.Bays[3], Warm, 0f,
            "The tube", "No lamp in this case at all. The tube is a surface, and it lights the room anyway.",
            (h, root) =>
            {
                h.Post("Tube", new Vector3(0, 1.95f, 0), 0.16f, 2f, p.Emissive(new Color3(0.45f, 1f, 0.75f), 6f), root);
                h.Ball("Lit", new Vector3(0, 1.25f, 0), 0.6f, p.Chalk, root);
            });

        Sealed(hall, ref index, 1, GalleryHall.Bays[3], Warm, 3.4f,
            "White on white", "Two identical whites. Only the bounce draws the edge between them.",
            (h, root) =>
            {
                h.Box("Wall", new Vector3(0, 1.85f, 0), new Vector3(1.4f, 1.8f, 0.08f), p.Chalk, root);
                h.Box("Shelf", new Vector3(0, 1.5f, 0.45f), new Vector3(1.2f, 0.06f, 0.8f), p.Chalk, root);
                h.Ball("Egg", new Vector3(0, 1.72f, 0.45f), 0.4f, p.Chalk, root);
            });

        Sealed(hall, ref index, -1, GalleryHall.Bays[4], Warm, 3.0f,
            "The cage", "A grid in front of a source: the shadow carries the pattern further than the object does.",
            (h, root) =>
            {
                // A grille lying flat, not a lattice standing on edge. The first version stacked its
                // horizontal courses along Y, and a source directly overhead projects every one of
                // them onto the same footprint - so half the bars cost fill and cast nothing, and
                // only the Z spacing ever reached the floor. Spacing both directions in one
                // horizontal plane is what turns a grid into a printed pattern.
                //
                // The rest is what the voxels can hold: bars four voxels thick, a pitch wider than
                // the voxel the cones read them at, and the whole thing hung low. A cone climbs one
                // mip per step, so an occluder keeps its edges only while it is still in the first
                // few samples - a quarter of a metre over the screen is three of them. Higher up,
                // nearer the slot, the grid would look right and print nothing.
                const float bar = 0.18f;
                const float pitch = 0.5f;
                const float x = 0.25f;
                const float y = 1.22f;

                // Runs along Z, spaced across X.
                for (var i = -1; i <= 1; i++)
                    h.Box($"BarX{i}", new Vector3(x + i * pitch, y, 0), new Vector3(bar, bar, 2.0f), p.Steel, root);

                // Runs along X, spaced down Z.
                for (var i = -2; i <= 2; i++)
                    h.Box($"BarZ{i}", new Vector3(x, y, i * pitch), new Vector3(1.5f, bar, bar), p.Steel, root);

                h.Box("Screen", new Vector3(x, 0.96f, 0), new Vector3(1.6f, 0.02f, 1.9f), p.Chalk, root);
            });

        Sealed(hall, ref index, 1, GalleryHall.Bays[4], Cool, 3.2f,
            "The glass", "It does not stop the light, it rearranges it. Watch the floor, not the ball.",
            (h, root) =>
            {
                h.Box("Base", new Vector3(0, 0.96f, 0), new Vector3(1.6f, 0.02f, 1.6f), p.Cobalt, root);
                h.Ball("Glass", new Vector3(0, 1.42f, 0), 0.9f, p.Glass, root);
            });

        // ------------------------------------------------------------- the ten that answer

        Interactive(hall, ref index, -1, GalleryHall.Bays[5], Warm, 3.2f,
            "The switch", "The lamp goes out. What is left came down the hall and turned in.",
            "switch the lamp",
            (h, root) => h.Ball("Subject", new Vector3(0, 1.35f, 0), 0.8f, p.Chalk, root),
            (exhibit, presses) => Repaint(exhibit, "Slot", presses % 2 == 1
                ? GalleryScene.Palette!.LampOff
                : GalleryScene.Palette!.Emissive(Warm, 3.2f)));

        Interactive(hall, ref index, 1, GalleryHall.Bays[5], Warm, 3.2f,
            "The gel", "Same lamp, another colour. Everything it touches changes its mind.",
            "change the gel",
            (h, root) =>
            {
                h.Box("Card", new Vector3(0, 0.96f, 0), new Vector3(1.5f, 0.02f, 1.5f), p.Chalk, root);
                h.Ball("Subject", new Vector3(0, 1.35f, 0), 0.75f, p.Chalk, root);
            },
            (exhibit, presses) =>
            {
                Color3[] gels = { Warm, new(1f, 0.32f, 0.28f), new(0.35f, 1f, 0.55f), new(0.4f, 0.55f, 1f) };
                Relight(exhibit, gels[presses % gels.Length], 3.2f);
            });

        Interactive(hall, ref index, -1, GalleryHall.Bays[6], Warm, 3.6f,
            "The shutter", "A panel drops in front of the source. The case does not go dark - the light moves.",
            "drop the shutter",
            // A plate that slides across under the slot, parked against the open face of the case.
            // The panel this started as hung at the back of the alcove - the far side of the object
            // from the lamp - and shared a face with the slot into the bargain: it blocked nothing,
            // and it z-fought while blocking nothing. Sliding this one over the source is what
            // makes the caption true: the case keeps its light, and the light arrives another way.
            // Thickness and clearance are the whole of it, and both are measured in voxels rather
            // than in metres - so both had to be revised when the volume grew and the voxel went
            // from 4.7 to 14 centimetres. At 0.22 the panel was back down to 1.6 voxels, the same
            // regime in which the original 0.06 one let the light walk straight through it: an
            // occluder thinner than the cells it is written into cannot fill them, and a cone reads
            // a half-filled cell as half-transparent. 0.45 is a little over three voxels.
            //
            // Clearance matters as much. The slot's underside is at 3.00 and the panel now tops out
            // at 2.68, which leaves better than two voxels of air between them - enough that the
            // emitter and its occluder never land in the same cell, where they would average into
            // one lit voxel and the shutter would occlude nothing however thick it was.
            //
            // Parked at -0.45 rather than -0.6: the free run beside the slot is exactly the width of
            // the panel, so that is the one open position that clears the source without burying
            // the far edge in the back wall.
            (h, root) => h.Box("Shutter", new Vector3(-0.45f, 2.45f, 0), new Vector3(0.9f, 0.45f, 2.2f), p.Soot, root),
            (exhibit, presses) => Move(exhibit, "Shutter", x: presses % 2 == 1 ? 0.35f : -0.45f));

        Interactive(hall, ref index, 1, GalleryHall.Bays[6], Cool, 3.0f,
            "The pendulum", "A weight swings, and its shadow runs faster than it does.",
            "start and stop it",
            (h, root) =>
            {
                // Wire and weight hang off one pivot, and the pivot is what swings. Moving the bob
                // on its own left the wire hanging straight down beside it, attached to nothing.
                var pivot = new Entity("Pivot");
                pivot.Transform.Position = new Vector3(0, 2.9f, 0);
                root.AddChild(pivot);

                h.Box("Wire", new Vector3(0, -0.6f, 0), new Vector3(0.03f, 1.2f, 0.03f), p.Steel, pivot);
                h.Ball("Bob", new Vector3(0, -1.3f, 0), 0.45f, p.Brass, pivot);
            },
            (exhibit, _) => exhibit.Running = !exhibit.Running,
            (exhibit, time) =>
            {
                if (!exhibit.Running)
                    return;

                if (Find(exhibit, "Pivot")?.Transform is { } pivot)
                    pivot.Rotation = Quaternion.RotationX(MathF.Sin(time * 1.6f) * 0.42f);
            });

        Interactive(hall, ref index, -1, GalleryHall.Bays[7], Warm, 3.2f,
            "The polish", "Matte to mirror in four steps, with nothing else touched.",
            "polish one step",
            (h, root) => h.Ball("Subject", new Vector3(0, 1.4f, 0), 0.9f, p.Metal(new Color3(0.8f), 0.6f), root),
            (exhibit, presses) =>
            {
                var roughness = new[] { 0.35f, 0.15f, 0.02f, 0.6f }[presses % 4];
                Repaint(exhibit, "Subject", GalleryScene.Palette!.Metal(new Color3(0.8f), roughness));
            });

        Interactive(hall, ref index, 1, GalleryHall.Bays[7], Warm, 1.6f,
            "The dimmer", "The same lamp, eight times stronger. The bounce does not climb at the same rate as the direct light.",
            "turn it up",
            (h, root) => h.Box("Card", new Vector3(0, 1.5f, 0), new Vector3(1.4f, 1.1f, 0.06f), p.Chalk, root),
            (exhibit, presses) => Relight(exhibit, Warm, new[] { 3.2f, 6.4f, 12.8f, 1.6f }[presses % 4]));

        Interactive(hall, ref index, -1, GalleryHall.Bays[8], Warm, 3.6f,
            "The curtain", "A wall between the lamp and the object. What still reaches it went around.",
            "draw the curtain",
            (h, root) =>
            {
                h.Ball("Subject", new Vector3(0, 1.3f, 0.6f), 0.7f, p.Chalk, root);
                h.Box("Curtain", new Vector3(0, 4.1f, -0.3f), new Vector3(1.6f, 1.9f, 0.1f), p.Crimson, root);
            },
            (exhibit, presses) => Move(exhibit, "Curtain", y: presses % 2 == 1 ? 1.9f : 4.1f));

        Interactive(hall, ref index, 1, GalleryHall.Bays[8], Warm, 3.2f,
            "The turn", "The object turns, the light does not. Everything moving in the image is a consequence.",
            "start and stop it",
            (h, root) => h.Box("Subject", new Vector3(0, 1.5f, 0), new Vector3(1.1f, 1.1f, 0.35f), p.Chalk, root),
            (exhibit, _) => exhibit.Running = !exhibit.Running,
            (exhibit, time) =>
            {
                if (exhibit.Running && Find(exhibit, "Subject")?.Transform is { } subject)
                    subject.Rotation = Quaternion.RotationY(time * 0.7f);
            });

        Interactive(hall, ref index, -1, GalleryHall.Bays[9], Warm, 3.2f,
            "The ground", "Only the floor of the case is repainted. The ceiling of it notices.",
            "repaint the floor",
            (h, root) =>
            {
                h.Box("Ground", new Vector3(0, 0.965f, 0), new Vector3(1.8f, 0.03f, 2.2f), p.Chalk, root);
                h.Ball("Subject", new Vector3(0, 1.355f, 0), 0.75f, p.Chalk, root);
            },
            (exhibit, presses) =>
            {
                var pal = GalleryScene.Palette!;
                Material[] coats = { pal.Crimson, pal.Viridian, pal.Cobalt, pal.Chalk };
                Repaint(exhibit, "Ground", coats[presses % coats.Length]);
            });

        BuildSkyConsole(hall);

        Interactive(hall, ref index, 1, GalleryHall.Bays[9], Cool, 3.8f,
            "The eclipse", "A disc crosses the source. The edge of its shadow is the only sharp thing here.",
            "start and stop it",
            (h, root) =>
            {
                h.Ball("Disc", new Vector3(-0.45f, 2.55f, 0), 0.5f, p.Soot, root);
                h.Box("Card", new Vector3(0, 0.96f, 0), new Vector3(1.6f, 0.02f, 1.8f), p.Chalk, root);
            },
            (exhibit, _) => exhibit.Running = !exhibit.Running,
            (exhibit, time) =>
            {
                // 1.1 put the disc's far edge at 1.35 in an alcove whose wall is at 1.4: it ended
                // each swing buried in the plaster, where the only shadow left is the contact one
                // along the seam. 0.8 keeps it in open air for the whole travel.
                if (exhibit.Running)
                    Move(exhibit, "Disc", z: MathF.Sin(time * 0.8f) * 0.8f);
            });
    }

    /// <summary>
    /// A lectern in the middle of the nave, facing the window, and the night outside answers its
    /// four buttons. The hall has no lamp aimed at the far end: everything you see down there
    /// arrived through the opening, so changing what is behind it repaints thirty metres of floor
    /// without touching a single light in the room.
    /// </summary>
    /// <remarks>
    /// It stands on the axis rather than against a side wall, six metres back from the sill, which
    /// is the one spot in the hall where the window fills your view and the console is under your
    /// hands at the same time - press a button and the change happens in front of you rather than
    /// off to your left. Everything about it is deliberately small: a brass stem, a canted desk,
    /// four seven-centimetre buttons in their bezels, and one warm slot under the lip that lights
    /// the engraved plate off the desk itself. That slot is the only source at this end of the
    /// hall, so the lectern is lit by its own bounce.
    /// </remarks>
    private static void BuildSkyConsole(GalleryHall hall)
    {
        var p = hall.Palette;
        var z = -GalleryHall.HalfLength + 6f;

        var console = new Entity("SkyConsole");
        console.Transform.Position = new Vector3(0, 0, z);
        hall.Scene.Entities.Add(console);

        // The stem: a stone pad, a brass foot, the column, and the collar the desk sits on.
        hall.Post("Pad", new Vector3(0, 0.025f, 0), 0.68f, 0.05f, p.Stone, console);
        hall.Post("Foot", new Vector3(0, 0.09f, 0), 0.34f, 0.13f, p.Bronze, console);
        hall.Post("Column", new Vector3(0, 0.62f, 0), 0.13f, 0.92f, p.Bronze, console);
        hall.Post("Collar", new Vector3(0, 1.10f, 0), 0.26f, 0.05f, p.Bronze, console);

        // The desk is canted towards whoever is standing at it, and everything on it is parented to
        // it so the tilt is described once.
        var desk = new Entity("Desk");
        desk.Transform.Position = new Vector3(0, 1.16f, 0);
        desk.Transform.Rotation = Quaternion.RotationX(MathUtil.DegreesToRadians(24f));
        console.AddChild(desk);

        hall.Box("Top", new Vector3(0, 0, 0), new Vector3(0.78f, 0.04f, 0.36f), p.Bronze, desk);
        hall.Box("Lip", new Vector3(0, 0.025f, 0.175f), new Vector3(0.78f, 0.035f, 0.03f), p.Bronze, desk);
        hall.Box("Plate", new Vector3(0, 0.023f, -0.105f), new Vector3(0.62f, 0.008f, 0.10f), p.Slate, desk);

        // Under the lip, aimed at nothing: it lights the plate, the plate lights your hands.
        hall.Box("Reading", new Vector3(0, -0.035f, 0.15f), new Vector3(0.52f, 0.02f, 0.02f),
                 p.Emissive(new Color3(1f, 0.86f, 0.66f), 1.6f), desk);

        for (var i = 0; i < Skies.Length; i++)
        {
            var x = -0.225f + i * 0.15f;
            hall.Post($"Bezel{i}", new Vector3(x, 0.026f, 0.035f), 0.105f, 0.016f, p.Slate, desk);
            hall.Post($"Button{i}", new Vector3(x, 0.039f, 0.035f), 0.072f, 0.024f,
                      i == 0 ? p.Emissive(Skies[0].Button, 2.2f) : p.LampOff, desk);
        }

        console.Add(new GalleryExhibit
        {
            Number = 0,
            Title = "The window",
            Caption = "Four nights. None of them is a light in this room, and all of them repaint its floor.",
            Prompt = $"switch the sky - now {Skies[0].Name}",
            Focus = new Vector3(0, 1.6f, z + 1.1f),
            Interact = (exhibit, presses) =>
            {
                var sky = Skies[presses % Skies.Length];
                var pal = GalleryScene.Palette!;

                if (hall.Sky?.Get<ModelComponent>() is { } dome)
                    dome.Model.Materials[0] = new MaterialInstance(pal.Emissive(sky.Colour, sky.Intensity));

                if (hall.Moon?.Get<ModelComponent>() is { } moon)
                    moon.Model.Materials[0] = new MaterialInstance(pal.Emissive(sky.Disc, sky.DiscIntensity));

                for (var i = 0; i < Skies.Length; i++)
                    Repaint(exhibit, $"Button{i}", i == presses % Skies.Length ? pal.Emissive(Skies[i].Button, 2.2f) : pal.LampOff);

                exhibit.Prompt = $"switch the sky - now {sky.Name}";
            },
        });
    }

    private readonly record struct Sky(string Name, Color3 Colour, float Intensity, Color3 Disc, float DiscIntensity, Color3 Button);

    private static readonly Sky[] Skies =
    {
        new("a clear night", new Color3(0.16f, 0.26f, 0.48f), 0.6f, new Color3(0.85f, 0.88f, 1f), 3f, new Color3(0.5f, 0.65f, 1f)),
        new("first light", new Color3(0.85f, 0.42f, 0.24f), 0.9f, new Color3(1f, 0.78f, 0.45f), 4f, new Color3(1f, 0.6f, 0.3f)),
        new("a storm", new Color3(0.10f, 0.11f, 0.14f), 0.35f, new Color3(0.55f, 0.58f, 0.62f), 0.6f, new Color3(0.6f, 0.62f, 0.66f)),
        new("an aurora", new Color3(0.10f, 0.48f, 0.34f), 0.85f, new Color3(0.45f, 0.30f, 0.75f), 2f, new Color3(0.3f, 1f, 0.6f)),
    };

    // ------------------------------------------------------------------ plumbing

    private static void Sealed(GalleryHall hall, ref int index, float side, float z, Color3 light, float intensity,
                               string title, string caption, Action<GalleryHall, Entity> contents)
    {
        index++;
        var root = hall.BuildAlcove(index, side, z, sealedCase: true, light, intensity);
        contents(hall, root);

        root.Add(new GalleryExhibit
        {
            Number = index,
            Title = title,
            Caption = caption,
            Focus = GalleryHall.FocusOf(side, z),
        });
    }

    private static void Interactive(GalleryHall hall, ref int index, float side, float z, Color3 light, float intensity,
                                    string title, string caption, string prompt,
                                    Action<GalleryHall, Entity> contents,
                                    Action<GalleryExhibit, int> interact,
                                    Action<GalleryExhibit, float>? animate = null)
    {
        index++;
        var root = hall.BuildAlcove(index, side, z, sealedCase: false, light, intensity);
        contents(hall, root);

        root.Add(new GalleryExhibit
        {
            Number = index,
            Title = title,
            Caption = caption,
            Prompt = prompt,
            Focus = GalleryHall.FocusOf(side, z),
            Interact = interact,
            Animate = animate,
        });
    }

    /// <summary>
    /// The named part of an exhibit, or nothing. Never the exhibit itself: falling back to the root
    /// is how an animation ends up moving the whole cabinet instead of the pendulum inside it.
    /// </summary>
    /// <remarks>
    /// The search goes all the way down, not one level: the console's buttons hang off its desk, so
    /// that the desk's tilt is stated once rather than repeated on each of them.
    /// </remarks>
    private static Entity? Find(GalleryExhibit exhibit, string name) => Find(exhibit.Entity, name);

    private static Entity? Find(Entity parent, string name)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child.Name == name)
                return child;

            if (Find(child, name) is { } found)
                return found;
        }

        return null;
    }

    private static void Move(GalleryExhibit exhibit, string child, float? x = null, float? y = null, float? z = null)
    {
        if (Find(exhibit, child)?.Transform is not { } transform)
            return;

        var position = transform.Position;
        transform.Position = new Vector3(x ?? position.X, y ?? position.Y, z ?? position.Z);
    }

    private static void Relight(GalleryExhibit exhibit, Color3 colour, float intensity)
        => Repaint(exhibit, "Slot", GalleryScene.Palette!.Emissive(colour, intensity));

    private static void Repaint(GalleryExhibit exhibit, string child, Material material)
    {
        if (Find(exhibit, child)?.Get<ModelComponent>() is { } model)
            model.Model.Materials[0] = new MaterialInstance(material);
    }
}
