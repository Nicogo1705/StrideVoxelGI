using System;
using Stride.Core.Mathematics;
using Stride.Engine;

namespace Demo.Gallery;

/// <summary>
/// A plaque on an exhibit. Closed ones only carry their text; the rest answer to a key.
/// </summary>
public class GalleryExhibit : SyncScript
{
    /// <summary>Number on the plaque, in the order the visitor meets them.</summary>
    public int Number { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>One line on what there is to look at. Not what it is made of - what it does.</summary>
    public string Caption { get; set; } = string.Empty;

    /// <summary>What the key does, for the ten that answer. Empty for a sealed case.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Called on the key press, with how many times it has been pressed.</summary>
    public Action<GalleryExhibit, int>? Interact { get; set; }

    /// <summary>Called every frame for the ones that move.</summary>
    public Action<GalleryExhibit, float>? Animate { get; set; }

    /// <summary>Where the visitor has to stand to read it.</summary>
    public Vector3 Focus { get; set; }

    /// <summary>Whether the moving parts are moving. The three that animate toggle this.</summary>
    public bool Running { get; set; }

    public bool IsInteractive => Interact is not null;

    private int presses;
    private float time;

    public override void Update()
    {
        if (Animate is null)
            return;

        time += (float)Game.UpdateTime.Elapsed.TotalSeconds;
        Animate(this, time);
    }

    public void Press()
    {
        if (Interact is null)
            return;

        presses++;
        Interact(this, presses);
    }
}
