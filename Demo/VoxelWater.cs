// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using Csl;
using Stride.Core.Mathematics;
using Half = Stride.Core.Mathematics.Half;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering.Voxels.Grid;

namespace Demo;

/// <summary>
/// Water as an amount per voxel, simulated and drawn without leaving the GPU.
/// </summary>
/// <remarks>
/// <para>
/// The CPU keeps the dry terrain (the samples the collider and the digging read) and uploads it to
/// <see cref="Terrain"/>. Everything else lives here: the amounts, in two textures that swap each step;
/// the drawn <see cref="Field"/>, composed from the terrain and the water at the finest level and
/// filtered down its mip chain; and the <see cref="Occupancy"/> pyramid the traversal skips with.
/// </para>
/// <para>
/// Work is done per brick of 8x8x8 cells. A step spreads the flags of the bricks that changed to
/// their neighbours, moves the water in those, recomposes them, and rebuilds the mips and the pyramid
/// over them - a lake at rest costs a few empty dispatches. The CPU never reads anything back; a dig
/// or a pour only marks the bricks it touched, and the GPU takes it from there on the next step.
/// </para>
/// <para>
/// The passes are the generated wrappers of the VoxelWater*.sdsl shaders: each parameter is a typed
/// property, each texture is allocated from the slots that bind it, and a dispatch takes the number
/// of cells to cover.
/// </para>
/// </remarks>
public sealed class VoxelWater : IDisposable
{
    public const int BrickSize = 8;

    /// <summary>The material id the composed field gives to water.</summary>
    public const int WaterMaterial = 13;

    /// <summary>Samples per axis.</summary>
    public int Samples { get; }

    public Int3 SampleCount => new(Samples);
    public Int3 BrickCount { get; }

    /// <summary>The dry field, density and material, one level: what the CPU writes.</summary>
    public Texture Terrain { get; }

    /// <summary>The field the grid draws: terrain and water composed, with its mip chain.</summary>
    public Texture Field { get; }

    /// <summary>The min/max pyramid over <see cref="Field"/>, rebuilt here.</summary>
    public VoxelGridOccupancy Occupancy { get; }

    /// <summary>How much a cell can hold over one per cell of water above it: the pressure that lifts water.</summary>
    public float MaxCompress { get; set; } = 0.02f;

    /// <summary>Amounts and hand-overs are multiples of this; a power of two, so every value is exact in a half float.</summary>
    public float Quantum { get; set; } = 1f / 256f;

    /// <summary>A film thinner than this soaks away: two quanta, so no residue is left trading single quanta.</summary>
    public float Soak { get; set; } = 2.5f / 256f;

    /// <summary>A cell with this much or more, over less than a full cell, is drawn as a film; see VoxelWaterCompose.</summary>
    public float FilmMin { get; set; } = 0.05f;

    /// <summary>The least density a film is drawn with: a shade over the iso level, a thin sheet on the ground.</summary>
    public float FilmDensity { get; set; } = 0.5625f;

    /// <summary>Flow steps per <see cref="Step"/>: the water moves a cell per sub-step, the drawing is redone once.</summary>
    public int Substeps { get; set; } = 3;

    /// <summary>Steps taken so far.</summary>
    public int StepCount { get; private set; }

    /// <summary>The amounts as of the last step, one half float per sample.</summary>
    public Texture Amounts => amounts.Current;

    private readonly ShaderContext context;

    // The amounts swap each sub-step: a pass reads Current and writes Next.
    private readonly PingPong<Texture> amounts;

    // One flag per brick: marked from the CPU or by the step, active this sub-step, drawn this step.
    private readonly Texture changed;
    private readonly Texture active;
    private readonly Texture drawn;

    // One view per mip: a compute pass reads one level and writes the next.
    private readonly MipChain fieldLevels;
    private readonly MipChain occupancyLevels;

    private readonly VoxelWaterSpreadEffect spread;
    private readonly VoxelWaterStepEffect step;
    private readonly VoxelWaterComposeEffect compose;
    private readonly VoxelWaterMipEffect mip;
    private readonly VoxelWaterOccupancyBaseEffect occupancyBase;
    private readonly VoxelWaterOccupancyUpEffect occupancyUp;

