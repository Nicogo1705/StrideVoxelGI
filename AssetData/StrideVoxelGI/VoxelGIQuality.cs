// Copyright (c) 2026 Nicogo. Distributed under the MIT license.
using Stride.Core;
using Stride.Graphics;
using Stride.Rendering.Voxels;

namespace StrideVoxelGI;

/// <summary>
/// Four points on the cost/quality curve of voxel cone tracing. Each one picks a clipmap
/// resolution, a voxel layout and how many cones are traced per pixel — see <see cref="VoxelGIPreset"/>.
/// </summary>
/// <summary>How many directions a voxel stores, and so how much of the texture budget it takes.</summary>
public enum VoxelGIDirectionality
{
    /// <summary>One value per voxel. Cheapest, leaks the most light through surfaces.</summary>
    Isotropic,

    /// <summary>Three: one per axis, the two facings of each packed together.</summary>
    Paired,

    /// <summary>Six: one per facing. The most accurate, and six times the texture budget.</summary>
    Anisotropic,
}

public enum VoxelGIQuality
{
    /// <summary>64³ isotropic voxels, 6 diffuse cones. Cheapest; use it as the console/low-end tier.</summary>
    [Display("Low (64³, 6 cones)")] Low,

    /// <summary>128³ isotropic voxels, 6 diffuse cones. The sane default.</summary>
    [Display("Medium (128³, 6 cones)")] Medium,

    /// <summary>128³ anisotropic voxels, 12 diffuse cones. Directional storage kills most light leaking.</summary>
    [Display("High (128³ anisotropic, 12 cones)")] High,

    /// <summary>256³ anisotropic voxels, 12 long diffuse cones. Screenshot mode.</summary>
    [Display("Ultra (256³ anisotropic, 12 cones)")] Ultra,
}

/// <summary>
/// The concrete settings behind a <see cref="VoxelGIQuality"/>, and the factory methods that turn
/// them into the Stride.Voxels object graph a <see cref="Stride.Rendering.Voxels.VoxelVolumeComponent"/>
/// expects. Build one with <see cref="For"/>, or hand-roll a preset if you want a tier of your own.
/// </summary>
public sealed class VoxelGIPreset
{
    /// <summary>Voxels along the longest axis of the finest clipmap: 64, 128 or 256.</summary>
    public VoxelStorageClipmaps.Resolutions ClipResolution = VoxelStorageClipmaps.Resolutions.x128;

    /// <summary>
    /// Store six directional values per voxel instead of one. Roughly 6x the memory and a slower
    /// voxelization, but a surface no longer receives light that reached the voxel from behind it —
    /// which is what most "light leaks through the wall" complaints actually are.
    /// </summary>
    /// <summary>
    /// How the voxels store direction. Isotropic keeps one value per voxel; paired keeps three
    /// (one per axis, the two facings packed together); full anisotropic keeps six, one per facing.
    /// <para>
    /// More directions means less light leaking through surfaces - and fewer clipmap rings. The
    /// storage stacks every ring and every direction down the Y axis of one 3D texture, which
    /// Direct3D11 caps at 2048 texels: at 128^3, six directions leave room for two rings where
    /// three leave room for five. Rings are what buy voxel size, and voxel size is what actually
    /// resolves a wall, so paying six directions for a voxel eight times bigger is a bad trade.
    /// See <see cref="VoxelGIVolume.EffectiveClipMapLevels"/>.
    /// </para>
    /// </summary>
    public VoxelGIDirectionality Directionality;

    /// <summary>Cones traced per pixel for diffuse GI: 6 (hemisphere) or 12.</summary>
    public int DiffuseCones = 6;

    /// <summary>Samples along each diffuse cone. More steps = light travels further before it fades.</summary>
    public int DiffuseSteps = 7;

    /// <summary>Samples along the single specular cone (voxel reflections).</summary>
    public int SpecularSteps = 30;

    /// <summary>
    /// How wide the specular cone opens with distance. 1 is a rough, blurry reflection; lower it
    /// towards 0.2 for something closer to a mirror (and much more aliasing).
    /// </summary>
    public float SpecularConeRatio = 1.0f;

    /// <summary>
    /// Re-voxelize every clipmap every frame instead of one per frame. Correct for scenes that
    /// change fast, several times the cost for scenes that don't.
    /// </summary>
    public bool UpdateAllClipmapsEveryFrame;

    /// <summary>
    /// Multisampling of the voxelization render target. More samples = fewer holes in thin
    /// geometry (each covered sample writes a fragment), at proportional fill cost during
    /// voxelization. Opacify already compensates partial coverage, so the low tiers get by
    /// with less.
    /// </summary>
    public MultisampleCount VoxelizationMSAA = MultisampleCount.X8;

    /// <summary>
    /// Roughness above which the specular cone (the most expensive trace) is skipped - rough
    /// surfaces get a blur the diffuse cones already provide. 1 traces everything.
    /// </summary>
    public float SpecularRoughnessCutoff = 1.0f;

