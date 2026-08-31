using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;

namespace Demo.Gallery;

/// <summary>
/// Reads the plaque of whatever the visitor is standing in front of, and passes on the key press.
/// </summary>
/// <remarks>
/// Everything is drawn with the engine's debug text rather than the UI system: a plaque is three
/// lines that appear when you are close enough, and a UI page for that would be more machinery
/// than the room deserves.
/// </remarks>
public class GalleryHud : SyncScript
{
    public Keys InteractKey { get; set; } = Keys.E;

    /// <summary>How close the visitor has to stand for the plaque to be readable.</summary>
    public float ReadingDistance { get; set; } = 3.4f;

    /// <summary>The controls, drawn in the far corner for as long as the hall is open.</summary>
    /// <remarks>
    /// Kept on screen rather than shown once at the door: a visitor who missed the opening card has
    /// no other way to find out that there is a lamp to switch on, and a line of grey text in a
    /// corner costs the room nothing.
    /// </remarks>
    private static readonly string[] Controls =
    {
        "ZQSD / WASD    marcher",
        "Fleches        marcher aussi",
        "Shift          courir",
        "Espace         sauter",
        "L              la boule de lumiere",
        "E              actionner la vitrine",
        "Echap          liberer la souris",
        "Ctrl + touche  reglages du GI",
    };

    /// <summary>Width of one character of the engine's debug font, in pixels.</summary>
    private const int Glyph = 8;

    private readonly List<GalleryExhibit> exhibits = new();
    private float openingCard = 7f;

    public override void Start()
    {
        Collect(SceneSystem.SceneInstance?.RootScene);
    }

    public override void Update()
    {
        var eye = Entity.Transform.Position;
        var nearest = exhibits
            .Select(exhibit => (exhibit, distance: Vector3.Distance(eye, exhibit.Focus)))
            .Where(pair => pair.distance < ReadingDistance)
            .OrderBy(pair => pair.distance)
            .Select(pair => pair.exhibit)
            .FirstOrDefault();

        var size = GraphicsDevice.Presenter.BackBuffer;
        var centre = new Int2(size.Width / 2, size.Height / 2);

        DrawControls(size.Width);

        if (openingCard > 0)
        {
            openingCard -= (float)Game.UpdateTime.Elapsed.TotalSeconds;
            DebugText.Print("LE CABINET DES LUMIERES", new Int2(centre.X - 130, 120));
            DebugText.Print("vingt pieces sur ce que fait la lumiere quand personne ne la regarde", new Int2(centre.X - 300, 148));
            DebugText.Print("souris pour regarder - le reste des commandes est dans le coin en haut a droite", new Int2(centre.X - 300, 176));
        }

        if (nearest is null)
            return;

        DebugText.Print($"{nearest.Number:00}   {nearest.Title}", new Int2(centre.X - 220, centre.Y + 150));
        DebugText.Print(nearest.Caption, new Int2(centre.X - 220, centre.Y + 176));

        if (!nearest.IsInteractive)
        {
            DebugText.Print("vitrine scellee", new Int2(centre.X - 220, centre.Y + 204));
            DrawMaterials(nearest, centre);
            return;
        }

        DebugText.Print($"[{InteractKey}] {nearest.Prompt}", new Int2(centre.X - 220, centre.Y + 204));
        DrawMaterials(nearest, centre);

        if (Input.IsKeyPressed(InteractKey))
            nearest.Press();
    }

    /// <summary>The alcove's own shell, which every case shares and nobody came to read.</summary>
    private static readonly string[] CaseParts = { "Back", "Top", "Plinth", "Slot", "Glass" };

    /// <summary>
    /// The surface values of whatever is actually on display, under the plaque.
    /// </summary>
    /// <remarks>
    /// The hall is meant to be borrowed from: someone who likes how a ball reads here should be
    /// able to walk up to it and copy the three numbers that make it, rather than guess at them
    /// from a screenshot. The case's own shell is filtered out by name - every alcove is built from
    /// the same plaster and stone, and repeating it under twenty plaques would bury the one line
    /// that differs.
    /// </remarks>
    private void DrawMaterials(GalleryExhibit exhibit, Int2 centre)
    {
        if (GalleryScene.Palette is not { } palette)
            return;

        var seen = new List<GalleryPalette.MaterialSpec>();

        void Collect(Entity entity, bool root)
        {
            if (!root && Array.IndexOf(CaseParts, entity.Name) >= 0)
                return;

            if (entity.Get<ModelComponent>()?.Model is { } model)
            {
                foreach (var instance in model.Materials)
                {
                    if (palette.Describe(instance.Material) is { } spec && !seen.Contains(spec))
                        seen.Add(spec);
                }
            }

            foreach (var child in entity.GetChildren())
                Collect(child, false);
        }

        Collect(exhibit.Entity, true);

        var y = centre.Y + 236;
        foreach (var spec in seen.Take(4))
        {
            DebugText.Print(spec.ToString(), new Int2(centre.X - 220, y));
            y += 18;
        }
    }

    /// <summary>Right-aligned against the edge of the back buffer, whatever it is.</summary>
    private void DrawControls(int width)
    {
        var longest = 0;
        foreach (var control in Controls)
            longest = Math.Max(longest, control.Length);

        var x = width - longest * Glyph - 16;
        var y = 16;

        foreach (var control in Controls)
        {
            DebugText.Print(control, new Int2(x, y));
            y += 18;
        }
    }

    private void Collect(Scene? scene)
    {
        if (scene is null)
            return;

        foreach (var entity in scene.Entities)
            Collect(entity);
    }

    private void Collect(Entity entity)
    {
        if (entity.Get<GalleryExhibit>() is { } exhibit)
            exhibits.Add(exhibit);

        foreach (var child in entity.GetChildren())
            Collect(child);
    }
}
