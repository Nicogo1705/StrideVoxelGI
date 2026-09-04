// Copyright (c) 2026 Nicogo. Distributed under the MIT license.
using System;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering.Voxels;
using Stride.Rendering.Voxels.Debug;
using Stride.Rendering.Voxels.VoxelGI;

namespace StrideVoxelGI;

/// <summary>
/// Turns one entity into a working voxel cone tracing setup: a <see cref="VoxelVolumeComponent"/>
/// that voxelizes everything around it, and a <see cref="LightVoxel"/> environment light that
/// cone-traces that volume back into the shading of every lit surface.
/// <para>
/// Doing this by hand means wiring five nested Stride.Voxels objects and remembering that
/// <see cref="LightVoxel.BounceIntensityScale"/> defaults to 0 — the hand-built setup renders no
/// GI at all until you find that field. This component picks a coherent set from a
/// <see cref="VoxelGIQuality"/> preset instead.
/// </para>
/// <para>
/// The volume is a cube of <see cref="VolumeSize"/> world units centred on this entity, and it
/// follows the entity: parent it to your camera or player for a moving world, leave it at the
/// origin for a single room.
/// </para>
/// <para>
/// Nothing renders unless the graphics compositor uses <c>ForwardRendererVoxels</c> and its two
/// voxelization render stages — the demo ships a ready-made one.
/// </para>
/// </summary>
[Display("Voxel GI Volume", Expand = ExpandRule.Once)]
[ComponentCategory("Lights")]
public class VoxelGIVolume : SyncScript
{
    /// <summary>
    /// Transform to keep the volume centred on - typically the camera or the player. Only the
    /// position is taken: the volume must stay axis-aligned, and VoxelGridSnapping quantizes the
    /// movement to whole voxels so the world does not shimmer as it re-voxelizes. Null leaves the
    /// volume where its entity sits.
    /// </summary>
    [DataMember(5)]
    public TransformComponent? Follow { get; set; }

    private VoxelGIQuality quality = VoxelGIQuality.Medium;
    private float volumeSize = 24f;
    private int clipMapLevels = 1;
    private float bounceIntensity = 1f;
    private float secondBounce = 1f;
    private float specularIntensity = 1f;
    private float opacify = 2f;
    private bool giEnabled = true;
    private bool voxelize = true;
    private VoxelGIDebugView debugView = VoxelGIDebugView.Off;
    private MultisampleCount? voxelizationMSAA;

    /// <summary>Edge of the voxelized cube, in world units. Bigger volume, coarser voxels.</summary>
    [DataMember(10)]
    public float VolumeSize
    {
        get => volumeSize;
        set { volumeSize = Math.Max(0.01f, value); Apply(); }
    }

    /// <summary>
    /// Clipmap levels: nested detail rings, each covering twice the distance of the previous at
    /// half its voxel density. 1 keeps the whole volume at full density. With N levels, the finest
    /// ring only spans <see cref="VolumeSize"/>/2^(N-1) around the entity, so a big world volume
    /// stays sharp up close and cheap far away - the cone tracer blends between rings on its own.
    /// Memory and voxelization cost grow linearly with levels, not cubically like resolution.
    /// </summary>
    [DataMember(15)]
    public int ClipMapLevels
    {
        get => clipMapLevels;
        set
        {
            value = Math.Clamp(value, 1, MaxClipMapLevels);
            if (clipMapLevels == value)
                return;
            clipMapLevels = value;
            if (Volume != null)
                Rebuild();
        }
    }

    /// <summary>
    /// The most rings this preset's resolution can carry. The storage gives each ring a column of
    /// its 3D texture (capped at 2048 per side) and shares its offset tables with the mipmaps, so
    /// the ceiling moves with the resolution: fourteen at 32³, thirteen at 64³, twelve at 128³,
    /// eight at 256³.
    /// </summary>
    [DataMemberIgnore]
    public int MaxClipMapLevels
        => VoxelStorageClipmaps.MaxClipMapCount((VoxelStorageClipmaps.Resolutions)Preset.Resolution);

