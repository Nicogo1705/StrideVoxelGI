// Stub of the engine's ComputeShaderBase.sdsl (Stride.Rendering, stride/Assets/ComputeEffect):
// what a compute shader written in C# inherits. Nothing is generated from it; the generator
// only reads its members to know the streams and the methods a derived shader may override.
using Csl.Hlsl;

namespace Csl.Engine;

/// <summary>Base compute shader. Override <see cref="Compute"/>; the engine's CSMain calls it once per thread.</summary>
[Shader(External = true)]
public abstract partial class ComputeShaderBase
{
    [Stage, Stream("SV_GroupID")] public uint3 GroupId;
    [Stage, Stream("SV_DispatchThreadID")] public uint3 DispatchThreadId;
    [Stage, Stream("SV_GroupThreadID")] public uint3 GroupThreadId;
    [Stage, Stream("SV_GroupIndex")] public uint GroupIndex;

    [Stage, Stream] public uint3 ThreadGroupCount;
    [Stage, Stream] public uint ThreadCountPerGroup;
    [Stage, Stream] public uint ThreadGroupIndex;

    [Stage, Stream] public int ThreadCountX;
    [Stage, Stream] public int ThreadCountY;
    [Stage, Stream] public int ThreadCountZ;

    /// <summary>The ThreadGroupCount of the dispatch, set by ComputeEffectShader.</summary>
    [Stage, Link("ComputeShaderBase.ThreadGroupCountGlobal")] public int3 ThreadGroupCountGlobal;

    /// <summary>Called once per thread by the entry point.</summary>
    public virtual void Compute() { }

    public virtual bool IsFirstThreadOfGroup() => GroupThreadId.x == 0 && GroupThreadId.y == 0 && GroupThreadId.z == 0;
}
