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

    private readonly List<GalleryExhibit> exhibits = new();
    private float openingCard = 7f;

    public override void Start()
    {
        Shell.DemoOverlay.Register("gallery-card", Shell.OverlayAnchor.Centre, () => cardLines, offset: new Int2(-300, -300));
        Shell.DemoOverlay.Register("gallery-caption", Shell.OverlayAnchor.Centre, () => captionLines, offset: new Int2(-220, 150));
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

        // The keys are the shell's list, along the bottom like every other demo's; the card only
        // says where to look.
        cardLines.Clear();
        if (openingCard > 0)
        {
            openingCard -= (float)Game.UpdateTime.Elapsed.TotalSeconds;
            cardLines.Add("          THE CABINET OF LIGHTS");
            cardLines.Add("twenty pieces on what light does when nobody is looking at it");
            cardLines.Add("mouse to look around - the rest of the controls are down the right edge");
        }

        captionLines.Clear();
        if (nearest is null)
            return;

        captionLines.Add($"{nearest.Number:00}   {nearest.Title}");
        captionLines.Add(nearest.Caption);
        captionLines.Add(nearest.IsInteractive ? $"[{InteractKey}] {nearest.Prompt}" : "case sealed");
        captionLines.Add("");
        DrawMaterials(nearest);

        if (nearest.IsInteractive && Input.IsKeyPressed(InteractKey))
            nearest.Press();
    }

    private readonly List<string> cardLines = new();
    private readonly List<string> captionLines = new();

    public override void Cancel()
    {
        Shell.DemoOverlay.Unregister("gallery-card");
        Shell.DemoOverlay.Unregister("gallery-caption");
        base.Cancel();
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
    private void DrawMaterials(GalleryExhibit exhibit)
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

        foreach (var spec in seen.Take(4))
            captionLines.Add(spec.ToString());
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