    /// <summary>
    /// Multisampling of the voxelization render target, or null for the tier's own.
    /// </summary>
    /// <remarks>
    /// Each covered sample writes a fragment, so this is what keeps geometry thinner than a voxel
    /// from dropping out of the grid - and it multiplies the cost of the most expensive pass in the
    /// frame by the sample count. Worth spending where the voxels are fine enough to resolve small
    /// things, worth almost nothing where they are not.
    /// </remarks>
    [DataMember(22)]
    public MultisampleCount VoxelizationMSAA
    {
        get => voxelizationMSAA ?? Preset.VoxelizationMSAA;
        set
        {
            if (voxelizationMSAA == value)
                return;
            voxelizationMSAA = value;
            if (Volume != null)
                Rebuild();
        }
    }

    /// <summary>
    /// Voxels along the longest axis of each ring, or the tier's own until set. Rebuilds the
    /// volume. Resolution is the cubic way to finer voxels where <see cref="ClipMapLevels"/> is
    /// the linear one - and it moves <see cref="MaxClipMapLevels"/> in the other direction, so
    /// raising it can silently cost a ring.
    /// </summary>
    [DataMember(23)]
    public VoxelStorageClipmaps.Resolutions ClipResolution
    {
        get => clipResolution ?? Preset.ClipResolution;
        set
        {
            if (clipResolution == value)
                return;
            clipResolution = value;
            if (Volume != null)
                Rebuild();
        }
    }

    private VoxelStorageClipmaps.Resolutions? clipResolution;

    /// <summary>
    /// How many directions each voxel stores, or the tier's own until set. Rebuilds the volume.
    /// More directions stop light reaching a surface from behind it - the classic leak - for one,
    /// three or six times the texture budget.
    /// </summary>
    [DataMember(24)]
    public VoxelGIDirectionality Directionality
    {
        get => directionality ?? Preset.Directionality;
        set
        {
            if (directionality == value)
                return;
            directionality = value;
            if (Volume != null)
                Rebuild();
        }
    }

    private VoxelGIDirectionality? directionality;

    /// <summary>
    /// Cones traced per pixel for diffuse GI - 6 or 12, zero for the tier's own. Rebuilds the
    /// volume.
    /// </summary>
    [DataMember(25)]
    public int DiffuseCones
    {
        get => diffuseCones > 0 ? diffuseCones : Preset.DiffuseCones;
        set
        {
            if (diffuseCones == value)
                return;
            diffuseCones = value;
            if (Volume != null)
                Rebuild();
        }
    }

    private int diffuseCones;

    /// <summary>
    /// Samples along each diffuse cone, zero for the tier's own. Rebuilds the volume. More steps
    /// carry bounce light further before it fades - and each is paid on every cone of every pixel.
    /// </summary>
    [DataMember(26)]
    public int DiffuseSteps
    {
        get => diffuseSteps > 0 ? diffuseSteps : Preset.DiffuseSteps;
        set
        {
            if (diffuseSteps == value)
                return;
            diffuseSteps = value;
            if (Volume != null)
                Rebuild();
        }
    }

    private int diffuseSteps;

    /// <summary>Cost/quality tier. Changing it at runtime rebuilds the voxel storage.</summary>
    [DataMember(20)]
    public VoxelGIQuality Quality
    {
        get => quality;
        set
        {
            if (quality == value)
                return;
            quality = value;
            if (Volume != null)
                Rebuild();
        }
    }

    /// <summary>
    /// Strength of the indirect light, as seen by the camera. 1 is "physical-ish"; artists
    /// routinely push 1.5-2 because a single bounce loses the energy later ones would have added.
    /// </summary>
    [DataMember(30)]
    public float BounceIntensity
    {
        get => bounceIntensity;
        set { bounceIntensity = Math.Max(0f, value); Apply(); }
    }

    /// <summary>
    /// How much of the indirect light is written back into the voxels, feeding the next bounce.
    /// 0 gives a single bounce; 1 lets light keep spreading, at no extra cost but with a frame of
    /// lag and a risk of runaway brightness in a closed white room.
    /// </summary>
    [DataMember(35)]
    public float SecondBounce
    {
        get => secondBounce;
        set { secondBounce = Math.Max(0f, value); Apply(); }
    }

    /// <summary>
    /// How much thin geometry is thickened during voxelization - see <see cref="VoxelGIPreset.Opacify"/>.
    /// Rebuilds the volume. Too high and a surface occludes the cones leaving it, which costs more
    /// indirect light than the leaking it prevents.
    /// </summary>
    [DataMember(38)]
    public float Opacify
    {
        get => opacify;
        set
        {
            if (Math.Abs(opacify - value) < 0.0001f)
                return;
            opacify = Math.Max(0f, value);
            if (Volume != null)
                Rebuild();
        }
    }

