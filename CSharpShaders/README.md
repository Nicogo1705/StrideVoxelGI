# C#SL: Stride shaders from C#

Tooling for writing and driving Stride compute shaders from C# without GPU plumbing. Two bricks:

1. **Typed wrappers** (`Csl.Generators` + `Csl.Runtime`): every compute shader of the project gets a
   `<Shader>Effect` class with a property per parameter, resource slots that say what to allocate,
   and a `Dispatch(cells)` that computes the thread groups. Generated at build time from the
   `.sdsl` files, so nothing to run and nothing to commit.
2. **C# to SDSL** (phase 2, coming): a `partial class` marked `[Shader]` becomes an `.sdsl` shader.

Built against Stride 4.4 (the `StrideVersion` in `Directory.Build.props`). The engine already
declares `**/*.sdsl` as `AdditionalFiles` and generates the `*Keys` classes from them with its own
Roslyn generator; `Csl.Generators` reads the same files and builds on those keys.

## Projects

| Project | Target | Role |
|---------|--------|------|
| `Csl.Generators` | netstandard2.0 | Roslyn source generator (`ShaderEffectGenerator`) and analyzer (`TypedUavFormatAnalyzer`). Has its own SDSL declaration parser: no engine dependency. |
| `Csl.Runtime` | net10.0 | `ComputeEffect` base class, `ShaderContext`, `ResourceSlot`, `Textures`/`Buffers` allocation, `MipChain`, `PingPong<T>`, region uploads. |
| `Csl.Tests` | net10.0, xunit | Parser, generator and analyzer tests, without a GPU. Also compiles the demo's shaders and `Demo/VoxelWater.cs` through the generator against the engine. |

Reference both from the project that owns the shaders:

```xml
<ProjectReference Include="..\CSharpShaders\Csl.Runtime\Csl.Runtime.csproj" />
<ProjectReference Include="..\CSharpShaders\Csl.Generators\Csl.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## The wrappers

For `shader VoxelWaterStep : ComputeShaderBase, VoxelWaterBricks` in namespace `Demo`, the generator
emits `Demo.VoxelWaterStepEffect`:

- **Base class**: the wrapper of the first base declared in the project (`VoxelWaterBricksEffect`,
  abstract, carrying `ActiveBricks`, `SampleCount`, `BrickCount` once for every pass that mixes it
  in), or `Csl.ComputeEffect` when there is none. Other project mixins are flattened into the class.
  Engine shaders (`ComputeShaderBase`) are not wrapped; a shader is "compute" when its inheritance
  reaches `ComputeShaderBase`.
- **Constructor**: `new VoxelWaterStepEffect(services, threadNumbers)` with an `Int3` or a single
  `int` for all axes. The `.sdsl` does not know its thread numbers (they are macros the effect sets),
  so they are given here; changing `ThreadNumbers` later recompiles the effect on the next dispatch.
- **One property per parameter**: every member that is not `stream`, `compose`, `const`, `static` or
  `groupshared`, typed as the engine's key is (`float`, `Int3`, `Vector3`, `Texture?`, `Buffer?`,
  `SamplerState?`). Setting one sets the key. The `///` comment of the member is the property's doc.
- **`Slots`**: a nested static class with one `ResourceSlot` per resource: its HLSL type, its element
  type and the access the shader makes of it, found by reading the method bodies (`Load`/`Sample`/
  `Res[i]` read, `Res[i] = ` write, `Interlocked*` both). `Texture3D<T>` is always a read slot;
  `RWTexture3D<T>` a write slot, or read-write when the body reads it back.
- **`Dispatch(Int3 cells)`**: thread group counts are `ceil(cells / threadNumbers)` per axis;
  `DispatchGroups(groups)` takes the counts directly. `Parameters` and `Shader` expose the engine
  objects underneath.
- **`IDisposable`**: disposes the `ComputeEffectShader`.

`ShaderContext.Get(services)` holds the `RenderContext`, one `RenderDrawContext` and the command
list, made once per game and registered as a service; every effect shares it.

### Allocation helpers

