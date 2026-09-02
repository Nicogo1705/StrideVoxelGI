using System;
using Stride.BepuPhysics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;

namespace Demo;

/// <summary>
/// Digs into the voxel field, and fills it back in.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole argument of the grid layer, made visible: editing terrain is a write into the
/// field. Nothing is re-meshed, no acceleration structure is rebuilt, no collider is re-created and
/// the collidable's bounds do not move. The renderer traces the samples and the narrow phase reads
/// the same samples, so both see the hole on the next frame - the drawn surface and the solid one
/// cannot drift apart, because there is only one of them.
/// </para>
/// <para>
/// The aim is taken with a physics ray against the voxel collider, which walks the grid on the CPU
/// exactly as the shader walks it on the GPU. Digging therefore exercises that path too.
/// </para>
/// </remarks>
public sealed class VoxelDigger : SyncScript
{
    /// <summary>Radius of the sphere added or removed, in world units.</summary>
    public float Radius { get; set; } = 1.1f;

    /// <summary>How far the aim reaches.</summary>
    public float Reach { get; set; } = 80f;

    /// <summary>Seconds between two edits while a button is held.</summary>
    public float Interval { get; set; } = 0.06f;

    /// <summary>
    /// Frames to wait before carving a fixed trench with no input at all. Zero leaves it to the
    /// mouse; anything else lets an automatic capture photograph an edited field.
    /// </summary>
    public int AutoDigAfterFrames { get; set; }

    private float cooldown;
    private int frame;
    private bool autoDug;

    public override void Update()
    {
        if (AutoDigAfterFrames > 0 && !autoDug && ++frame >= AutoDigAfterFrames)
        {
            autoDug = true;
            var extent = VoxelGridDemo.Extent;
            // A shaft straight down through the terrain, so the hole is unmistakable from any angle.
            for (int i = 0; i < 12; ++i)
            {
                var point = new Vector3(
                    extent * 0.32f,
                    extent * (0.55f - 0.035f * i),
                    extent * 0.38f);
                VoxelGridDemo.Edit(Game, point, 1.5f, fill: false);
            }
        }

        cooldown -= (float)Game.UpdateTime.Elapsed.TotalSeconds;

        var dig = Input.IsMouseButtonDown(MouseButton.Left);
        var fill = Input.IsKeyDown(Keys.F);
        if ((!dig && !fill) || cooldown > 0f)
            return;

        cooldown = Interval;

        var simulation = Entity.GetSimulation();
        if (simulation is null)
            return;

        Entity.Transform.UpdateWorldMatrix();
        var origin = Entity.Transform.WorldMatrix.TranslationVector;
        var forward = -Vector3.Normalize(new Vector3(
            Entity.Transform.WorldMatrix.M31,
            Entity.Transform.WorldMatrix.M32,
            Entity.Transform.WorldMatrix.M33));

        if (!simulation.RayCast(origin, forward, Reach, out var hit))
            return;

        // Filling reaches back along the ray, so material is added onto the surface rather than
        // inside it - digging one cell deep and filling one cell out are not the same point.
        var centre = hit.Point + (fill ? hit.Normal * (Radius * 0.6f) : Vector3.Zero);
        VoxelGridDemo.Edit(Game, centre, Radius, fill);
    }
}