    /// <summary>Strength of the cone-traced specular (voxel reflections). 0 removes the look, not the cost.</summary>
    [DataMember(40)]
    public float SpecularIntensity
    {
        get => specularIntensity;
        set { specularIntensity = Math.Max(0f, value); Apply(); }
    }

    /// <summary>Whether the GI light contributes. Toggling it is the honest before/after switch.</summary>
    [DataMember(50)]
    public bool GIEnabled
    {
        get => giEnabled;
        set { giEnabled = value; Apply(); }
    }

    /// <summary>
    /// Keep refreshing the voxels. Turn it off to freeze the last capture: the cones keep tracing it,
    /// and the voxelization pass costs nothing. That is the right setting for static geometry.
    /// </summary>
    [DataMember(60)]
    public bool Voxelize
    {
        get => voxelize;
        set { voxelize = value; Apply(); }
    }

    /// <summary>
    /// Voxelize only when something changed: after a rebuild or settings change, and whenever the
    /// volume has moved by at least one voxel (each burst runs one round of clipmap levels). A
    /// static scene watched by a moving camera then costs nothing in voxelization. The wrapper
    /// cannot see scene edits, so a game that moves geometry or lights inside the volume calls
    /// <see cref="MarkDirty"/> when it does - or leaves this off.
    /// </summary>
    [DataMember(65)]
    public bool AutoFreeze
    {
        get => autoFreeze;
        set
        {
            autoFreeze = value;
            // Coming out of auto mode, hand Voxelize back to its own knob.
            if (!value)
                Apply();
        }
    }

    /// <summary>
    /// Tells an <see cref="AutoFreeze"/> volume that its content changed: geometry moved, a light
    /// changed, a door opened. Schedules one full round of clipmap re-voxelization.
    /// </summary>
    public void MarkDirty() => voxelizeFramesLeft = EffectiveClipMapLevels + 1;

    /// <summary>Whether <see cref="AutoFreeze"/> is currently holding voxelization off.</summary>
    [DataMemberIgnore]
    public bool IsFrozen => autoFreeze && voxelizeFramesLeft <= 0;

    /// <summary>Replace the frame with a view of the voxels themselves. See <see cref="VoxelGIDebugView"/>.</summary>
    [DataMember(70)]
    [Display(category: "Debug")]
    public VoxelGIDebugView DebugView
    {
        get => debugView;
        set { debugView = value; Apply(); }
    }

    /// <summary>Rebuilds the debug visualization, picking up a changed <see cref="DebugMipmap"/>.</summary>
    public void RefreshDebugView() => Apply();

    /// <summary>Which mipmap level <see cref="VoxelGIDebugView.Raw"/> slices through.</summary>
    [DataMember(80)]
    [Display(category: "Debug")]
    public int DebugMipmap { get; set; }

    /// <summary>
    /// Overrides the preset's <see cref="VoxelGIPreset.GIResolutionDivisor"/>: 1 traces the diffuse
    /// cones per shaded pixel, 2 traces a quarter of them and upsamples, 4 a sixteenth. Zero keeps
    /// whatever the quality preset asks for.
    /// <para>
    /// It needs a depth-only render stage on the graphics compositor to prime the depth buffer the
    /// pass reads; without one the light quietly keeps marching inline.
    /// </para>
    /// </summary>
    [DataMember(85)]
    public int GIResolutionDivisor
    {
        get => giResolutionDivisor;
        set { giResolutionDivisor = value; Apply(); }
    }

    private int giResolutionDivisor;

    /// <summary>The divisor actually in force, preset included.</summary>
    [DataMemberIgnore]
    public int EffectiveGIResolutionDivisor => giResolutionDivisor > 0 ? giResolutionDivisor : Preset.GIResolutionDivisor;

    /// <summary>
    /// Overrides the preset's <see cref="VoxelGIPreset.SpecularConeRatio"/>: the aperture of the
    /// cone the reflections are traced with, where zero keeps the tier's own. Closing it is what
    /// turns a metal from a smear into a reflection - and closing it all the way is a ray march,
    /// which costs what a ray march costs. This is the knob to turn before adding a light.
    /// </summary>
    [DataMember(86)]
    public float SpecularConeRatio
    {
        get => specularConeRatio;
        set { specularConeRatio = value; Apply(); }
    }

