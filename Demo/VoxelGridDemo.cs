using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
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
using Stride.Rendering.Colors;
using Stride.Rendering.Lights;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Stride.Rendering.ProceduralModels;
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
    /// <summary>
    /// Samples along each axis, and the world size of one cell. Both settable, because the pair is
    /// the whole cost model and it is worth being able to move it from the command line.
    /// </summary>
    /// <remarks>
    /// A dense grid is cubic in its resolution: making the terrain ten times finer <em>and</em> ten
    /// times wider is a thousandfold in both memory and traversal, which 65 turns into 6401 - some
    /// 262 billion samples, a petabyte of buffer. Nothing about the collider or the traced renderer
    /// forbids it; a single dense array is what forbids it. Past a few hundred a side the answer is
    /// several grids side by side, each its own entity, which both of them already support.
    /// </remarks>
    public static int Samples = 129;
    public static float CellSize = 0.125f;
    public const float IsoLevel = 0.5f;

    public static float Extent => (Samples - 1) * CellSize;

    /// <summary>Centre and radius of the sphere in the field, so a capture can frame it.</summary>
    public static Vector3 SphereCentre => new(Extent * 0.5f, Extent * 0.42f, Extent * 0.5f);

    public const float SphereRadius = 3.4f;

    /// <summary>
    /// A field worth looking at: rolling ground, a big sphere half sunk into it, and an arch, each
    /// with its own material id so the packed source has something to colour.
    /// </summary>
    public static ushort[] Generate()
    {
        var samples = new ushort[Samples * Samples * Samples];
        var centre = SphereCentre;

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
                    var sphere = Saturate((SphereRadius - (p - centre).Length()) * 0.9f);
                    var toCentre = new Vector2(p.X - centre.X, p.Z - centre.Z + 5.0f);
                    var ring = new Vector2(toCentre.Length() - 4.0f, p.Y - Extent * 0.30f);
                    var arch = Saturate((1.1f - ring.Length()) * 1.2f);

                    var density = MathF.Max(ground, MathF.Max(sphere, arch));
                    // Decided with a margin, not on equality. Where two of the three shapes have
                    // almost the same value - which is a whole shell around every intersection -
                    // comparing them directly flips from one cell to the next on nothing but the
                    // last decimal. The smooth surfaces blend that away; cubes show one colour per
                    // cell, so it appears as speckle exactly where two materials meet.
                    const float margin = 0.05f;
                    var material = density <= 0f ? 0
                                 : arch > sphere + margin && arch > ground + margin ? 3
                                 : sphere > ground + margin ? 2
                                 : 1;

                    var index = (x * Samples + y) * Samples + z;
                    samples[index] = (ushort)((byte)(Saturate(density) * 255f) | (material << 8));
                }
            }
        }
        return samples;
    }

    private static float Saturate(float value) => MathF.Max(0f, MathF.Min(1f, value));

    /// <summary>
    /// The same field again, as a 3D texture, for the material that draws it as a model.
    /// </summary>
    /// <remarks>
    /// The traced pass reads a structured buffer, and that works because an image effect binds its
    /// own resources. A material cannot: its resources go through the descriptor set the mesh render
    /// feature builds, and a structured buffer set on a material pass is carried there and bound to
    /// nothing - every density read comes back zero, so the ray crosses a field that is empty
    /// everywhere and there is no surface to find. Nothing reports it; the buffer is present in the
    /// material's parameters when asked. A texture is the resource a material does bind, which is
    /// why the field goes down this path twice.
    /// </remarks>
    public static Texture CreateTexture(IGame game, ushort[] samples)
    {
        // Written in the texture's order, not the array's.
        //
        // The samples are indexed with z varying fastest; a 3D texture is laid out with x varying
        // fastest. Copying one into the other straight through transposes the field, and a
        // transposed field still draws - in bands, because along any ray the samples jump about -
        // which reads as an artefact of the tracing rather than as the data being wrong.
        gpuTexels = new byte[samples.Length * 4];
        for (int x = 0; x < Samples; ++x)
            for (int y = 0; y < Samples; ++y)
                for (int z = 0; z < Samples; ++z)
                    WriteTexel(gpuTexels, (z * Samples + y) * Samples + x, samples[(x * Samples + y) * Samples + z]);


        // Created empty and filled afterwards, rather than handed its data here: a texture given its
        // contents at creation is immutable, and the first dig then throws from inside SetData -
        // far from the line that decided it.
        gpuTexture = Texture.New3D(
            game.GraphicsDevice, Samples, Samples, Samples, PixelFormat.R8G8B8A8_UNorm,
            TextureFlags.ShaderResource, GraphicsResourceUsage.Default);
        UploadTexture(game.GraphicsContext.CommandList);
        return gpuTexture;
    }

    /// <summary>
    /// Pushes the whole field to the GPU, the way a texture in default usage takes it.
    /// </summary>
    /// <remarks>
    /// The whole field, because an edit is a ball and the array is contiguous in a different order
    /// than the region would be. Small enough at this size that a partial update is not worth the
    /// arithmetic; a game with real chunks would upload the box it touched.
    /// </remarks>
    private static void UploadTexture(CommandList commandList)
    {
        if (gpuTexture != null && gpuTexels != null)
            gpuTexture.SetData(commandList, gpuTexels);
    }

    /// <summary>Density in red, the material's colour in the rest - what the texture source reads.</summary>
    private static void WriteTexel(byte[] texels, int index, ushort packed)
    {
        var colour = MaterialColour(packed >> 8);
        texels[index * 4 + 0] = (byte)(packed & 0xFF);
        texels[index * 4 + 1] = (byte)(colour.X * 255f);
        texels[index * 4 + 2] = (byte)(colour.Y * 255f);
        texels[index * 4 + 3] = (byte)(colour.Z * 255f);
    }

    /// <summary>The palette the packed buffer's shader computes, worked out here instead.</summary>
    private static Vector3 MaterialColour(int material)
    {
        if (material == 0)
            return Vector3.Zero;

        var hue = (material * 0.6180339887f) % 1f;
        return new Vector3(
            Saturate(MathF.Abs(((hue + 0.0000f) % 1f) * 6f - 3f) - 1f),
            Saturate(MathF.Abs(((hue + 0.6666f) % 1f) * 6f - 3f) - 1f),
            Saturate(MathF.Abs(((hue + 0.3333f) % 1f) * 6f - 3f) - 1f));
    }

    /// <summary>Widens the samples for the structured buffer the packed source reads.</summary>
    public static GraphicsBuffer CreateBuffer(GraphicsDevice device, ushort[] samples)
    {
        gpuSamples = new uint[samples.Length];
        for (int i = 0; i < samples.Length; ++i)
            gpuSamples[i] = samples[i];
        return gpuBuffer = GraphicsBuffer.Structured.New(device, gpuSamples, false);
    }

    /// <summary>
    /// Adds or removes a ball of material, and that is the whole of a terrain edit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One write per sample, into the array the renderer reads and into the collider, which reads
    /// the field on demand. Nothing is re-meshed, no tree is rebuilt, no collidable is re-created,
    /// and the bounds do not change - so the hole is solid on the same frame it is visible.
    /// </para>
    /// <para>
    /// The whole buffer goes back to the GPU here, which a real game would not do: it would upload
    /// the touched region, or hold the field in a 3D texture and write the box. At a megabyte a
    /// stroke it is not what this demo is trying to show.
    /// </para>
    /// </remarks>
    public static void Edit(IGame game, Vector3 centre, float radius, bool fill)
    {
        if (cachedSamples is null || gpuSamples is null || gpuBuffer is null || collider is null)
            return;

        // Sample coordinates are cell coordinates: sample (x, y, z) sits at x * CellSize.
        var inverse = 1f / CellSize;
        var lo = centre - new Vector3(radius);
        var hi = centre + new Vector3(radius);

        var x0 = Math.Max(0, (int)MathF.Floor(lo.X * inverse));
        var y0 = Math.Max(0, (int)MathF.Floor(lo.Y * inverse));
        var z0 = Math.Max(0, (int)MathF.Floor(lo.Z * inverse));
        var x1 = Math.Min(Samples - 1, (int)MathF.Ceiling(hi.X * inverse));
        var y1 = Math.Min(Samples - 1, (int)MathF.Ceiling(hi.Y * inverse));
        var z1 = Math.Min(Samples - 1, (int)MathF.Ceiling(hi.Z * inverse));

        var touched = false;

        for (int x = x0; x <= x1; ++x)
        {
            for (int y = y0; y <= y1; ++y)
            {
                for (int z = z0; z <= z1; ++z)
                {
                    var offset = new Vector3(x, y, z) * CellSize - centre;
                    var distance = offset.Length();
                    if (distance > radius)
                        continue;

                    var index = (x * Samples + y) * Samples + z;
                    var previous = cachedSamples[index];
                    var density = previous & 0xFF;
                    var material = previous & 0xFF00;

                    // Empty to the core, feathered only at the rim. Interpolating the whole ball
                    // towards zero instead moves most of it a little, and a sample has to cross the
                    // iso level to change anything at all - so on a thin shape the whole thickness
                    // drops below it and vanishes, while solid ground loses a pinprick at the exact
                    // centre and looks untouched. The rim is still soft, because the surface is an
                    // interpolation of these samples and a hard cut comes out with a step in it.
                    var edge = MathUtil.Clamp((distance - radius * 0.55f) / (radius * 0.45f), 0f, 1f);
                    var limit = (int)MathF.Round(255f * edge);

                    var target = fill
                        ? Math.Max(density, 255 - limit)
                        : Math.Min(density, limit);
                    target = Math.Clamp(target, 0, 255);
                    if (target == density)
                        continue;

                    // Filling empty space needs a material, or the new matter is drawn as air was.
                    if (fill && material == 0)
                        material = 2 << 8;

                    var packed = (ushort)(target | material);
                    cachedSamples[index] = packed;
                    gpuSamples[index] = packed;
                    if (gpuTexels != null)
                        WriteTexel(gpuTexels, (z * Samples + y) * Samples + x, packed);
                    collider.SetVoxel(x, y, z, packed);
                    touched = true;
                }
            }
        }

        if (!touched)
            return;

        gpuBuffer.SetData(game.GraphicsContext.CommandList, gpuSamples);
        UploadTexture(game.GraphicsContext.CommandList);

        // Physics already sees the edit; this is for anything that kept a copy of the surface, which
        // here is the F11 wireframe. Cheap on this collider - a shape slot swap, no tree rebuilt.
        collider.NotifyFieldChanged();
    }

    /// <summary>The traced pass's traversal, over the packed buffer. Outlives the scene, as the pass does.</summary>
    private static VoxelGridTraversalDDA? traversal;

    /// <summary>The model's own traversal, over the texture copy of the field. Follows every switch.</summary>
    private static VoxelGridTraversalDDA? modelTraversal;

    /// <summary>Collider form to start on, from --collider.</summary>
    public static VoxelChildForm StartColliderForm { get; set; } = VoxelChildForm.TriangleMarchingCubes;

    /// <summary>Draw the grid as a model, off with --no-model so the traced pass can be judged alone.</summary>
    public static bool StartWithModel { get; set; } = true;

    /// <summary>Start with the traced pass on, from --trace. Both paths draw the same surface, so off by default.</summary>
    public static bool StartWithTrace { get; set; }

    /// <summary>Show the collider wireframe from the first frame, from --wireframe.</summary>
    public static bool StartWithWireframe { get; set; }

    /// <summary>Surface to start on, from --surface. Lets an unattended capture photograph any of them.</summary>
    public static VoxelSurfaceForm StartSurface { get; set; } = VoxelSurfaceForm.MarchingCubes;

    /// <summary>What the digging tool is pointing at, shown by the shell with everything else.</summary>
    public static string AimStatus { get; set; } = "nothing";

    /// <summary>Which surface the traced pass stops on.</summary>
    public static VoxelSurfaceForm Surface
    {
        get => traversal?.Surface ?? VoxelSurfaceForm.MarchingCubes;
        set
        {
            // Both paths, or B changes the traced pass and leaves the model where it was - which
            // reads as the model ignoring the key rather than as a second traversal nobody told.
            if (traversal is not null) traversal.Surface = value;
            if (modelTraversal is not null) modelTraversal.Surface = value;
        }
    }

    /// <summary>Steps to the next drawn surface.</summary>
    public static void CycleSurface() => Surface = Surface switch
    {
        VoxelSurfaceForm.Cubes => VoxelSurfaceForm.MarchingCubes,
        VoxelSurfaceForm.MarchingCubes => VoxelSurfaceForm.SurfaceNets,
        _ => VoxelSurfaceForm.Cubes,
    };

    /// <summary>
    /// What the collider presents to the narrow phase.
    /// </summary>
    /// <remarks>
    /// Worth pairing deliberately with <see cref="Surface"/>. Cubes against a box collider agree
    /// exactly, and so do the two marching-cubes forms; surface nets is a third surface, and pairing
    /// it with either of the others draws one body and collides with another - which is visible the
    /// moment both are on screen at once.
    /// </remarks>
    public static VoxelChildForm ColliderForm
    {
        get => collider?.Form ?? VoxelChildForm.TriangleMarchingCubes;
        set { if (collider is not null) collider.Form = value; }
    }

    /// <summary>Steps to the next collider form, through the three that have a drawn counterpart.</summary>
    public static void CycleColliderForm() => ColliderForm = ColliderForm switch
    {
        VoxelChildForm.Box => VoxelChildForm.TriangleMarchingCubes,
        VoxelChildForm.TriangleMarchingCubes => VoxelChildForm.TriangleSurfaceNets,
        VoxelChildForm.TriangleSurfaceNets => VoxelChildForm.Sphere,
        _ => VoxelChildForm.Box,
    };

    /// <summary>Sets both to the pair that describes the same body.</summary>
    public static void MatchColliderToSurface() => ColliderForm = Surface switch
    {
        VoxelSurfaceForm.Cubes => VoxelChildForm.Box,
        VoxelSurfaceForm.SurfaceNets => VoxelChildForm.TriangleSurfaceNets,
        _ => VoxelChildForm.TriangleMarchingCubes,
    };

    /// <summary>Whether the traced pass draws. Owned by the shell, since the pass outlives the scene.</summary>
    public static bool PassEnabled
    {
        get => VoxelGridPass.Enabled;
        set => VoxelGridPass.Enabled = value;
    }

    private static ushort[]? cachedSamples;
    private static bool passInstalled;

    private static uint[]? gpuSamples;
    private static byte[]? gpuTexels;
    private static Texture? gpuTexture;
    private static GraphicsBuffer? gpuBuffer;
    private static VoxelCollider? collider;

    /// <summary>Scaffolding: carve a fixed trench after this many frames, for an unattended capture.</summary>
    public static int AutoDigAfterFrames { get; set; }

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

    private static VoxelGridRenderer? renderer;

    public static void Build(Game game, Entity camera)
        => BuildScene(game, game.SceneSystem.SceneInstance.RootScene, camera, cachedSamples ??= Generate());

    private static void InstallPass(Game game, ushort[] samples)
    {
        renderer = new VoxelGridRenderer
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
                // Long enough to cross the grid corner to corner, whatever it was sized at: a walk
                // that runs out of steps stops mid-field and punches a hole in the surface.
                MaxSteps = Samples * 3 + 64,
                Surface = StartSurface,
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
        collider = new VoxelCollider
        {
            // Matched to the drawn surface by default; X keeps them matched, C lets them differ.
            Form = StartColliderForm,
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

        // -- the same field, drawn the way a model is drawn -----------------------------------
        if (StartWithModel)
        terrain.Add(new VoxelGridComponent
        {
            Traversal = modelTraversal = new VoxelGridTraversalDDA
            {
                Source = new VoxelGridSourceTexture3D
                {
                    Texture = CreateTexture(game, samples),
                    SampleCount = new Int3(Samples, Samples, Samples),
                },
                CellSize = CellSize,
                IsoLevel = IsoLevel,
                MaxSteps = Samples * 3 + 64,
                Surface = StartSurface,
            },
            CastShadows = true,
        });

        // -- a light, because the drawn grid now needs one ------------------------------------
        // The traced pass lit itself from a direction it kept in its own parameters, which is exactly
        // the sort of thing that stops a voxel from matching the scene around it. This one is the
        // scene's light, and it lights both the grid and the mesh below.
        var sun = new Entity("Sun")
        {
            new LightComponent
            {
                Type = new LightDirectional
                {
                    Color = new ColorRgbProvider(new Color3(1f, 0.95f, 0.85f)),
                    Shadow =
                    {
                        Enabled = true,
                        Size = LightShadowMapSize.Large,
                        Filter = new LightShadowMapFilterTypePcf(),
                        // A little more than the default, not three times it. A surface found by a
                        // ray shadows itself along a sawtooth at grazing angles without some bias -
                        // but the normal offset pushes the receiver towards the light, and at 30 it
                        // ate the thin contact shadow a half-sunk ball leaves at its own base.
                        BiasParameters = { DepthBias = 0.015f, NormalOffsetScale = 12f },
                    },
                },
                Intensity = 12f,
            },
        };
        sun.Transform.Rotation = Quaternion.RotationYawPitchRoll(0.9f, -0.85f, 0);
        scene.Entities.Add(sun);

        scene.Entities.Add(new Entity("Ambient")
        {
            new LightComponent
            {
                Type = new LightAmbient { Color = new ColorRgbProvider(new Color3(0.30f, 0.34f, 0.42f)) },
                Intensity = 1f,
            },
        });

        // -- an ordinary mesh, standing half inside the volume ---------------------------------
        // The test of the whole thing. If the voxel surface is a surface like any other, this sphere
        // is cut by it where it enters, is shadowed by it, and casts its own shadow onto it - with
        // nothing written anywhere to make those three happen.
        scene.Entities.Add(BuildReferenceMesh(game));

        // F11 draws every collidable as wireframe - which for the terrain means the collider's own
        // AppendModel, so this is also the test of that.
        scene.Entities.Add(new Entity("VoxelDebug") { new DebugRenderComponent { Visible = StartWithWireframe } });

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
        camera.Add(new VoxelDigger { AutoDigAfterFrames = AutoDigAfterFrames });
    }

    /// <summary>A plain mesh, placed so it meets the voxel surface rather than sitting clear of it.</summary>
    private static Entity BuildReferenceMesh(Game game)
    {
        var descriptor = new MaterialDescriptor
        {
            Attributes =
            {
                Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(new Color4(0.85f, 0.45f, 0.25f, 1f))),
                DiffuseModel = new MaterialDiffuseLambertModelFeature(),
                Specular = new MaterialMetalnessMapFeature(new ComputeFloat(0f)),
                SpecularModel = new MaterialSpecularMicrofacetModelFeature
                {
                    // The lookup table is a texture the pipeline binds for a material that came from
                    // an asset; one built here at runtime has none, and metal reads as black.
                    Environment = new MaterialSpecularMicrofacetEnvironmentGGXPolynomial(),
                },
                MicroSurface = new MaterialGlossinessMapFeature(new ComputeFloat(0.5f)),
            },
        };

        var material = Material.New(game.GraphicsDevice, descriptor);
        var sphere = new SphereProceduralModel { Radius = 2.2f, Tessellation = 32, MaterialInstance = { Material = material } };
        var model = (Model)sphere.Generate(game.Services);

        var entity = new Entity("ReferenceMesh") { new ModelComponent(model) { IsShadowCaster = true } };
        entity.Transform.Position = new Vector3(Extent * 0.5f + 4.5f, Extent * 0.46f, Extent * 0.5f - 4.5f);
        return entity;
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
