using System;
using System.Collections.Generic;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Debug;
using Stride.BepuPhysics.Definitions.Colliders;
using Stride.BepuPhysics.Definitions.Colliders.Voxels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Voxels.Grid;
using GraphicsBuffer = Stride.Graphics.Buffer;

namespace Demo;

/// <summary>
/// A procedural voxel field, drawn by tracing it rather than meshing it, collided against by the
/// voxel collider, and shown as wireframe on F11.
/// </summary>
/// <remarks>
/// The two halves of the same data: Stride.Voxels' grid layer walks it on the GPU to put it on
/// screen, and Stride.BepuPhysics' VoxelCollider walks the same samples on the CPU to make it solid.
/// Neither produces a triangle.
/// </remarks>
public static class VoxelGridDemo
{
    public const int Samples = 65;
    public const float CellSize = 0.25f;
    public const float IsoLevel = 0.5f;

    public static float Extent => (Samples - 1) * CellSize;

    /// <summary>
    /// A field worth looking at: rolling ground, a big sphere half sunk into it, and an arch, each
    /// with its own material id so the packed source has something to colour.
    /// </summary>
    public static ushort[] Generate()
    {
        var samples = new ushort[Samples * Samples * Samples];
        var centre = new Vector3(Extent * 0.5f, Extent * 0.42f, Extent * 0.5f);

        for (int x = 0; x < Samples; ++x)
        {
            for (int y = 0; y < Samples; ++y)
            {
                for (int z = 0; z < Samples; ++z)
                {
                    var p = new Vector3(x, y, z) * CellSize;

                    // Ground: two sine ridges crossing, so the surface is never flat enough to hide
                    // a normal that is wrong.
                    var height = Extent * 0.28f
                                 + MathF.Sin(p.X * 0.55f) * 0.9f
                                 + MathF.Cos(p.Z * 0.42f) * 1.1f
                                 + MathF.Sin((p.X + p.Z) * 0.23f) * 0.7f;
                    var ground = Saturate((height - p.Y) * 0.9f);

                    // A sphere sitting in the ground, and an arch crossing it: a torus cut in half
                    // by the ground plane reads as an arch from any angle.
                    var sphere = Saturate((3.4f - (p - centre).Length()) * 0.9f);
                    var toCentre = new Vector2(p.X - centre.X, p.Z - centre.Z + 5.0f);
                    var ring = new Vector2(toCentre.Length() - 4.0f, p.Y - Extent * 0.30f);
                    var arch = Saturate((1.1f - ring.Length()) * 1.2f);

                    var density = MathF.Max(ground, MathF.Max(sphere, arch));
                    var material = density <= 0f ? 0
                                 : arch >= sphere && arch >= ground ? 3
                                 : sphere >= ground ? 2
                                 : 1;

                    var index = (x * Samples + y) * Samples + z;
                    samples[index] = (ushort)((byte)(Saturate(density) * 255f) | (material << 8));
                }
            }
        }
        return samples;
    }

    private static float Saturate(float value) => MathF.Max(0f, MathF.Min(1f, value));

    /// <summary>Widens the samples for the structured buffer the packed source reads.</summary>
    public static GraphicsBuffer CreateBuffer(GraphicsDevice device, ushort[] samples)
    {
        var widened = new uint[samples.Length];
        for (int i = 0; i < samples.Length; ++i)
            widened[i] = samples[i];
        return GraphicsBuffer.Structured.New(device, widened, false);
    }

    /// <summary>Whether the traced pass draws. Owned by the shell, since the pass outlives the scene.</summary>
    private static VoxelGridTraversalDDA? traversal;

    /// <summary>Smooth iso-surface, or cubes. Both walk the same cells.</summary>
    public static bool Smooth
    {
        get => traversal?.Smooth ?? true;
        set { if (traversal is not null) traversal.Smooth = value; }
    }

    public static bool PassEnabled
    {
        get => VoxelGridPass.Enabled;
        set => VoxelGridPass.Enabled = value;
    }

    private static ushort[]? cachedSamples;
    private static bool passInstalled;

    /// <summary>
    /// Puts the traced pass in the compositor, switched off. Called once, before the game loop.
    /// </summary>
    /// <remarks>
    /// Before the loop and not on entering the demo: the compositor's renderers are walked while a
    /// frame is being drawn, and adding one from a script means adding it to a list something else
    /// is enumerating.
    /// </remarks>
    public static void InstallPass(Game game)
    {
        if (passInstalled)
            return;
        passInstalled = true;

        var samples = cachedSamples ??= Generate();
        InstallPass(game, samples);
        PassEnabled = false;
    }

