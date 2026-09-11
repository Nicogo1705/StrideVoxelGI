// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Stride.Core.Mathematics;
using Half = Stride.Core.Mathematics.Half;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.ComputeEffect;
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

    /// <summary>Hand-overs under this are not made, so a sheet comes to rest; kept above a half-float's step near one.</summary>
    public float MinFlow { get; set; } = 0.002f;

    /// <summary>A film thinner than this soaks away.</summary>
    public float Soak { get; set; } = 0.005f;

    /// <summary>Steps taken so far.</summary>
    public int StepCount { get; private set; }

    private readonly IGame game;
    private readonly float isoLevel;
    private readonly Texture[] amounts = new Texture[2];
    private int current;
    private readonly Texture active;
    private readonly Texture changed;
    private readonly Texture[] fieldLevels;
    private readonly Texture[] occupancyLevels;

    private ComputeEffectShader? spread, step, compose, mip, occupancyBase, occupancyUp;
    private RenderDrawContext? drawContext;

    private Vector3 pourCentre;
    private float pourRadius;
    private float pourAmount;

    public VoxelWater(IGame game, int samples, float isoLevel)
    {
        this.game = game;
        this.isoLevel = isoLevel;
        Samples = samples;
        BrickCount = new Int3((samples + BrickSize - 1) / BrickSize);
        var device = game.GraphicsDevice;

        Terrain = Texture.New3D(device, samples, samples, samples, 1, PixelFormat.R8G8_UNorm, TextureFlags.ShaderResource, GraphicsResourceUsage.Default);
        Field = Texture.New3D(device, samples, samples, samples, new MipMapCount(true), PixelFormat.R8G8_UNorm,
            TextureFlags.ShaderResource | TextureFlags.UnorderedAccess, GraphicsResourceUsage.Default);
        for (int i = 0; i < 2; i++)
            amounts[i] = Texture.New3D(device, samples, samples, samples, 1, PixelFormat.R16_Float,
                TextureFlags.ShaderResource | TextureFlags.UnorderedAccess, GraphicsResourceUsage.Default);
        active = Texture.New3D(device, BrickCount.X, BrickCount.Y, BrickCount.Z, 1, PixelFormat.R8_UInt,
            TextureFlags.ShaderResource | TextureFlags.UnorderedAccess, GraphicsResourceUsage.Default);
        changed = Texture.New3D(device, BrickCount.X, BrickCount.Y, BrickCount.Z, 1, PixelFormat.R8_UInt,
            TextureFlags.ShaderResource | TextureFlags.UnorderedAccess, GraphicsResourceUsage.Default);
        Occupancy = new VoxelGridOccupancy(device, SampleCount, unorderedAccess: true);

        // One view per mip: a compute pass reads one level and writes the next, and Direct3D wants
        // the two as distinct subresources.
        fieldLevels = new Texture[Field.MipLevelCount];
        for (int level = 0; level < fieldLevels.Length; level++)
            fieldLevels[level] = LevelView(Field, level);
        occupancyLevels = new Texture[Occupancy.Levels];
        for (int level = 0; level < occupancyLevels.Length; level++)
            occupancyLevels[level] = LevelView(Occupancy.Texture, level);

        // Every brick is marked at the start: the first step composes the whole field.
        MarkAll();
    }

    private static Texture LevelView(Texture texture, int level) => texture.ToTextureView(new TextureViewDescription
    {
        Type = ViewType.Single,
        MipLevel = level,
        ArraySlice = 0,
        Flags = TextureFlags.ShaderResource | TextureFlags.UnorderedAccess,
        Format = texture.Format,
    });

    private CommandList CommandList => game.GraphicsContext.CommandList;

    // -- what the CPU hands over ------------------------------------------------------------------

    /// <summary>The whole dry field: two bytes per sample, x fastest as the texture is laid out.</summary>
    public void UploadTerrain(byte[] texels) => Terrain.SetData(CommandList, texels);

    /// <summary>The dry field over a box of samples, inclusive, from the same array.</summary>
    public void UploadTerrain(byte[] texels, Int3 lo, Int3 hi)
    {
        var size = Samples;
        var w = hi.X - lo.X + 1;
        var h = hi.Y - lo.Y + 1;
        var d = hi.Z - lo.Z + 1;
        if (w <= 0 || h <= 0 || d <= 0)
            return;
        var block = new byte[w * h * d * 2];
        for (int z = 0; z < d; z++)
            for (int y = 0; y < h; y++)
                System.Buffer.BlockCopy(texels, (((lo.Z + z) * size + (lo.Y + y)) * size + lo.X) * 2, block, ((z * h + y) * w) * 2, w * 2);
        Terrain.SetData(CommandList, block, 0, 0, new ResourceRegion(lo.X, lo.Y, lo.Z, hi.X + 1, hi.Y + 1, hi.Z + 1));
        MarkDirty(lo, hi);
    }

    /// <summary>The starting amounts, one per sample in the texture's order, into both textures.</summary>
    public void SeedAmounts(Half[] values)
    {
        amounts[0].SetData(CommandList, values);
        amounts[1].SetData(CommandList, values);
        MarkAll();
    }

    /// <summary>Marks the bricks over a box of samples, inclusive, as changed: they and their neighbours run on the next step.</summary>
    public void MarkDirty(Int3 lo, Int3 hi)
    {
        var b0 = Int3.Max(lo / BrickSize, Int3.Zero);
        var b1 = Int3.Min(hi / BrickSize, BrickCount - Int3.One);
        var extent = b1 - b0 + Int3.One;
        if (extent.X <= 0 || extent.Y <= 0 || extent.Z <= 0)
            return;
        var ones = new byte[extent.X * extent.Y * extent.Z];
        Array.Fill(ones, (byte)1);
        changed.SetData(CommandList, ones, 0, 0, new ResourceRegion(b0.X, b0.Y, b0.Z, b1.X + 1, b1.Y + 1, b1.Z + 1));
    }

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
        var services = game.Services;
        var renderContext = RenderContext.GetShared(services);
        drawContext ??= new RenderDrawContext(services, renderContext, game.GraphicsContext);
        spread ??= new ComputeEffectShader(renderContext) { ShaderSourceName = "VoxelWaterSpread" };
        step ??= new ComputeEffectShader(renderContext) { ShaderSourceName = "VoxelWaterStep" };
        compose ??= new ComputeEffectShader(renderContext) { ShaderSourceName = "VoxelWaterCompose" };
        mip ??= new ComputeEffectShader(renderContext) { ShaderSourceName = "VoxelWaterMip" };
        occupancyBase ??= new ComputeEffectShader(renderContext) { ShaderSourceName = "VoxelWaterOccupancyBase" };
        occupancyUp ??= new ComputeEffectShader(renderContext) { ShaderSourceName = "VoxelWaterOccupancyUp" };

        // Which bricks run: those that changed, and their neighbours.
        spread.Parameters.Set(VoxelWaterSpreadKeys.ChangedBricks, changed);
        spread.Parameters.Set(VoxelWaterSpreadKeys.ActiveOut, active);
        spread.Parameters.Set(VoxelWaterSpreadKeys.BrickCount, BrickCount);
        Dispatch(spread, new Int3(4), BrickCount);

        // The water moves, from one texture into the other.
        var next = 1 - current;
        Bricks(step.Parameters);
        step.Parameters.Set(VoxelWaterStepKeys.ChangedOut, changed);
        step.Parameters.Set(VoxelWaterStepKeys.Terrain, Terrain);
        step.Parameters.Set(VoxelWaterStepKeys.Amounts, amounts[current]);
        step.Parameters.Set(VoxelWaterStepKeys.AmountsOut, amounts[next]);
        step.Parameters.Set(VoxelWaterStepKeys.IsoLevel, isoLevel);
        step.Parameters.Set(VoxelWaterStepKeys.MaxCompress, MaxCompress);
        step.Parameters.Set(VoxelWaterStepKeys.MinFlow, MinFlow);
        step.Parameters.Set(VoxelWaterStepKeys.Soak, Soak);
        step.Parameters.Set(VoxelWaterStepKeys.PourCentre, pourCentre);
        step.Parameters.Set(VoxelWaterStepKeys.PourRadius, pourRadius);
        step.Parameters.Set(VoxelWaterStepKeys.PourAmount, pourAmount);
        Dispatch(step, new Int3(BrickSize), SampleCount);
        current = next;
        pourRadius = 0f;

        // The drawn field, finest level, where it ran.
        Bricks(compose.Parameters);
        compose.Parameters.Set(VoxelWaterComposeKeys.Terrain, Terrain);
        compose.Parameters.Set(VoxelWaterComposeKeys.Amounts, amounts[current]);
        compose.Parameters.Set(VoxelWaterComposeKeys.FieldOut, fieldLevels[0]);
        compose.Parameters.Set(VoxelWaterComposeKeys.WaterMaterial, WaterMaterial / 255f);
        Dispatch(compose, new Int3(BrickSize), SampleCount);

        // Its mips, each from the one above, where their sources changed.
        for (int level = 1; level < fieldLevels.Length; level++)
        {
            var sourceSize = new Int3(Texture.CalculateMipSize(Samples, level - 1));
            var targetSize = new Int3(Texture.CalculateMipSize(Samples, level));
            Bricks(mip.Parameters);
            mip.Parameters.Set(VoxelWaterMipKeys.Source, fieldLevels[level - 1]);
            mip.Parameters.Set(VoxelWaterMipKeys.Target, fieldLevels[level]);
            mip.Parameters.Set(VoxelWaterMipKeys.SourceSize, sourceSize);
            mip.Parameters.Set(VoxelWaterMipKeys.TargetSize, targetSize);
            mip.Parameters.Set(VoxelWaterMipKeys.Level, level);
            Dispatch(mip, new Int3(BrickSize), targetSize);
        }

        // The pyramid: the base from the field, each level from the one under it.
        var baseSize = new Int3(Occupancy.Texture.Width);
        Bricks(occupancyBase.Parameters);
        occupancyBase.Parameters.Set(VoxelWaterOccupancyBaseKeys.Field, fieldLevels[0]);
        occupancyBase.Parameters.Set(VoxelWaterOccupancyBaseKeys.Target, occupancyLevels[0]);
        occupancyBase.Parameters.Set(VoxelWaterOccupancyBaseKeys.TargetSize, baseSize);
        Dispatch(occupancyBase, new Int3(4), baseSize);
        for (int level = 1; level < occupancyLevels.Length; level++)
        {
            var sourceSize = new Int3(Texture.CalculateMipSize(Occupancy.Texture.Width, level - 1));
            var targetSize = new Int3(Texture.CalculateMipSize(Occupancy.Texture.Width, level));
            Bricks(occupancyUp.Parameters);
            occupancyUp.Parameters.Set(VoxelWaterOccupancyUpKeys.Source, occupancyLevels[level - 1]);
            occupancyUp.Parameters.Set(VoxelWaterOccupancyUpKeys.Target, occupancyLevels[level]);
            occupancyUp.Parameters.Set(VoxelWaterOccupancyUpKeys.SourceSize, sourceSize);
            occupancyUp.Parameters.Set(VoxelWaterOccupancyUpKeys.TargetSize, targetSize);
            occupancyUp.Parameters.Set(VoxelWaterOccupancyUpKeys.BrickCells, 1 << (level + 1));
            Dispatch(occupancyUp, new Int3(4), targetSize);
        }

        StepCount++;
    }

    private void Bricks(ParameterCollection parameters)
    {
        parameters.Set(VoxelWaterBricksKeys.ActiveBricks, active);
        parameters.Set(VoxelWaterBricksKeys.SampleCount, SampleCount);
        parameters.Set(VoxelWaterBricksKeys.BrickCount, BrickCount);
    }

    private void Dispatch(ComputeEffectShader shader, Int3 threads, Int3 cells)
    {
        shader.ThreadNumbers = threads;
        shader.ThreadGroupCounts = new Int3(
            (cells.X + threads.X - 1) / threads.X,
            (cells.Y + threads.Y - 1) / threads.Y,
            (cells.Z + threads.Z - 1) / threads.Z);
        shader.Draw(drawContext!);
    }

    public void Dispose()
    {
        spread?.Dispose();
        step?.Dispose();
        compose?.Dispose();
        mip?.Dispose();
        occupancyBase?.Dispose();
        occupancyUp?.Dispose();
        foreach (var view in fieldLevels)
            view.Dispose();
        foreach (var view in occupancyLevels)
            view.Dispose();
        Occupancy.Dispose();
        active.Dispose();
        changed.Dispose();
        amounts[0].Dispose();
        amounts[1].Dispose();
        Field.Dispose();
        Terrain.Dispose();
    }
}
