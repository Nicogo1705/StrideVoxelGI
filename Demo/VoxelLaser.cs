using System;
using Stride.Core.Mathematics;
using Stride.Extensions;
using Stride.Engine;
using Stride.Graphics;
using Stride.Graphics.GeometricPrimitives;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

namespace Demo;

/// <summary>
/// Draws the aiming ray, and a bead where it lands.
/// </summary>
/// <remarks>
/// A tool that misses and a tool that fires nowhere look the same from behind the mouse. This makes
/// the ray a thing on screen: if the beam does not go where the view points, the aim is wrong; if it
/// goes there and stops short, something is in the way; if it reaches and no bead appears, nothing
/// was hit at all.
/// </remarks>
public sealed class VoxelLaser
{
    private readonly Entity beam;
    private readonly Entity bead;

    public VoxelLaser(GraphicsDevice device, Scene scene)
    {
        // Emissive, so the beam reads the same whatever the lighting does - and needs no specular
        // environment term, which a material built at runtime does not get.
        var material = Material.New(device, new MaterialDescriptor
        {
            Attributes =
            {
                Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(new Color4(0.02f, 0.02f, 0.02f, 1f))),
                DiffuseModel = new MaterialDiffuseLambertModelFeature(),
                Emissive = new MaterialEmissiveMapFeature(new ComputeColor(new Color4(1f, 0.25f, 0.15f, 1f)))
                {
                    Intensity = new ComputeFloat(6f),
                },
            },
        });

        var cube = new Model { material, new Mesh { Draw = GeometricPrimitive.Cube.New(device).ToMeshDraw() } };
        var sphere = new Model { material, new Mesh { Draw = GeometricPrimitive.Sphere.New(device, 1f, 12).ToMeshDraw() } };

        beam = new Entity("Laser") { new ModelComponent(cube) };
        bead = new Entity("LaserHit") { new ModelComponent(sphere) };

        scene.Entities.Add(beam);
        scene.Entities.Add(bead);
    }

    /// <summary>Points the beam along a ray, and puts the bead at the hit - or hides it.</summary>
    public void Aim(Vector3 origin, Vector3 direction, float length, Vector3? hit)
    {
        // The cube is a unit cube centred on itself, so half the length either side of the middle.
        beam.Transform.Position = origin + direction * (length * 0.5f);
        beam.Transform.Rotation = LookAlong(direction);
        beam.Transform.Scale = new Vector3(0.03f, 0.03f, length);

        if (hit is { } point)
        {
            bead.Transform.Position = point;
            bead.Transform.Scale = new Vector3(0.18f);
        }
        else
        {
            bead.Transform.Scale = Vector3.Zero;
        }
    }

    /// <summary>Rotation whose local Z runs along a direction. The cube is stretched on Z.</summary>
    private static Quaternion LookAlong(Vector3 direction)
    {
        var yaw = MathF.Atan2(direction.X, direction.Z);
        var pitch = -MathF.Asin(MathUtil.Clamp(direction.Y, -1f, 1f));
        return Quaternion.RotationYawPitchRoll(yaw, pitch, 0);
    }
}