    public static void Build(Game game, Entity camera)
        => BuildScene(game, game.SceneSystem.SceneInstance.RootScene, camera, cachedSamples ??= Generate());

    private static void InstallPass(Game game, ushort[] samples)
    {
        var renderer = new VoxelGridRenderer
        {
            Traversal = traversal = new VoxelGridTraversalDDA
            {
                Source = new VoxelGridSourcePackedBuffer
                {
                    Data = CreateBuffer(game.GraphicsDevice, samples),
                    SampleCount = new Int3(Samples, Samples, Samples),
                },
                CellSize = CellSize,
                IsoLevel = IsoLevel,
                MaxSteps = 512,
            },
            World = Matrix.Identity,
            MaxDistance = 200f,
            DebugBounds = new Vector3(Extent, Extent, Extent),
        };

        AppendPass(game.SceneSystem.GraphicsCompositor.Game, new VoxelGridPass { Renderer = renderer });
    }

    private static void BuildScene(Game game, Scene scene, Entity camera, ushort[] samples)
    {
        // -- the same field, collided against ------------------------------------------------
        var collider = new VoxelCollider
        {
            Form = VoxelChildForm.TriangleSurfaceNets,
            CellSize = CellSize,
            IsoLevel = IsoLevel,
        };
        collider.SetData(Samples, Samples, Samples, samples);

        var terrain = new Entity("VoxelTerrain") { new StaticComponent { Collider = collider } };
        scene.Entities.Add(terrain);

        // Something to drop on it, so contacts are visible rather than asserted.
        var random = new Random(7);
        for (int i = 0; i < 12; ++i)
        {
            var body = new Entity($"Ball{i}")
            {
                new BodyComponent { Collider = new CompoundCollider { Colliders = { new SphereCollider { Radius = 0.35f } } } },
            };
            body.Transform.Position = new Vector3(
                Extent * 0.5f + (float)(random.NextDouble() - 0.5) * 6f,
                Extent * 0.85f + i * 0.8f,
                Extent * 0.5f + (float)(random.NextDouble() - 0.5) * 6f);
            scene.Entities.Add(body);
        }

        // F11 draws every collidable as wireframe - which for the terrain means the collider's own
        // AppendModel, so this is also the test of that.
        scene.Entities.Add(new Entity("VoxelDebug") { new DebugRenderComponent { Visible = false } });

        // -- camera ---------------------------------------------------------------------------
        // The shell's camera, moved and given flight controls. Creating one here would put a second
        // camera in a slot that holds one.
        if (camera.Get<CameraComponent>() is { } view)
        {
            view.VerticalFieldOfView = 65f;
            view.NearClipPlane = 0.1f;
            view.FarClipPlane = 400f;
        }

        camera.Transform.Position = new Vector3(Extent * 0.5f, Extent * 0.75f, -Extent * 0.35f);
        camera.Transform.Rotation = Quaternion.RotationYawPitchRoll(MathUtil.Pi, -0.45f, 0);
        camera.Add(new BasicCameraController());
    }

    /// <summary>
    /// Puts the pass inside the camera renderer, after whatever already draws the scene.
    /// </summary>
    /// <remarks>
    /// Inside matters: a renderer sitting beside the camera renderer rather than under it runs with
    /// no RenderView, so it has no camera to build rays from and quietly draws nothing. The search
    /// therefore descends to the SceneCameraRenderer and appends within it.
    /// </remarks>
    private static bool AppendPass(ISceneRenderer? renderer, ISceneRenderer pass)
    {
        switch (renderer)
        {
            case SceneCameraRenderer camera:
                if (camera.Child is SceneRendererCollection inner)
                {
                    inner.Children.Add(pass);
                }
                else
                {
                    var wrapper = new SceneRendererCollection();
                    wrapper.Children.Add(camera.Child);
                    wrapper.Children.Add(pass);
                    camera.Child = wrapper;
                }
                return true;

            case SceneRendererCollection collection:
                foreach (var child in collection.Children)
                {
                    if (AppendPass(child, pass))
                        return true;
                }
                return false;

            default:
                return false;
        }
    }
}

/// <summary>Draws the traced grid over whatever the forward renderer produced.</summary>
public class VoxelGridPass : SceneRendererBase
{
    public static bool Enabled = true;

    public VoxelGridRenderer? Renderer;

    protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
    {
        if (!Enabled || Renderer is null)
            return;

        var output = drawContext.CommandList.RenderTarget;
        if (output is null)
            return;

        var depth = drawContext.CommandList.DepthStencilBuffer;
        if (depth is not null)
            Renderer.SetDepthOutput(depth, output);
        else
            Renderer.SetOutput(output);

        Renderer.Draw(drawContext);
    }
}
