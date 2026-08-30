// Copyright (c) 2026 Nicogo. Distributed under the MIT license.
using System;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
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
            value = Math.Clamp(value, 1, 6);
            if (clipMapLevels == value)
                return;
            clipMapLevels = value;
            if (Volume != null)
                Rebuild();
        }
    }

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
    /// The clipmap levels actually allocated. The storage texture stacks every level and, for
    /// anisotropic presets, all six directions along its Y axis, and Direct3D11 caps a 3D texture
    /// at 2048 per axis - so High/Ultra (128/256 voxels x 6 directions) can only fit 2 or 1
    /// levels. Asking for more would be an E_INVALIDARG crash in CreateTexture3D, so the request
    /// is clamped here instead.
    /// </summary>
    [DataMemberIgnore]
    public int EffectiveClipMapLevels
        => Math.Min(clipMapLevels, Math.Max(1, 2048 / (Preset.Resolution * (Preset.Anisotropic ? 6 : 1))));

    /// <summary>Edge of a single voxel of the finest clipmap, in world units.</summary>
    [DataMemberIgnore]
    public float VoxelSize => Preset.VoxelSizeFor(volumeSize / (1 << (EffectiveClipMapLevels - 1)));

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
        var gridPosition = Entity.Transform.Position / VoxelSize;
        var snapped = new Int3((int)MathF.Floor(gridPosition.X), (int)MathF.Floor(gridPosition.Y), (int)MathF.Floor(gridPosition.Z));
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
            VoxelizationMethod = new VoxelizationMethodDominantAxis(),
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
        }

        // Any settings change can affect what the voxels hold (intensity feeds the second-bounce
        // re-injection during voxelization), so an AutoFreeze volume wakes up for one round.
        MarkDirty();
    }

    private IVoxelVisualization? CreateVisualization() => debugView switch
    {
        // The beam advances one voxel per step, so a fixed step count makes the view's range
        // shrink as voxels get smaller - at 256^3 it stopped about 11 units out, and the volume
        // seemed to vanish when the camera backed away. Deriving the count from the volume keeps
        // the range at roughly twice the volume whatever the quality, so the views compare.
        VoxelGIDebugView.Cones => new VoxelVisualizationView
        {
            MarchMethod = new VoxelMarchBeam(Math.Clamp((int)(volumeSize * 2f / VoxelSize), 64, 1024), 1.0f, 1.0f),
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