    private float specularConeRatio;

    /// <summary>The aperture actually in force, preset included.</summary>
    [DataMemberIgnore]
    public float EffectiveSpecularConeRatio => specularConeRatio > 0f ? specularConeRatio : Preset.SpecularConeRatio;

    /// <summary>
    /// Overrides the preset's <see cref="VoxelGIPreset.SpecularSteps"/>, or zero for the tier's.
    /// This is the range half of <see cref="SpecularConeRatio"/> and moves with it.
    /// </summary>
    [DataMemberIgnore]
    public int SpecularSteps
    {
        get => specularSteps;
        set { specularSteps = value; Apply(); }
    }

    private int specularSteps;

    /// <summary>
    /// Furthest a reflection may travel, in world units; zero keeps the preset's.
    /// See <see cref="VoxelGIPreset.SpecularRange"/> for why a reflection needs a horizon at all.
    /// </summary>
    [DataMember(87)]
    public float SpecularRange
    {
        get => specularRange;
        set { specularRange = MathF.Max(0f, value); Apply(); }
    }

    /// <remarks>
    /// Initialised from <see cref="VoxelGIPreset.SpecularRange"/> rather than falling back to it,
    /// because zero is a setting and not an absence: it is the unlimited march this wrapper started
    /// with. Reading it as "unset" the way the other overrides do would make the one value worth
    /// comparing against unreachable.
    /// </remarks>
    private float specularRange = VoxelGIPreset.DefaultSpecularRange;

    /// <summary>The range in force. Zero is no horizon, not "ask the preset".</summary>
    [DataMemberIgnore]
    public float EffectiveSpecularRange => specularRange;

    /// <summary>The step count actually in force, preset included.</summary>
    [DataMemberIgnore]
    public int EffectiveSpecularSteps => specularSteps > 0 ? specularSteps : Preset.SpecularSteps;

    /// <summary>
    /// Overrides the preset's <see cref="VoxelGIPreset.SpecularRoughnessCutoff"/>, or zero for the
    /// tier's. It is what keeps a long, sharp cone affordable: past the cutoff the march is skipped
    /// entirely, so a couple of hundred steps are paid for by the polished things that need them
    /// and by nothing else in the room.
    /// </summary>
    [DataMemberIgnore]
    public float SpecularRoughnessCutoff
    {
        get => specularRoughnessCutoff;
        set { specularRoughnessCutoff = value; Apply(); }
    }

    private float specularRoughnessCutoff;

    /// <summary>
    /// How far along the normal, in voxels, every cone starts from the shaded point: the diffuse
    /// set's offset and the specular cone's alike. Zero keeps the marchers' own default of one.
    /// Curved surfaces draw rings when a cone grazes its own voxel shell; starting further out clears them.
    /// </summary>
    [DataMemberIgnore]
    public float ConeOffset
    {
        get => coneOffset;
        set { coneOffset = value; Apply(); }
    }

    private float coneOffset;

    /// <summary>
    /// What a cone sees where it leaves the volume without meeting anything: the sky, credited by
    /// how much of the cone is still open. Black leaves the volume lit by its emitters alone.
    /// </summary>
    [DataMemberIgnore]
    public Color3 SkyColor
    {
        get => skyColor;
        set { skyColor = value; Apply(); }
    }

    private Color3 skyColor;

    /// <summary>Multiplier on <see cref="SkyColor"/>.</summary>
    [DataMemberIgnore]
    public float SkyIntensity
    {
        get => skyIntensity;
        set { skyIntensity = value; Apply(); }
    }

    private float skyIntensity = 1f;

    /// <summary>The cutoff actually in force, preset included.</summary>
    [DataMemberIgnore]
    public float EffectiveSpecularRoughnessCutoff
        => specularRoughnessCutoff > 0f ? specularRoughnessCutoff : Preset.SpecularRoughnessCutoff;

    /// <summary>The volume built by this component. Null until <see cref="Start"/> has run.</summary>
    [DataMemberIgnore]
    public VoxelVolumeComponent? Volume { get; private set; }

    /// <summary>The environment light built by this component. Null until <see cref="Start"/> has run.</summary>
    [DataMemberIgnore]
    public LightComponent? Light { get; private set; }