```csharp
// Format from the element type, views from the slots: R16_Float, SRV|UAV.
var amounts = Textures.New3D<Half>(device, size, VoxelWaterStepEffect.Slots.Amounts, VoxelWaterStepEffect.Slots.AmountsOut);
// Whole mip chain, R8G8_UNorm.
var field = Textures.New3D<Texels.Rg8>(device, size, MipMapCount.Auto, VoxelWaterComposeEffect.Slots.FieldOut, VoxelWaterMipEffect.Slots.Source);
var levels = field.MipViews();          // MipChain: one single-mip view per level, levels[i], levels.SizeAt(i)
var pair = new PingPong<Texture>(() => Textures.New3D<Half>(device, size, slots)); // pair.Current, pair.Next, pair.Swap()
terrain.UploadRegion(commandList, MemoryMarshal.Cast<byte, Texels.Rg8>(texels), size, lo, hi); // a box of a whole-texture array
changed.FillRegion(commandList, 1u, lo, hi);                                                   // one value over a box
```

Element types: `float`, `Half`, `uint`, `int`, `byte`, `ushort`, `Vector2/4`, `Half2/4`, `Int2/4`,
`Color`, and `Csl.Texels.Rg8/R8/R16` for the unorm formats without a struct of their own.
`Buffers.NewTyped<T>`, `NewStructured<T>` and `NewRaw` do the same for buffers.

### Diagnostics

| Id | Where | Says |
|----|-------|------|
| CSL001 | `.sdsl` | A declaration the parser did not understand; the wrapper may be incomplete. The engine's own parser still validates the shader. |
| CSL002 | `.sdsl` | A parameter whose type has no C# key type (a struct, `float3x3`): use `Parameters`. |
| CSL003 | `.sdsl` | An array parameter, not wrapped: use `Parameters`. |
| CSL004 | `.sdsl` | The same shader name in two files; only the first is wrapped. |
| CSL010 | C# | `Textures.New3D<T>(…, slot)` with a slot the shader both reads and writes through its RW view and a `T` other than `float`, `uint`, `int`: Direct3D 11 only allows a typed UAV load on R32 single-channel formats. The same check runs when a texture is bound to such a slot, whatever it was allocated with. |

Binding a texture that lacks a view the slot needs (no UAV on a `RW` slot) throws with the slot's
name; so does allocating with the wrong element type on a read-write slot.

## Before and after: `Demo/VoxelWater.cs`

Before, each pass was a `ComputeEffectShader` created lazily, its parameters set by key on every
step, the thread group counts computed by a local `Dispatch`, and each texture created with its
format and flags spelled out:

```csharp
step ??= new ComputeEffectShader(renderContext) { ShaderSourceName = "VoxelWaterStep" };
...
Bricks(step.Parameters, active);
step.Parameters.Set(VoxelWaterStepKeys.ChangedOut, changed);
step.Parameters.Set(VoxelWaterStepKeys.Terrain, Terrain);
step.Parameters.Set(VoxelWaterStepKeys.Amounts, amounts[current]);
step.Parameters.Set(VoxelWaterStepKeys.AmountsOut, amounts[next]);
step.Parameters.Set(VoxelWaterStepKeys.IsoLevel, isoLevel);
... seven more ...
Dispatch(step, new Int3(BrickSize), SampleCount);
current = next;
```

After, the constants are set once at construction and a step reads as the algorithm:

```csharp
step = new VoxelWaterStepEffect(services, BrickSize) { ActiveBricks = active, ChangedOut = changed, Terrain = Terrain, IsoLevel = isoLevel };
...
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
```

The orchestration (sub-steps, the ping-pong, the mip loop, the pyramid, the brick marking) is
unchanged and still explicit. The texture creation, the mip views, the region upload, the
`ParameterCollection` calls, the group count arithmetic and the effect lifetime are gone: 326 lines
to 279, most of the difference in the constructor and `Step`.

## Limits

- The declaration parser covers what the wrappers need (namespaces, shaders, bases, members,
  attributes, cbuffers, method bodies as tokens). Generic shaders get no wrapper; effects (`.sdfx`)
  and structs are skipped.
- Resource usage is syntactic: a resource passed to a function is counted as read; a resource used
  only inside a mixin's methods is classified by that mixin. Neither can produce a false "read-only".
- Thread numbers are not in the `.sdsl`; they are given at construction.
- Only cubic mip sizes were exercised (the demo's textures); `MipChain.SizeAt` computes each axis.
- Building the demo needs the engine fork it targets (`Stride.Voxels` with `VoxelGridOccupancy`);
  the published 4.4 packages build `Csl.*` and the tests, not the demo.
