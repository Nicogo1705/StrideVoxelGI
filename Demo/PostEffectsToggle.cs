using System.Collections.Generic;
using Stride.Engine;
using Stride.Input;
using Stride.Rendering.Compositing;
using Stride.Rendering.Images;

namespace Demo;

/// <summary>
/// Turns bloom and antialiasing off and on at runtime, to bisect a screen artefact by hand.
/// </summary>
/// <remarks>
/// An artefact that only appears from certain viewpoints cannot be chased by reasoning about
/// shaders: the question "is this the bloom chain or not" is answered by switching the bloom chain
/// off while the artefact is on screen, and nothing else answers it as directly.
/// </remarks>
public class PostEffectsToggle : SyncScript
{
    /// <summary>Cycles: everything on, bloom off, antialiasing off, both off.</summary>
    public Keys CycleKey { get; set; } = Keys.B;

    /// <summary>Require Ctrl, so the key does not collide with walking.</summary>
    public bool RequireControl { get; set; }

    private PostProcessingEffects? effects;
    private int state;

    public override void Start()
    {
        effects = FindPostEffects(SceneSystem.GraphicsCompositor?.Game);
    }

    public override void Update()
    {
        if (effects is null)
            return;

        var modifier = !RequireControl || Input.IsKeyDown(Keys.LeftCtrl) || Input.IsKeyDown(Keys.RightCtrl);

        if (modifier && Input.IsKeyPressed(CycleKey))
        {
            state = (state + 1) % 4;

            effects.Bloom.Enabled = state is 0 or 2;
            if (effects.Antialiasing is { } antialiasing)
                antialiasing.Enabled = state is 0 or 1;
        }

        DebugText.Print($"[{CycleKey}] Post          : bloom {(effects.Bloom.Enabled ? "on " : "off")}  antialiasing {(effects.Antialiasing?.Enabled == true ? "on" : "off")}", new Stride.Core.Mathematics.Int2(16, 232));
    }

    /// <summary>
    /// Walks the compositor for the renderer that owns the post effects. It is not always the one
    /// drawing the scene - here the forward renderer's own PostEffects is null and a separate
    /// renderer holds them - so the graph is searched rather than assumed.
    /// </summary>
    public static PostProcessingEffects? FindPostEffects(ISceneRenderer? renderer)
    {
        switch (renderer)
        {
            case null:
                return null;

            case SceneRendererCollection collection:
                foreach (var child in collection.Children)
                {
                    if (FindPostEffects(child) is { } found)
                        return found;
                }

                return null;

            case SceneCameraRenderer camera:
                return FindPostEffects(camera.Child);

            case ForwardRenderer forward:
                return forward.PostEffects as PostProcessingEffects;

            default:
                return null;
        }
    }
}