    /// <summary>The GI light as a <see cref="LightVoxel"/>, for anything this wrapper doesn't expose.</summary>
    [DataMemberIgnore]
    public LightVoxel? VoxelLight => Light?.Type as LightVoxel;

    /// <summary>The preset currently in use.</summary>
    [DataMemberIgnore]
    public VoxelGIPreset Preset { get; private set; } = VoxelGIPreset.For(VoxelGIQuality.Medium);

    /// <summary>
    /// The clipmap levels actually allocated. The storage texture gives each level a column of its
    /// own along X and each direction a row along Y, and Direct3D11 caps a 3D texture
    /// at 2048 per axis - so a 128-voxel ring fits sixteen levels and a 256-voxel one eight,
    /// whatever the directionality. Asking for more would be an E_INVALIDARG crash in
    /// CreateTexture3D, so the request is clamped here instead.
    /// </summary>
    [DataMemberIgnore]
    public int EffectiveClipMapLevels
        => Math.Min(clipMapLevels, MaxClipMapLevels);

    /// <summary>Edge of a single voxel of the finest clipmap, in world units.</summary>
    [DataMemberIgnore]
    public float VoxelSize => Preset.VoxelSizeFor(volumeSize / (1 << (EffectiveClipMapLevels - 1)));

    /// <summary>
    /// Re-voxelize every clipmap ring each frame instead of one ring per frame.
    /// </summary>
    /// <remarks>
    /// What "the lighting swims when I walk" actually is. In the default single-ring mode a ring is
    /// refreshed once every ClipMapLevels frames and keeps the snapping offset it was given then,
    /// so at any instant the room is lit by several rings each anchored to a different past camera
    /// position - and each snapped to its own grid, the coarsest jumping by eight times the finest.
    /// Updating them together costs roughly one voxelization per ring instead of one in total, and
    /// removes the lag but not the snapping: only anchoring the volume removes that.
    /// </remarks>
    [DataMember(66)]
    public bool UpdateAllClipmapsEveryFrame
    {
        get => updateAllClipmaps;
        set
        {
            if (updateAllClipmaps == value)
                return;
            updateAllClipmaps = value;
            if (Volume != null)
                Rebuild();
        }
    }

    private bool updateAllClipmaps;

    /// <summary>
    /// How radiance survives the mipmap chain. See <see cref="VoxelGIPreset.LightFalloff"/>.
    /// Rebuilds the volume.
    /// </summary>
    [DataMember(39)]
    public VoxelAttributeEmissionOpacity.LightFalloffs LightFalloff
    {
        get => lightFalloff;
        set
        {
            if (lightFalloff == value)
                return;
            lightFalloff = value;
            if (Volume != null)
                Rebuild();
        }
    }

    private VoxelAttributeEmissionOpacity.LightFalloffs lightFalloff = VoxelAttributeEmissionOpacity.LightFalloffs.Heuristic;

    private bool autoFreeze;
    private Int3 lastSnappedPosition;
    private int voxelizeFramesLeft;

    private Entity? lightEntity;

    public override void Start()
    {
        Rebuild();
    }

    public override void Update()
    {
        // World position, so a camera nested under another entity is followed correctly. The
        // volume entity is expected to be a scene root; if it were itself nested, the parent's
        // transform would double up here.
        if (Follow != null)
            Entity.Transform.Position = Follow.WorldMatrix.TranslationVector;

        if (!autoFreeze || Volume == null)
            return;

        // One-voxel granularity: VoxelGridSnapping quantizes the voxelized volume to whole
        // voxels, so a sub-voxel move re-voxelizes to the exact same content - only an actual
        // grid crossing is a change worth waking up for.
        // Clamped before the cast: a position far enough out, or a voxel small enough, would
        // overflow the int and the volume would then wake on every frame or never.
        var gridPosition = Entity.Transform.Position / VoxelSize;
        const float reach = 1 << 30;
        var snapped = new Int3(
            (int)MathF.Floor(Math.Clamp(gridPosition.X, -reach, reach)),
            (int)MathF.Floor(Math.Clamp(gridPosition.Y, -reach, reach)),
            (int)MathF.Floor(Math.Clamp(gridPosition.Z, -reach, reach)));
        if (snapped != lastSnappedPosition)
        {
            lastSnappedPosition = snapped;
            MarkDirty();
        }

        Volume.Voxelize = voxelize && voxelizeFramesLeft > 0;
        if (voxelizeFramesLeft > 0)
            voxelizeFramesLeft--;
    }