    private Vector3 pourCentre;
    private float pourRadius;
    private float pourAmount;

    public VoxelWater(IGame game, int samples, float isoLevel)
    {
        context = ShaderContext.Get(game);
        Samples = samples;
        BrickCount = new Int3((samples + BrickSize - 1) / BrickSize);
        var device = game.GraphicsDevice;

        // The element type gives the format, the slots give the views. The brick flags are uint
        // because the spread pass reads its own Drawn output back, and a typed UAV load is only
        // allowed on 32-bit single-channel formats: New3D refuses anything narrower on that slot.
        Terrain = Textures.New3D<Texels.Rg8>(device, SampleCount, VoxelWaterStepEffect.Slots.Terrain);
        Field = Textures.New3D<Texels.Rg8>(device, SampleCount, MipMapCount.Auto, VoxelWaterComposeEffect.Slots.FieldOut, VoxelWaterMipEffect.Slots.Source);
        amounts = new PingPong<Texture>(() => Textures.New3D<Half>(device, SampleCount, VoxelWaterStepEffect.Slots.Amounts, VoxelWaterStepEffect.Slots.AmountsOut));
        changed = Textures.New3D<uint>(device, BrickCount, VoxelWaterSpreadEffect.Slots.ChangedBricks, VoxelWaterStepEffect.Slots.ChangedOut);
        active = Textures.New3D<uint>(device, BrickCount, VoxelWaterSpreadEffect.Slots.ActiveOut, VoxelWaterBricksEffect.Slots.ActiveBricks);
        drawn = Textures.New3D<uint>(device, BrickCount, VoxelWaterSpreadEffect.Slots.DrawnOut, VoxelWaterBricksEffect.Slots.ActiveBricks);
        Occupancy = new VoxelGridOccupancy(device, SampleCount, unorderedAccess: true);
        fieldLevels = Field.MipViews();
        occupancyLevels = Occupancy.Texture.MipViews();

        // The passes: one thread per brick where the work is per brick, one per cell otherwise.
        // What never changes is set here; Step sets what does.
        var services = game.Services;
        spread = new VoxelWaterSpreadEffect(services, 4)
        {
            ChangedBricks = changed,
            ActiveOut = active,
            DrawnOut = drawn,
            BrickCount = BrickCount,
        };
        step = new VoxelWaterStepEffect(services, BrickSize)
        {
            ActiveBricks = active,
            ChangedOut = changed,
            Terrain = Terrain,
            IsoLevel = isoLevel,
        };
        compose = new VoxelWaterComposeEffect(services, BrickSize)
        {
            ActiveBricks = drawn,
            Terrain = Terrain,
            FieldOut = fieldLevels[0],
            WaterMaterial = WaterMaterial / 255f,
        };
        mip = new VoxelWaterMipEffect(services, BrickSize) { ActiveBricks = drawn };
        occupancyBase = new VoxelWaterOccupancyBaseEffect(services, 4)
        {
            ActiveBricks = drawn,
            Field = fieldLevels[0],
            Target = occupancyLevels[0],
            TargetSize = occupancyLevels.SizeAt(0),
        };
        occupancyUp = new VoxelWaterOccupancyUpEffect(services, 4) { ActiveBricks = drawn };
        foreach (var pass in new VoxelWaterBricksEffect[] { step, compose, mip, occupancyBase, occupancyUp })
        {
            pass.SampleCount = SampleCount;
            pass.BrickCount = BrickCount;
        }

        // Every brick is marked at the start: the first step composes the whole field.
        MarkAll();
    }

    private CommandList CommandList => context.CommandList;

    // -- what the CPU hands over ------------------------------------------------------------------

    /// <summary>The whole dry field: two bytes per sample, x fastest as the texture is laid out.</summary>
    public void UploadTerrain(byte[] texels) => Terrain.SetData(CommandList, texels);

