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
using Stride.Rendering.Voxels;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Voxels.Grid;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;
using StrideVoxelGI;
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
    /// with its own material id so the packed source has something to colour, and a white lamp
    /// pillar in each corner.
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

                    // A white pillar in each corner: a lamp per corner, so the far reaches of the
                    // field get light from somewhere when the arch is the only other emitter.
                    var inset = 1.5f;
                    var cornerX = MathF.Min(MathF.Abs(p.X - inset), MathF.Abs(p.X - (Extent - inset)));
                    var cornerZ = MathF.Min(MathF.Abs(p.Z - inset), MathF.Abs(p.Z - (Extent - inset)));
                    var pillarRadial = 0.7f - MathF.Max(cornerX, cornerZ);
                    var pillarVertical = MathF.Min(p.Y, Extent * 0.55f - p.Y);
                    var pillar = Saturate(MathF.Min(pillarRadial, pillarVertical) * 1.2f);

                    var density = MathF.Max(MathF.Max(ground, pillar), MathF.Max(sphere, arch));
                    // Decided with a margin, not on equality. Where two of the three shapes have
                    // almost the same value - which is a whole shell around every intersection -
                    // comparing them directly flips from one cell to the next on nothing but the
                    // last decimal. The smooth surfaces blend that away; cubes show one colour per
                    // cell, so it appears as speckle exactly where two materials meet.
                    const float margin = 0.05f;
                    var material = density <= 0f ? 0
                                 : pillar > ground + margin ? 4
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
        gpuTexels = new byte[samples.Length * 2];
        for (int x = 0; x < Samples; ++x)
            for (int y = 0; y < Samples; ++y)
                for (int z = 0; z < Samples; ++z)
                    WriteTexel(gpuTexels, (z * Samples + y) * Samples + x, samples[(x * Samples + y) * Samples + z]);


        // Created empty and filled afterwards, rather than handed its data here: a texture given its
        // contents at creation is immutable, and the first dig then throws from inside SetData -
        // far from the line that decided it.
        gpuTexture = Texture.New3D(
            game.GraphicsDevice, Samples, Samples, Samples, PixelFormat.R8G8_UNorm,
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

    /// <summary>Density in red, the material id in green - what the texture source reads.</summary>
    private static void WriteTexel(byte[] texels, int index, ushort packed)
    {
        texels[index * 2 + 0] = (byte)(packed & 0xFF);
        texels[index * 2 + 1] = (byte)(packed >> 8);
    }

    /// <summary>
    /// The materials the field's ids point at, by id: air, ground, the sphere, the arch. Ordinary
    /// materials, built here the way an asset would be authored; the grid reads their constants.
    /// </summary>
    private static List<Material> BuildMaterials(GraphicsDevice device)
    {
        static Material Make(GraphicsDevice device, Color4 colour, float glossiness, float metalness, Color4? emissive = null, float intensity = 0f)
        {
            var descriptor = new MaterialDescriptor
            {
                Attributes =
                {
                    Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(colour)),
                    DiffuseModel = new MaterialDiffuseLambertModelFeature(),
                    MicroSurface = new MaterialGlossinessMapFeature(new ComputeFloat(glossiness)),
                    Specular = new MaterialMetalnessMapFeature(new ComputeFloat(metalness)),
                    // The polynomial environment term, as the gallery's palette: the default LUT
                    // reads a DFG texture nothing supplies to a material built at runtime.
                    SpecularModel = new MaterialSpecularMicrofacetModelFeature { Environment = new MaterialSpecularMicrofacetEnvironmentGGXPolynomial() },
                    Emissive = emissive is { } glow ? new MaterialEmissiveMapFeature(new ComputeColor(glow)) { Intensity = new ComputeFloat(intensity), UseAlpha = false } : null,
                },
            };
            return Material.New(device, descriptor);
        }

        return
        [
            Make(device, new Color4(0f, 0f, 0f, 1f), 0f, 0f),                                              // 0: air, never at a surface
            Make(device, new Color4(0.10f, 0.75f, 0.95f, 1f), 0.35f, 0f),                                  // 1: the ground
            Make(device, new Color4(0.85f, 0.90f, 0.15f, 1f), 0.6f, 0.2f),                                 // 2: the sphere, a little glossy
            Make(device, new Color4(1.00f, 0.00f, 0.88f, 1f), 0.4f, 0f, new Color4(1f, 0f, 0.88f, 1f), 6f), // 3: the arch, which glows
            Make(device, new Color4(1f, 1f, 1f, 1f), 0.3f, 0f, new Color4(1f, 1f, 1f, 1f), 4f),             // 4: the corner pillars, white lamps
        ];
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
        if (cachedSamples is null || collider is null)
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
                    if (gpuTexels != null)
                        WriteTexel(gpuTexels, (z * Samples + y) * Samples + x, packed);
                    collider.SetVoxel(x, y, z, packed);
                    touched = true;
                }
            }
        }

        if (!touched)
            return;

        UploadTexture(game.GraphicsContext.CommandList);

        // The pyramid over the box the brush touched, and nothing outside it.
        occupancy?.Update(game.GraphicsContext.CommandList, ReadDensity, new Int3(x0, y0, z0), new Int3(x1, y1, z1));

        // Physics already sees the edit; this is for anything that kept a copy of the surface, which
        // here is the F11 wireframe. Cheap on this collider - a shape slot swap, no tree rebuilt.
        collider.NotifyFieldChanged();
    }

    /// <summary>The model's own traversal, over the texture copy of the field. Follows every switch.</summary>
    private static VoxelGridTraversalDDA? modelTraversal;

    /// <summary>Collider form to start on, from --collider.</summary>
    public static VoxelChildForm StartColliderForm { get; set; } = VoxelChildForm.TriangleMarchingCubes;

    /// <summary>Show the collider wireframe from the first frame, from --wireframe.</summary>
    public static bool StartWithWireframe { get; set; }

    /// <summary>Diagnostic view of the drawn surface, from STRIDE_VOXEL_DEBUG; see VoxelGridFieldKeys.Debug.</summary>
    public static float DebugView { get; set; }

    /// <summary>Surface to start on, from --surface. Lets an unattended capture photograph any of them.</summary>
    public static VoxelSurfaceForm StartSurface { get; set; } = VoxelSurfaceForm.MarchingCubes;

    /// <summary>What the digging tool is pointing at, shown by the shell with everything else.</summary>
    public static string AimStatus { get; set; } = "nothing";

    /// <summary>Start without the grid casting shadows, from --no-shadows.</summary>
    public static bool StartWithShadows { get; set; } = true;
    /// <summary>Whether the field is injected into the GI volume directly, or its proxy voxelized like a mesh.</summary>
    public static bool StartWithInjection { get; set; } = true;
    /// <summary>How much of the previous frame's light the injected field bounces; negative keeps the component's default.</summary>
    public static float StartInjectBounce { get; set; } = -1f;
    /// <summary>Where the GI cones start, in voxels from the surface; zero keeps the marchers' default.</summary>
    // Three: at one, the cones of a polished sphere graze its own voxel shell at fixed angles as
    // they climb the mips and the sphere wears rings; two leaves a trace, three clears them.
    public static float StartConeOffset { get; set; } = 3f;

    /// <summary>Start with the GI volume around the camera, from --gi.</summary>
    public static bool StartWithGI { get; set; }

    /// <summary>How material boundaries are drawn at the start, from --dither.</summary>
    public static VoxelMaterialDither StartDither { get; set; } = VoxelMaterialDither.InterleavedGradientNoise;

    /// <summary>Start with the sun and the ambient off, from --gi-only, so the field is lit by what it emits.</summary>
    public static bool StartWithLights { get; set; } = true;

    private static readonly List<LightComponent> sceneLights = [];

    /// <summary>Whether the scene's own lights - the sun and the ambient - are on.</summary>
    public static bool LightsEnabled
    {
        get => sceneLights.Count > 0 && sceneLights[0].Enabled;
        set { foreach (var light in sceneLights) light.Enabled = value; }
    }

    private static VoxelGridComponent? grid;
    private static Entity? giVolume;
    private static Entity? cameraEntity;
    private static Scene? currentScene;

    /// <summary>
    /// Whether the grid writes the shadow maps. It receives shadows either way.
    /// </summary>
    /// <remarks>
    /// The one switch that matters to the frame: four cascades each walk the field again, and
    /// together they cost more than the view does. A world lit by the GI volume instead has no use
    /// for them.
    /// </remarks>
    public static bool CastShadows
    {
        get => grid?.CastShadows ?? StartWithShadows;
        set { if (grid is not null) grid.CastShadows = value; }
    }

    /// <summary>How a boundary between two materials is shared out between pixels.</summary>
    public static VoxelMaterialDither Dither
    {
        get => grid?.Dither ?? VoxelMaterialDither.InterleavedGradientNoise;
        set { if (grid is not null) grid.Dither = value; }
    }

    /// <summary>Steps to the next way of drawing a material boundary.</summary>
    public static void CycleDither() => Dither = Dither switch
    {
        VoxelMaterialDither.Sharp => VoxelMaterialDither.Bayer4x4,
        VoxelMaterialDither.Bayer4x4 => VoxelMaterialDither.Bayer8x8,
        VoxelMaterialDither.Bayer8x8 => VoxelMaterialDither.InterleavedGradientNoise,
        _ => VoxelMaterialDither.Sharp,
    };

    /// <summary>Whether a voxel GI volume is following the camera.</summary>
    public static bool GIEnabled => giVolume is not null;

    /// <summary>
    /// Puts a voxel GI volume around the camera, or takes it away.
    /// </summary>
    /// <remarks>
    /// The volume is voxelized from what the renderer draws, and the grid is drawn as a model, so
    /// the field lights itself through the same path a mesh would. A volume that follows the camera
    /// is what an open world needs: the field can be any size, only the part around the camera is
    /// lit indirectly, and the rest is not paid for.
    /// </remarks>
    public static void ToggleGI()
    {
        if (currentScene is null || cameraEntity is null)
            return;

        if (giVolume is not null)
        {
            currentScene.Entities.Remove(giVolume);
            giVolume = null;
            return;
        }

        giVolume = new Entity("Voxel GI");
        giVolume.Transform.Position = cameraEntity.Transform.Position;
        giVolume.Add(new VoxelGIVolume
        {
            // The gallery's settings, which the hall settled on after trying both ends: consistent
            // and coarse beats fine and seamed. Four times the grid at three levels puts the finest
            // ring over the whole field at once, so following the camera has little to re-snap.
            VolumeSize = Extent * 4f,
            ClipMapLevels = 3,
            Quality = VoxelGIQuality.UltraPlus,
            // And the hall's lighting balance, for the same reasons it gives: radiance halves per
            // mip rather than falling with the fill count, and the bounce doubled to match.
            BounceIntensity = 2f,
            LightFalloff = VoxelAttributeEmissionOpacity.LightFalloffs.PhysicallyBased,
            Opacify = 1f,
            SpecularSteps = 576,
            ConeOffset = StartConeOffset,
            SpecularRange = 24f,
            SpecularRoughnessCutoff = 1.0f,
            AutoFreeze = false,
            Follow = cameraEntity.Transform,
        });
        giVolume.Add(new VoxelGIDebug
        {
            OverlayPosition = new Int2(16, 16),
            RequireControl = true,
            ScreenshotKey = Keys.None,
            FollowCandidate = cameraEntity.Transform,
        });
        currentScene.Entities.Add(giVolume);
    }

    /// <summary>Which surface the walk stops on.</summary>
    public static VoxelSurfaceForm Surface
    {
        get => modelTraversal?.Surface ?? VoxelSurfaceForm.MarchingCubes;
        set { if (modelTraversal is not null) modelTraversal.Surface = value; }
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

    private static ushort[]? cachedSamples;

    private static uint[]? gpuSamples;
    private static byte[]? gpuTexels;
    private static Texture? gpuTexture;
    private static GraphicsBuffer? gpuBuffer;
    private static VoxelCollider? collider;

    /// <summary>The min/max pyramid the traversal skips empty space on.</summary>
    private static VoxelGridOccupancy? occupancy;

    /// <summary>Density of one sample as the pyramid reads it, from the CPU copy of the field.</summary>
    private static float ReadDensity(int x, int y, int z)
        => (cachedSamples![(x * Samples + y) * Samples + z] & 0xFF) / 255f;

    /// <summary>Scaffolding: carve a fixed trench after this many frames, for an unattended capture.</summary>
    public static int AutoDigAfterFrames { get; set; }

    public static void Build(Game game, Entity camera)
        => BuildScene(game, game.SceneSystem.SceneInstance.RootScene, camera, cachedSamples ??= Generate());

    private static void BuildScene(Game game, Scene scene, Entity camera, ushort[] samples)
    {
        currentScene = scene;
        cameraEntity = camera;
        giVolume = null;

        // The pyramid outlives the scene, as the field does.
        if (occupancy is null)
        {
            occupancy = new VoxelGridOccupancy(game.GraphicsDevice, new Int3(Samples, Samples, Samples));
            occupancy.Update(game.GraphicsContext.CommandList, ReadDensity);
        }

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
        terrain.Add(grid = new VoxelGridComponent
        {
            Traversal = modelTraversal = new VoxelGridTraversalDDA
            {
                Occupancy = occupancy,
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
            CastShadows = StartWithShadows,
            InjectIntoGI = StartWithInjection,
            DebugView = DebugView,
            Dither = StartDither,
        });

        // The palette, by id. The arch emits; with the sun and the ambient off it is the only light
        // there is, and with a GI volume around the camera it lights the field it stands on.
        grid.Materials.AddRange(BuildMaterials(game.GraphicsDevice));
        if (StartInjectBounce >= 0f)
            grid.InjectBounce = StartInjectBounce;

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

        var ambient = new Entity("Ambient")
        {
            new LightComponent
            {
                Type = new LightAmbient { Color = new ColorRgbProvider(new Color3(0.30f, 0.34f, 0.42f)) },
                Intensity = 1f,
            },
        };
        scene.Entities.Add(ambient);

        // Both on one switch, so the field can be seen lit by nothing but what it emits.
        sceneLights.Clear();
        sceneLights.Add(sun.Get<LightComponent>());
        sceneLights.Add(ambient.Get<LightComponent>());
        LightsEnabled = StartWithLights;

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

        // The aim: a dot at the centre of the screen, in place of a beam drawn into the scene. A
        // beam is a model, which the voxelizer and the shadow maps see as one more thing to draw;
        // a dot in the UI costs nothing and is where every first-person tool puts its aim.
        scene.Entities.Add(BuildReticle());

        if (StartWithGI)
            ToggleGI();
    }

    /// <summary>A small square, centred, on the UI layer the shell's menu also draws on.</summary>
    private static Entity BuildReticle()
    {
        var dot = new Border
        {
            Width = 6,
            Height = 6,
            BorderThickness = new Thickness(3, 3, 3, 3),
            BorderColor = new Color(1f, 1f, 1f, 0.85f),
        };
        dot.SetCanvasRelativePosition(new Vector3(0.5f, 0.5f, 0f));
        dot.SetCanvasPinOrigin(new Vector3(0.5f, 0.5f, 0f));

        var canvas = new Canvas();
        canvas.Children.Add(dot);

        return new Entity("Reticle")
        {
            new UIComponent
            {
                Page = new UIPage { RootElement = canvas },
                IsFullScreen = true,
                Resolution = new Vector3(1280, 720, 1000),
                RenderGroup = RenderGroup.Group31,
            },
        };
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
}