    public override void Cancel()
    {
        if (Volume != null)
            Entity.Remove(Volume);
        if (lightEntity != null)
            Entity.RemoveChild(lightEntity);

        Volume = null;
        Light = null;
        lightEntity = null;
    }

    /// <summary>
    /// Rebuilds the voxel storage and the cone sets from the current <see cref="Quality"/>. The
    /// volume drops its textures and re-voxelizes, so expect a hitch: this is a settings change,
    /// not a per-frame call.
    /// </summary>
    public void Rebuild()
    {
        Preset = VoxelGIPreset.For(quality);
        Preset.Opacify = opacify;
        Preset.UpdateAllClipmapsEveryFrame = updateAllClipmaps;
        Preset.LightFalloff = lightFalloff;
        if (voxelizationMSAA is { } msaa)
            Preset.VoxelizationMSAA = msaa;
        if (clipResolution is { } resolution)
            Preset.ClipResolution = resolution;
        if (directionality is { } directions)
            Preset.Directionality = directions;
        if (diffuseCones > 0)
            Preset.DiffuseCones = diffuseCones;
        if (diffuseSteps > 0)
            Preset.DiffuseSteps = diffuseSteps;

        // Replace the volume and the light rather than reconfiguring them in place. Everything
        // downstream caches by instance: the voxel renderer keys its per-volume data on the
        // VoxelVolumeComponent, and the light renderer keys its shader group on the RenderLight and
        // holds the attribute it last resolved. Swapping storage and attributes on a live volume
        // left those caches straddling two generations, and the buffer-clearing dispatch sized its
        // thread groups from one while writing the other's buffers - a hard crash inside D3D11.
        // A quality change reallocates every voxel texture anyway, so there is nothing to save here.
        if (Volume != null)
            Entity.Remove(Volume);
        if (lightEntity != null)
            Entity.RemoveChild(lightEntity);

        Volume = new VoxelVolumeComponent
        {
            VoxelizationMethod = new VoxelizationMethodDominantAxis { MultisampleCount = Preset.VoxelizationMSAA },
            Storage = Preset.CreateStorage(),
            VoxelGridSnapping = true,
        };
        Volume.Attributes.Add(Preset.CreateAttribute());
        Entity.Add(Volume);

        // The light lives on a child entity so this component never fights a LightComponent that is
        // already on the entity it was dropped onto.
        lightEntity = new Entity("Voxel GI Light");
        Light = new LightComponent
        {
            Intensity = 1f,
            Type = new LightVoxel
            {
                Volume = Volume,
                AttributeIndex = 0,
                DiffuseMarcher = Preset.CreateDiffuseMarcher(),
                BounceMarcher = Preset.CreateBounceMarcher(),
                SpecularMarcher = Preset.CreateSpecularMarcher(),
            },
        };
        lightEntity.Add(Light);
        Entity.AddChild(lightEntity);

        Apply();
    }

