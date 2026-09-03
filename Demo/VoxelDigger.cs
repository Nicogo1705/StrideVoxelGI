using System;
using System.Collections.Generic;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions.Colliders;
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
    public float Radius { get; set; } = 1.6f;

    /// <summary>How far the aim reaches.</summary>
    public float Reach { get; set; } = 80f;

    /// <summary>Seconds between two edits while a button is held.</summary>
    public float Interval { get; set; } = 0.06f;

    /// <summary>
    /// Frames to wait before carving a fixed trench with no input at all. Zero leaves it to the
    /// mouse; anything else lets an automatic capture photograph an edited field.
    /// </summary>
    public int AutoDigAfterFrames { get; set; }

    private readonly List<HitInfo> hits = [];
    private string status = "nothing";
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

        // The shell owns the top of the screen and lays its lines out one after another; a fixed
        // position here lands on top of whichever line happens to be there.
        VoxelGridDemo.AimStatus = status;

        var simulation = Entity.GetSimulation();
        if (simulation is null)
            return;

        Entity.Transform.UpdateWorldMatrix();
        var origin = Entity.Transform.WorldMatrix.TranslationVector;
        var forward = -Vector3.Normalize(new Vector3(
            Entity.Transform.WorldMatrix.M31,
            Entity.Transform.WorldMatrix.M32,
            Entity.Transform.WorldMatrix.M33));

        // Every hit along the ray, not just the first: the balls resting on the terrain are hit
        // before it is, and digging where a ball happens to be does nothing at all - which is what
        // a tool that works only half the time feels like.
        hits.Clear();
        simulation.RayCastPenetrating(origin, forward, Reach, hits);

        HitInfo? terrain = null;
        foreach (var candidate in hits)
        {
            if (candidate.Collidable.Collider is not VoxelCollider)
                continue;
            if (terrain is null || candidate.Distance < terrain.Value.Distance)
                terrain = candidate;
        }

        // The aim is the reticle at the centre of the screen; what it lands on is said in words.
        status = terrain is { } aimed
            ? $"terrain at {aimed.Distance:0.0} m"
            : hits.Count > 0 ? "something else in the way" : "nothing";

        var dig = Input.IsMouseButtonDown(MouseButton.Left);
        var fill = Input.IsKeyDown(Keys.F);
        if ((!dig && !fill) || cooldown > 0f || terrain is not { } hit)
            return;

        cooldown = Interval;

        var centre = hit.Point + (fill ? hit.Normal * (Radius * 0.6f) : Vector3.Zero);
        VoxelGridDemo.Edit(Game, centre, Radius, fill);
    }
}
