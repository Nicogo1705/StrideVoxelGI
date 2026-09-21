using Stride.Core.Mathematics;
using Stride.Graphics;

namespace Stride.Rendering.Voxels.Grid;

/// <summary>
/// Stand-in for the occupancy pyramid of the engine fork the demo runs on, which the published
/// packages do not carry: just enough for Demo/VoxelWater.cs to compile here, never instantiated.
/// </summary>
public sealed class VoxelGridOccupancy : IDisposable
{
    public VoxelGridOccupancy(GraphicsDevice device, Int3 samples, bool unorderedAccess)
    {
        throw new NotSupportedException("A compile-time stand-in only");
    }

    public Texture Texture { get; } = null!;
    public int Levels { get; }

    public void Dispose() { }
}
