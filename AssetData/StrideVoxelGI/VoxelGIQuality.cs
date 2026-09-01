// Copyright (c) 2026 Nicogo. Distributed under the MIT license.
using System;
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
    [Display("Low (64³ isotropic, 6 cones)")] Low,

    /// <summary>128³ isotropic voxels, 6 diffuse cones. The sane default.</summary>
    [Display("Medium (128³ isotropic, 6 cones)")] Medium,

    /// <summary>128³ anisotropic voxels, 12 diffuse cones. Directional storage kills most light leaking.</summary>
    [Display("High (128³ paired, 12 cones)")] High,

    /// <summary>128³ paired voxels, 12 long diffuse cones. Screenshot mode.</summary>
    [Display("Ultra (128³ paired, 12 long cones)")] Ultra,

    /// <summary>
    /// 256³ paired voxels. The only tier that widens the sharp region instead of moving it.
    /// </summary>
    /// <remarks>
    /// The finest clipmap ring is always exactly <c>resolution</c> voxels across - ring extent is
    /// VolumeSize/2^(levels-1) and voxel size is that over the resolution, so their ratio is fixed.
    /// Every other setting therefore slides along one line: six metres of sharp reflection at 4.7cm
    /// voxels, or twenty-four at 18.8cm, but never twenty-four at 4.7. Doubling the resolution is
    /// the only way off that line, and it pays for it in the atlas: rings stack along X and
    /// directions along Y, so 256³ is a little over six times the texels of 128³ at the same ring
    /// count - and eight times the voxels to fill each frame. Pair it with one clipmap level fewer
    /// to spend the gain on ring extent rather than on voxel size.
    /// </remarks>
    [Display("Ultra+ (256³ paired, 12 long cones)")] UltraPlus,
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

    /// <summary>
    /// Cones traced for the bounce re-injected into the voxels during voxelization, or 0 to trace
    /// the same set the camera does.
    /// </summary>
    /// <remarks>
    /// The two are the same question asked for very different readers. The camera's answer is
    /// looked at; this one is written into a voxel, averaged with every other fragment in it,
    /// mipmapped, and read back through a cone that integrates a mip - blurred twice before anyone
    /// sees it. It is also, on this hall, the single most expensive line in the frame: the camera
    /// traces its cones into a reduced buffer, and a voxel view has no screen to reduce.
    /// </remarks>
    public int BounceCones = 6;

    /// <summary>
    /// Samples along each bounce cone, capped at <see cref="DiffuseSteps"/>, or 0 to march as far
    /// as the camera's cones do.
    /// </summary>
    /// <remarks>
    /// The step count is a distance, not a quality: VoxelMarchConePerMipmap doubles its stride and
    /// drops one mip at every step, so N steps reach 2^N voxels. At the 14cm voxel this hall runs,
    /// twelve steps reach 577 units - a hall 44 long, whose volume is 144 - so the last few march
    /// through the empty space outside the building to accumulate nothing. It is therefore a safe
    /// place to cut, but a small one: those steps read mips of a few texels and are the cheapest of
    /// the march. The saving is in <see cref="BounceCones"/>, which is where this leaves it.
    /// </remarks>
    public int BounceSteps;

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
        VoxelGIQuality.UltraPlus => new VoxelGIPreset
        {
            ClipResolution = VoxelStorageClipmaps.Resolutions.x256,
            Directionality = VoxelGIDirectionality.Paired,
            DiffuseCones = 12,
            DiffuseSteps = 12,
            SpecularSteps = 60,
            SpecularConeRatio = 0.6f,

            // Four rather than the eight the upper tiers inherit. Voxelization multisampling exists
            // so a surface that only grazes a voxel still writes a fragment, which matters when the
            // geometry is thinner than the grid - and at this tier the grid is the coarse half of
            // the trade, so nothing in a room is thin relative to it. Eight samples is the single
            // most expensive line in the frame and it is buying resolution the voxels cannot hold.
            VoxelizationMSAA = MultisampleCount.X4,
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
    /// How radiance is carried down the mipmap chain, and therefore how fast the light dies with
    /// distance.
    /// </summary>
    /// <remarks>
    /// Merging eight voxels into one asks by how much to divide their radiance. Opacity divides by
    /// eight in every strategy - it is a volume - but radiance is what a cone reads off a surface,
    /// and a surface is a 2D projection, so it should fall by four. <c>Sharp</c> divides it by eight
    /// anyway and every mip comes out half as bright as the last, which is the light draining away
    /// as the cones climb. <c>PhysicallyBased</c> always divides by four. <c>Heuristic</c> divides
    /// by the number of filled sub-voxels with a floor of four, so it matches PhysicallyBased on an
    /// isolated surface and falls back to eight where all eight are filled - inside thick walls and
    /// large solids, which is precisely where the dimming is noticed.
    /// </remarks>
    public VoxelAttributeEmissionOpacity.LightFalloffs LightFalloff = VoxelAttributeEmissionOpacity.LightFalloffs.Heuristic;

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
            LightFalloff = LightFalloff,
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

    /// <summary>
    /// The cone set traced while voxelizing, or null when it would be identical to
    /// <see cref="CreateDiffuseMarcher"/> - in which case the light marches that one in both views
    /// rather than compiling a second permutation of the same shader.
    /// </summary>
    public IVoxelMarchSet? CreateBounceMarcher()
    {
        var cones = BounceCones > 0 ? BounceCones : DiffuseCones;
        var steps = Math.Min(BounceSteps > 0 ? BounceSteps : DiffuseSteps, DiffuseSteps);

        if (cones == DiffuseCones && steps == DiffuseSteps)
            return null;

        var marcher = new VoxelMarchConePerMipmap(1.0f, steps);
        return cones >= 12
            ? new VoxelMarchSetHemisphere12(marcher)
            : new VoxelMarchSetHemisphere6(marcher);
    }

    /// <summary>The single cone traced for indirect specular (voxel reflections).</summary>
    /// <summary>
    /// The specular marcher, optionally with a tighter cone than the tier asks for. The ratio is
    /// the aperture: at 1 the cone integrates whole mips and hands back the average of the room,
    /// which is why a mirror under this tier shows a smear rather than a reflection; as it goes to
    /// zero the cone closes into a ray march at the finest mip, and the reflection sharpens.
    /// </summary>
    /// <remarks>
    /// The two arguments are one setting in two halves. A cone advances by its own current radius,
    /// so the aperture sets how fast it grows and therefore how far a fixed number of steps
    /// reaches: closing the cone from 1 to 0.25 makes it four times sharper and roughly four times
    /// shorter. Sharpen without adding steps and the ray dies a couple of metres out, which reads
    /// as a black reflection rather than a blurry one - so nothing here closes the cone without
    /// paying for the range.
    /// </remarks>
    /// <summary>
    /// How far a reflection may travel, in world units, or zero for the old unlimited march.
    /// </summary>
    /// <remarks>
    /// A cone stops on saturation or on leaving the volume, and in a building neither fires: walls
    /// stop occluding once the cone reads them from a coarse mip, so the ray passes through one a
    /// few metres out, and the volume is far larger than the rooms inside it. The ray then spends
    /// the rest of its budget in the empty space outside the geometry and brings back the building
    /// seen from without - which arrives in the reflection as a small, legible copy of the level.
    /// Range is what that costs nothing to fix: a reflection is only sharp over its first few
    /// metres anyway, because past them the cone has widened into an average.
    /// </remarks>
    public float SpecularRange = DefaultSpecularRange;

    /// <summary>Range a volume starts at, before anyone touches it.</summary>
    public const float DefaultSpecularRange = 12f;

    /// <remarks>
    /// <paramref name="range"/> is passed straight through, unlike the other two: zero is a real
    /// setting here - it means no horizon at all - so it cannot double as "unset, use the preset".
    /// </remarks>
    public IVoxelMarchMethod CreateSpecularMarcher(float coneRatio = 0f, int steps = 0, float range = 0f)
        => new VoxelMarchCone(
            steps > 0 ? steps : SpecularSteps,
            0.5f,
            coneRatio > 0f ? coneRatio : SpecularConeRatio,
            range);

    /// <summary>Voxel size that makes the finest clipmap line up with a volume of that edge length.</summary>
    public float VoxelSizeFor(float volumeSize) => volumeSize / Resolution;
}
