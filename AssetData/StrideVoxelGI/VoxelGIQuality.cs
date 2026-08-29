// Copyright (c) 2026 Nicogo. Distributed under the MIT license.
using Stride.Core;
using Stride.Rendering.Voxels;

namespace StrideVoxelGI;

/// <summary>
/// Four points on the cost/quality curve of voxel cone tracing. Each one picks a clipmap
/// resolution, a voxel layout and how many cones are traced per pixel — see <see cref="VoxelGIPreset"/>.
/// </summary>
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
    public bool Anisotropic;

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

    public static VoxelGIPreset For(VoxelGIQuality quality) => quality switch
    {
        VoxelGIQuality.Low => new VoxelGIPreset
        {
            ClipResolution = VoxelStorageClipmaps.Resolutions.x64,
            Anisotropic = false,
            DiffuseCones = 6,
            DiffuseSteps = 5,
            SpecularSteps = 16,
        },
        VoxelGIQuality.High => new VoxelGIPreset
        {
            ClipResolution = VoxelStorageClipmaps.Resolutions.x128,
            Anisotropic = true,
            DiffuseCones = 12,
            DiffuseSteps = 9,
            SpecularSteps = 40,
        },
        VoxelGIQuality.Ultra => new VoxelGIPreset
        {
            ClipResolution = VoxelStorageClipmaps.Resolutions.x256,
            Anisotropic = true,
            DiffuseCones = 12,
            DiffuseSteps = 12,
            SpecularSteps = 60,
            SpecularConeRatio = 0.6f,
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
            VoxelLayout = Anisotropic ? new VoxelLayoutAnisotropic() : new VoxelLayoutIsotropic(),
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