    /// <summary>
    /// Pushes the cheap knobs (size, intensities, enabled, debug view) onto the live objects.
    /// The setters call it for you; call it yourself if you poke the underlying types directly.
    /// </summary>
    public void Apply()
    {
        if (Volume == null || Light == null)
            return;

        // A tier change can lower the ceiling under a request that was legal at the previous one.
        clipMapLevels = Math.Clamp(clipMapLevels, 1, MaxClipMapLevels);

        Volume.VoxelVolumeSize = volumeSize;
        // The storage derives its clipmap count from VolumeSize / AproximateVoxelSize: each factor
        // of two past the preset's resolution becomes one more ring. Asking for the finest ring's
        // voxel size is therefore what turns ClipMapLevels on.
        Volume.AproximateVoxelSize = VoxelSize;
        Volume.Voxelize = voxelize;
        Volume.VisualizeVoxels = debugView != VoxelGIDebugView.Off;
        Volume.VisualizeIndex = 0;
        Volume.Visualization = CreateVisualization();

        Light.Enabled = giEnabled;

        // How strong the indirect light looks is LightComponent.Intensity: LightVoxelShaderGroup
        // reads it for the camera view and only folds BounceIntensityScale in when rendering into
        // the voxels, where it decides how much of this bounce is re-injected for the next one.
        // Driving BounceIntensityScale therefore changes nothing on screen, however far it is pushed.
        Light.Intensity = bounceIntensity;
        if (Light.Type is LightVoxel voxelLight)
        {
            voxelLight.BounceIntensityScale = secondBounce;
            voxelLight.SpecularIntensityScale = specularIntensity;
            voxelLight.SpecularRoughnessCutoff = EffectiveSpecularRoughnessCutoff;
            voxelLight.SkyColor = skyColor;
            voxelLight.SkyIntensity = skyIntensity;
            if (coneOffset > 0f)
            {
                voxelLight.SpecularOffset = coneOffset;
                if (voxelLight.DiffuseMarcher is VoxelMarchSetBase diffuseSet)
                    diffuseSet.Offset = coneOffset;
                if (voxelLight.BounceMarcher is VoxelMarchSetBase bounceSet)
                    bounceSet.Offset = coneOffset;
            }
            // Reuse the marcher when only the range moved. LightVoxelRenderer composes a marcher's
            // parameter keys in UpdateMarchingLayout, and only calls it when the shader permutation
            // changes - which range, being a uniform rather than a template argument, does not do.
            // A fresh instance would therefore carry an uncomposed key and write its value nowhere.
            // Aperture and step count still need a new one: they are baked into the ShaderSource.
            if (voxelLight.SpecularMarcher is VoxelMarchCone cone
                && cone.Steps == EffectiveSpecularSteps
                && MathF.Abs(cone.ConeRatio - EffectiveSpecularConeRatio) < 0.0001f)
            {
                cone.MaxDistance = EffectiveSpecularRange;
            }
            else
            {
                voxelLight.SpecularMarcher = Preset.CreateSpecularMarcher(specularConeRatio, specularSteps, EffectiveSpecularRange);
            }
            voxelLight.ScreenSpaceDivisor = EffectiveGIResolutionDivisor;
        }

        // Any settings change can affect what the voxels hold (intensity feeds the second-bounce
        // re-injection during voxelization), so an AutoFreeze volume wakes up for one round.
        MarkDirty();
    }

    private IVoxelVisualization? CreateVisualization() => debugView switch
    {
        // Constants, and that is the point: steps and step scale are both template parameters of
        // VoxelMarchBeam, baked into the ShaderClassSource. Deriving the count from the volume gave
        // a fresh shader permutation for every quality tier and every volume size - each one a cold
        // compile of a specialised loop with texture fetches in it, which is why switching to this
        // view used to hang for minutes, and again after every Ctrl+Q.
        //
        // The old count also lied: it asked for twice the volume in one-voxel steps, 4085 of them
        // here, and the clamp cut that to 1024 - so the advertised range was never delivered.
        // 512 steps of two voxels reach the same 1024 voxels for half the compile.
        //
        // The step is what sets the banding, and it is worth spending on. The beam advances by a
        // fixed amount and samples at a fixed radius, so its samples sit on evenly spaced shells
        // centred on the eye; where a shell grazes a surface it leaves a band, and a family of them
        // cuts a wall into rings centred on the view axis. Doubling the step doubles their width -
        // which is already visible. Quadrupling it, for a compile that is only twice as fast again,
        // is not a trade worth making on the one view whose whole job is to show the data plainly.
        VoxelGIDebugView.Cones => new VoxelVisualizationView
        {
            MarchMethod = new VoxelMarchBeam(512, 2.0f, 1.0f),
            Background = new Color(0.05f, 0.05f, 0.07f, 1.0f),
        },
        VoxelGIDebugView.Raw => new VoxelVisualizationRaw
        {
            Mipmap = Math.Max(0, DebugMipmap),
            Range = new Vector2(0f, 1f),
        },
        _ => null,
    };
}

/// <summary>What <see cref="VoxelGIVolume.DebugView"/> draws over the frame.</summary>
public enum VoxelGIDebugView
{
    /// <summary>Normal rendering.</summary>
    [Display("Off")] Off,

    /// <summary>Ray-march the voxels from the camera: the world as the cones see it.</summary>
    [Display("Voxels (ray-marched)")] Cones,

    /// <summary>Slice straight through the storage texture at <see cref="VoxelGIVolume.DebugMipmap"/>.</summary>
    [Display("Raw storage slice")] Raw,
}
