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
public class VoxelGIVolume : StartupScript
{
    private VoxelGIQuality quality = VoxelGIQuality.Medium;
    private float volumeSize = 24f;
    private float bounceIntensity = 1f;
    private float specularIntensity = 1f;
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
    /// Strength of the indirect diffuse bounce. 1 is "physical-ish"; artists routinely push 1.5-2
    /// because a single bounce loses the energy the later bounces would have added.
    /// </summary>
    [DataMember(30)]
    public float BounceIntensity
    {
        get => bounceIntensity;
        set { bounceIntensity = Math.Max(0f, value); Apply(); }
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

    /// <summary>Replace the frame with a view of the voxels themselves. See <see cref="VoxelGIDebugView"/>.</summary>
    [DataMember(70)]
    [Display(category: "Debug")]
    public VoxelGIDebugView DebugView
    {
        get => debugView;
        set { debugView = value; Apply(); }
    }

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

    /// <summary>Edge of a single voxel of the finest clipmap, in world units.</summary>
    [DataMemberIgnore]
    public float VoxelSize => Preset.VoxelSizeFor(volumeSize);

    private Entity? lightEntity;

    public override void Start()
    {
        Rebuild();
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

        if (Volume == null)
        {
            Volume = new VoxelVolumeComponent();
            Entity.Add(Volume);
        }

        Volume.VoxelizationMethod = new VoxelizationMethodDominantAxis();
        Volume.Storage = Preset.CreateStorage();
        Volume.Attributes.Clear();
        Volume.Attributes.Add(Preset.CreateAttribute());
        Volume.VoxelGridSnapping = true;

        if (lightEntity == null)
        {
            // The light lives on a child entity so this component never fights a LightComponent
            // that is already on the entity it was dropped onto.
            lightEntity = new Entity("Voxel GI Light");
            Light = new LightComponent { Intensity = 1f };
            lightEntity.Add(Light);
            Entity.AddChild(lightEntity);
        }

        Light!.Type = new LightVoxel
        {
            Volume = Volume,
            AttributeIndex = 0,
            DiffuseMarcher = Preset.CreateDiffuseMarcher(),
            SpecularMarcher = Preset.CreateSpecularMarcher(),
        };

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
        Volume.AproximateVoxelSize = Preset.VoxelSizeFor(volumeSize);
        Volume.Voxelize = voxelize;
        Volume.VisualizeVoxels = debugView != VoxelGIDebugView.Off;
        Volume.VisualizeIndex = 0;
        Volume.Visualization = CreateVisualization();

        Light.Enabled = giEnabled;
        if (Light.Type is LightVoxel voxelLight)
        {
            voxelLight.BounceIntensityScale = bounceIntensity;
            voxelLight.SpecularIntensityScale = specularIntensity;
        }
    }

    private IVoxelVisualization? CreateVisualization() => debugView switch
    {
        VoxelGIDebugView.Cones => new VoxelVisualizationView
        {
            MarchMethod = new VoxelMarchBeam(200, 1.0f, 1.0f),
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