    /// <summary>The dry field over a box of samples, inclusive, from the same array.</summary>
    public void UploadTerrain(byte[] texels, Int3 lo, Int3 hi)
    {
        Terrain.UploadRegion(CommandList, MemoryMarshal.Cast<byte, Texels.Rg8>(texels), SampleCount, lo, hi);
        MarkDirty(lo, hi);
    }

    /// <summary>The starting amounts, one per sample in the texture's order, into both textures.</summary>
    public void SeedAmounts(Half[] values)
    {
        amounts.ForEach(texture => texture.SetData(CommandList, values));
        MarkAll();
    }

    /// <summary>Marks the bricks over a box of samples, inclusive, as changed: they and their neighbours run on the next step.</summary>
    public void MarkDirty(Int3 lo, Int3 hi) => changed.FillRegion(CommandList, 1u, lo / BrickSize, hi / BrickSize);

    public void MarkAll() => MarkDirty(Int3.Zero, SampleCount - Int3.One);

    /// <summary>Adds water in a ball, in cell units, on the next step. One pour per step; a second before it replaces the first.</summary>
    public void Pour(Vector3 centreCells, float radiusCells, float amount)
    {
        pourCentre = centreCells;
        pourRadius = radiusCells;
        pourAmount = amount;
        var r = new Vector3(radiusCells + 1f);
        MarkDirty(ToInt3(centreCells - r), ToInt3(centreCells + r));
    }

    private static Int3 ToInt3(Vector3 v) => new((int)MathF.Floor(v.X), (int)MathF.Floor(v.Y), (int)MathF.Floor(v.Z));

    // -- the step --------------------------------------------------------------------------------

    /// <summary>One flow step, then everything drawn from the field over the bricks it touched.</summary>
    public void Step()
    {
        for (int substep = 0; substep < Math.Max(Substeps, 1); substep++)
        {
            // Which bricks run: those that changed, and their neighbours.
            spread.Reset = substep == 0 ? 1 : 0;
            spread.Dispatch(BrickCount);

            // The water moves, from one texture into the other.
            step.Amounts = amounts.Current;
            step.AmountsOut = amounts.Next;
            step.MaxCompress = MaxCompress;
            step.Quantum = Quantum;
            step.Soak = Soak;
            step.PourCentre = pourCentre;
            step.PourRadius = pourRadius;
            step.PourAmount = pourAmount;
            step.Dispatch(SampleCount);
            amounts.Swap();
            pourRadius = 0f;
        }

        // The drawn field, finest level, over every brick that ran.
        compose.Amounts = amounts.Current;
        compose.FilmMin = FilmMin;
        compose.FilmDensity = FilmDensity;
        compose.Dispatch(SampleCount);

        // Its mips, each from the one above, where their sources changed.
        for (int level = 1; level < fieldLevels.Count; level++)
        {
            mip.Source = fieldLevels[level - 1];
            mip.Target = fieldLevels[level];
            mip.SourceSize = fieldLevels.SizeAt(level - 1);
            mip.TargetSize = fieldLevels.SizeAt(level);
            mip.Level = level;
            mip.Dispatch(mip.TargetSize);
        }

        // The pyramid: the base from the field, each level from the one under it.
        occupancyBase.Dispatch(occupancyBase.TargetSize);
        for (int level = 1; level < occupancyLevels.Count; level++)
        {
            occupancyUp.Source = occupancyLevels[level - 1];
            occupancyUp.Target = occupancyLevels[level];
            occupancyUp.SourceSize = occupancyLevels.SizeAt(level - 1);
            occupancyUp.TargetSize = occupancyLevels.SizeAt(level);
            occupancyUp.BrickCells = 1 << (level + 1);
            occupancyUp.Dispatch(occupancyUp.TargetSize);
        }

        StepCount++;
    }

    public void Dispose()
    {
        spread.Dispose();
        step.Dispose();
        compose.Dispose();
        mip.Dispose();
        occupancyBase.Dispose();
        occupancyUp.Dispose();
        fieldLevels.Dispose();
        occupancyLevels.Dispose();
        Occupancy.Dispose();
        active.Dispose();
        changed.Dispose();
        drawn.Dispose();
        amounts.Dispose();
        Field.Dispose();
        Terrain.Dispose();
    }
}
