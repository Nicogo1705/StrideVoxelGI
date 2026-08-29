// Copyright (c) 2026 Nicogo. Distributed under the MIT license.
using System;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;

namespace StrideVoxelGI;

/// <summary>
/// Hotkeys and an on-screen readout for a <see cref="VoxelGIVolume"/>. Drop it next to the volume
/// (or point <see cref="Target"/> at one) to get the before/after toggle, the voxel views and the
/// quality tiers without opening the property grid.
/// <para>
/// It is a debug tool, not part of the renderer: delete it from your scene and the GI is unchanged.
/// </para>
/// </summary>
[Display("Voxel GI Debug", Expand = ExpandRule.Once)]
[ComponentCategory("Lights")]
public class VoxelGIDebug : SyncScript
{
    /// <summary>The volume to drive. Defaults to one on this entity.</summary>
    [DataMember(10)]
    public VoxelGIVolume? Target { get; set; }

    /// <summary>Draw the readout in the corner of the screen.</summary>
    [DataMember(20)]
    public bool ShowOverlay { get; set; } = true;

    /// <summary>Top-left corner of the readout, in pixels.</summary>
    [DataMember(30)]
    public Int2 OverlayPosition { get; set; } = new Int2(16, 16);

    /// <summary>Toggles the indirect light on and off.</summary>
    [DataMember(40)]
    public Keys ToggleGIKey { get; set; } = Keys.G;

    /// <summary>Cycles off / ray-marched voxels / raw storage slice.</summary>
    [DataMember(50)]
    public Keys CycleViewKey { get; set; } = Keys.V;

    /// <summary>Freezes and unfreezes voxelization.</summary>
    [DataMember(60)]
    public Keys FreezeKey { get; set; } = Keys.F;

    /// <summary>Cycles the quality preset.</summary>
    [DataMember(70)]
    public Keys CycleQualityKey { get; set; } = Keys.Q;

    /// <summary>Lowers the bounce intensity by <see cref="BounceStep"/>.</summary>
    [DataMember(80)]
    public Keys BounceDownKey { get; set; } = Keys.OemOpenBrackets;

    /// <summary>Raises the bounce intensity by <see cref="BounceStep"/>.</summary>
    [DataMember(90)]
    public Keys BounceUpKey { get; set; } = Keys.OemCloseBrackets;

    /// <summary>How much one bounce key press moves <see cref="VoxelGIVolume.BounceIntensity"/>.</summary>
    [DataMember(100)]
    public float BounceStep { get; set; } = 0.25f;

    public override void Start()
    {
        Target ??= Entity.Get<VoxelGIVolume>();
    }

    public override void Update()
    {
        var target = Target;
        if (target == null)
            return;

        if (Input.IsKeyPressed(ToggleGIKey))
            target.GIEnabled = !target.GIEnabled;

        if (Input.IsKeyPressed(CycleViewKey))
            target.DebugView = target.DebugView switch
            {
                VoxelGIDebugView.Off => VoxelGIDebugView.Cones,
                VoxelGIDebugView.Cones => VoxelGIDebugView.Raw,
                _ => VoxelGIDebugView.Off,
            };

        if (Input.IsKeyPressed(FreezeKey))
            target.Voxelize = !target.Voxelize;

        if (Input.IsKeyPressed(CycleQualityKey))
            target.Quality = target.Quality switch
            {
                VoxelGIQuality.Low => VoxelGIQuality.Medium,
                VoxelGIQuality.Medium => VoxelGIQuality.High,
                VoxelGIQuality.High => VoxelGIQuality.Ultra,
                _ => VoxelGIQuality.Low,
            };

        if (Input.IsKeyPressed(BounceDownKey))
            target.BounceIntensity = MathF.Max(0f, target.BounceIntensity - BounceStep);

        if (Input.IsKeyPressed(BounceUpKey))
            target.BounceIntensity += BounceStep;

        if (!ShowOverlay)
            return;

        var line = OverlayPosition;
        void Print(string text)
        {
            DebugText.Print(text, line);
            line.Y += 18;
        }

        Print($"[{ToggleGIKey}] Voxel GI      : {(target.GIEnabled ? "ON" : "OFF")}");
        Print($"[{CycleViewKey}] Voxel view    : {target.DebugView}");
        Print($"[{FreezeKey}] Voxelization  : {(target.Voxelize ? "live" : "frozen")}");
        Print($"[{CycleQualityKey}] Quality       : {target.Quality} ({target.Preset.Resolution}^3, {target.Preset.DiffuseCones} cones)");
        Print($"[{BounceDownKey}/{BounceUpKey}] Bounce  : {target.BounceIntensity:0.00}");
        Print($"    Volume        : {target.VolumeSize:0.#} units, voxel {target.VoxelSize:0.###}");
    }
}