    /// <summary>
    /// Trace the diffuse cones into a buffer this many times smaller than the screen along each
    /// axis, and read that back when shading: 1 traces per shaded pixel, 2 costs a quarter of the
    /// cones, 4 a sixteenth. Bounced light is low frequency, so what it costs to trace and what it
    /// costs to apply need not be the same resolution.
    /// </summary>
    public int GIResolutionDivisor = 1;

    public static VoxelGIPreset For(VoxelGIQuality quality) => quality switch
    {
        VoxelGIQuality.Low => new VoxelGIPreset
        {
            ClipResolution = VoxelStorageClipmaps.Resolutions.x64,
            Directionality = VoxelGIDirectionality.Isotropic,
            DiffuseCones = 6,
            DiffuseSteps = 5,
            SpecularSteps = 16,
            VoxelizationMSAA = MultisampleCount.X2,
            SpecularRoughnessCutoff = 0.7f,
            GIResolutionDivisor = 2,
        },
        VoxelGIQuality.High => new VoxelGIPreset
        {
            ClipResolution = VoxelStorageClipmaps.Resolutions.x128,
            Directionality = VoxelGIDirectionality.Paired,
            DiffuseCones = 12,
            DiffuseSteps = 9,
            SpecularSteps = 40,
        },
        VoxelGIQuality.Ultra => new VoxelGIPreset
        {
            // Not 256^3: the storage stacks rings and directions down one 3D texture's Y axis, and
            // Direct3D11 caps that at 2048. At 256^3 paired there is room for two rings, which
            // makes the voxels 0.75 units - four times bigger than this tier's, and bigger than
            // High's. Rings buy voxel size and voxel size resolves geometry, so Ultra spends its
            // budget on cones and steps instead, over the finest voxels the cap allows.
            ClipResolution = VoxelStorageClipmaps.Resolutions.x128,
            Directionality = VoxelGIDirectionality.Paired,
            DiffuseCones = 12,
            DiffuseSteps = 12,
            SpecularSteps = 60,
            SpecularConeRatio = 0.6f,
        },
        VoxelGIQuality.Medium => new VoxelGIPreset
        {
            VoxelizationMSAA = MultisampleCount.X4,
            SpecularRoughnessCutoff = 0.9f,
        },
        _ => new VoxelGIPreset(),
    };

    /// <summary>Voxels along the longest axis, as a number.</summary>
    public int Resolution => (int)ClipResolution;

    /// <summary>Clipmap storage at <see cref="Resolution"/> voxels per axis.</summary>
    public VoxelStorageClipmaps CreateStorage() => new()
    {
        ClipResolution = ClipResolution,
        UpdatesPerFrame = UpdateAllClipmapsEveryFrame
            ? VoxelStorageClipmaps.UpdateMethods.AllClipmapsMultipleRenders
            : VoxelStorageClipmaps.UpdateMethods.SingleClipmap,
        DownsampleFinerClipMaps = true,
    };

    /// <summary>
    /// How much thin geometry is thickened during voxelization, or 0 to leave it alone.
    /// <para>
    /// Meshes are hollow: a wall, a box, a sphere are all zero-thickness surfaces. Voxelizing one
    /// gives voxels that are only partly covered, and a cone marching through reads them as
    /// half-transparent - the surface comes out dashed and light leaks through it. Opacify scales
    /// that coverage up so a grazed surface still reads as solid.
    /// </para>
    /// <para>
    /// Turn it down if thin geometry looks inflated, up if surfaces leak. It is no substitute for
    /// voxels small enough to resolve the geometry: check the voxel size first.
    /// </para>
    /// </summary>
    public float Opacify = 2.0f;

    /// <summary>
    /// The one attribute voxel GI actually needs: per-voxel emitted radiance + opacity. Everything
    /// the cones read is written here during the voxelization pass.
    /// </summary>
    public VoxelAttributeEmissionOpacity CreateAttribute()
    {
        var attribute = new VoxelAttributeEmissionOpacity
        {
            VoxelLayout = Directionality switch
            {
                VoxelGIDirectionality.Paired => new VoxelLayoutAnisotropicPaired(),
                VoxelGIDirectionality.Anisotropic => new VoxelLayoutAnisotropic(),
                _ => new VoxelLayoutIsotropic(),
            },
            LightFalloff = VoxelAttributeEmissionOpacity.LightFalloffs.Heuristic,
        };

        if (Opacify > 0f)
            attribute.Modifiers.Add(new VoxelModifierEmissionOpacityOpacify { Amount = Opacify });

        return attribute;
    }

    /// <summary>The cone set traced for indirect diffuse.</summary>
    public IVoxelMarchSet CreateDiffuseMarcher()
    {
        var marcher = new VoxelMarchConePerMipmap(1.0f, DiffuseSteps);
        return DiffuseCones >= 12
            ? new VoxelMarchSetHemisphere12(marcher)
            : new VoxelMarchSetHemisphere6(marcher);
    }

    /// <summary>The single cone traced for indirect specular (voxel reflections).</summary>
    public IVoxelMarchMethod CreateSpecularMarcher() => new VoxelMarchCone(SpecularSteps, 0.5f, SpecularConeRatio);

    /// <summary>Voxel size that makes the finest clipmap line up with a volume of that edge length.</summary>
    public float VoxelSizeFor(float volumeSize) => volumeSize / Resolution;
}
