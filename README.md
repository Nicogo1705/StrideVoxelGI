# Stride Voxel GI (VXGI) Starter

[![Available on the Community Stride Asset Store](https://img.shields.io/badge/Community_Stride_Asset_Store-install-5b8def)](https://nicogo1705.github.io/StrideAssetStore/a/com.nicogo.voxel-gi)

Real-time global illumination for [Stride](https://www.stride3d.net/), in one component.

Stride already contains a full voxel cone tracing implementation (`Stride.Voxels`, by Sean
Boettger) — it just ships switched off, undocumented, and spread over about a dozen types you have
to assemble in the right order before a single photon bounces. This asset is that assembly: a
`VoxelGIVolume` component, four quality presets, a ready-made graphics compositor, and a Cornell
box demo where you can toggle the indirect light on and off with one key.

## What's in the box

| File | Role |
|------|------|
| `VoxelGIVolume` | The component. Drop it on an entity: it builds the `VoxelVolumeComponent` that voxelizes the world around it and the `LightVoxel` environment light that cone-traces it back. Every knob you actually tune is on it. |
| `VoxelGIPreset` / `VoxelGIQuality` | Low / Medium / High / Ultra, each a coherent set of clipmap resolution, voxel layout and cone count. Also the place to hand-roll a tier of your own. |
| `VoxelGIDebug` | Hotkeys and an on-screen readout: toggle GI, ray-march the voxels, freeze voxelization, cycle quality, push the bounce. Pure debug — delete it and nothing changes. |
| `Demo/Assets/GraphicsCompositor.sdgfxcomp` | A flattened, self-contained voxel compositor. This is the part people get stuck on. |

## Quick start

1. Reference `StrideVoxelGI` from your game project (it pulls `Stride.Voxels` in for you).
2. **Use a voxel-capable graphics compositor.** Copy `Demo/Assets/GraphicsCompositor.sdgfxcomp`
   into your `Assets/` folder and select it in *Game Settings*, or build your own — see below.
3. Add an empty entity in the middle of the area you want lit, attach a **Voxel GI Volume**
   component (category *Lights*), set `VolumeSize` to cover it.
4. Press play. Add a **Voxel GI Debug** next to it and press `G` to see the before/after.

```csharp
// Or entirely in code — parent it to the player for a volume that follows them.
var gi = new Entity("Voxel GI");
gi.Add(new VoxelGIVolume
{
    VolumeSize = 26f,        // edge of the voxelized cube, in world units
    Quality = VoxelGIQuality.Medium,
    BounceIntensity = 1.25f, // one bounce loses energy; artists push past 1
});
Entity.Scene.Entities.Add(gi);
```

## Hotkeys in the demo

| Key | Does |
|-----|------|
| `G` | Toggle the indirect light. This is the whole pitch in one key. |
| `V` | Cycle the voxel views: off → ray-marched voxels → raw storage slice. |
| `F` | Freeze voxelization (keeps lighting from the last capture — the right setting for static geometry). |
| `Q` | Cycle Low / Medium / High / Ultra. |
| `O` | Cycle the voxelization thickening: 0, 1, 2, 4. |
| `PgDn` `PgUp` | Step the mip level shown by the raw view. |
| numpad `-` `+` | Lower / raise the bounce intensity. |
| `R` | Cycle the GI resolution: 1/1, 1/2, 1/4 of the screen. |
| `P` | Cycle Stride's profiler: off / FPS / CPU events / GPU events. The GPU page is what the voxel passes actually cost. |
| `N` | Next profiler result page (`Shift`+`N` goes back to the first). |
| `Ctrl`+`S` | Save a PNG of the frame to `Screenshots/`. |
| right-drag, `WASD`/`ZQSD` | Fly the camera. The keyboard only moves it while the right button is held, so the letter keys above stay free the rest of the time. `C` and `E`/`Space` go down and up, `Shift` goes faster. |

## The knobs that matter

- **`VolumeSize`** — the voxelized cube is centred on the entity and is *all* the GI knows about.
  Geometry outside it does not bounce light. Parent the entity to your camera or player for an
  open world; leave it fixed for a room.
- **`Quality`** — picks clipmap resolution, voxel layout and cone count together. `VoxelSize`
  (shown in the debug overlay) is `VolumeSize / 2^(ClipMapLevels-1) / resolution` — the rings
  matter as much as the resolution. That number, not the preset name, is what decides whether a
  doorframe survives voxelization, so read it off the overlay rather than assuming.
- **`ClipMapLevels`** — nested detail rings, each covering twice the distance of the last. This is
  the cheap way to finer voxels: memory grows linearly with rings and cubically with resolution,
  so eight rings at 128³ resolve eight times finer than five for a fraction of what 256³ costs.
  The ceiling is `MaxClipMapLevels` (twelve at 128³, eight at 256³), set by how much of a 3D
  texture Direct3D11 will allocate.
- **`GIResolutionDivisor`** — trace the diffuse cones into a buffer 1/N of the screen and read it
  back when shading, instead of tracing per shaded pixel. 2 costs a quarter of the cones, 4 a
  sixteenth, for softer silhouettes. Needs a depth-only stage on the compositor; without one the
  light quietly keeps marching inline.
- **`BounceIntensity`** — Stride's own `LightVoxel.BounceIntensityScale` defaults to **0**, which
  is why a hand-built voxel GI setup renders exactly nothing. Here it defaults to 1.
- **Directional storage** (`Directionality`, paired on High and above) — stores three directional
  values per voxel instead of one, the two facings of each axis packed together. Three times the
  memory, but a surface stops receiving light that reached the voxel from behind it, which is what
  most "light leaks through my wall" reports actually are. Full six-way anisotropy is there too,
  at twice the cost of paired for a difference you have to look for.
- **`Voxelize`** — turn it off for static levels. The cones keep tracing the last capture and the
  voxelization pass costs nothing.

## Building your own compositor

If you'd rather not copy the one from the demo, the recipe in Game Studio is:

1. Add two render stages, `VoxelizationPassFirst` (effect slot `Voxelizer`) and
   `VoxelizationPassSecond` (slot `Voxelizer2`).
2. On the `MeshRenderFeature`, add two `MeshTransparentRenderStageSelector`s pointing at them with
   effect `StrideForwardShadingEffectVXGI.VoxelizeToFragmentsEffect`, a `VoxelPipelineProcessor`
   listing both stages, and a `VoxelRenderFeature`.
3. Add a `LightVoxelRenderer` to the `ForwardLightingRenderFeature`'s light renderers.
4. Replace the `ForwardRenderer` with a `ForwardRendererVoxels`, and give its `VoxelRenderer` the
   two voxelization stages.

`Stride.Voxels` also ships a `DefaultGraphicsCompositorVoxels` you can select directly from the
package — the demo carries its own flattened copy so the asset works even if that ever moves.

## Requirements & limits

- Stride **4.4**, and a **Feature Level 11** GPU — voxelization throws below it. The engine-side
  fixes `Stride.Voxels` needs on 4.4 landed after `4.4.0-beta5`, so build against a newer engine.
- Voxelization re-renders the scene into the voxel grid: cost scales with triangles *and* with
  volume resolution. Only one clipmap is refreshed per frame by default, which is why a fast-moving
  volume trails slightly behind.
- Emissive materials are voxelized as emitted radiance, so an emissive panel lights the room
  through the GI without being a light at all — the demo's ceiling panel does exactly that.
- One bounce. Voxel cone tracing is not path tracing; it is an approximation that looks right and
  runs in a frame.

## The demo

`Demo/` is a Cornell box: red wall, green wall, two boxes, a chrome sphere, three glass spheres
and a spotlight pointing at an emissive ceiling panel. With GI off it is flat and the shadows are
black. With GI on, the red and green walls bleed onto the white boxes, the ceiling lights up from
bounce alone, and the spheres pick the room up through the specular cone.

```
dotnet run --project Demo
```

It also runs itself, which is how the screenshots and the timings in this repo were made:
synthesized key presses do not reach Stride's input, so anything driving the demo from outside
needs a way in that is not the keyboard.

```
Demo.exe --capture --profiler=gpu --quality=ultra
```

| Option | Does |
|--------|------|
| `--capture` | Walk the camera through five viewpoints, save a PNG at each, then exit. |
| `--profiler=gpu\|cpu\|fps` | Open Stride's profiler at startup, so the shots carry the timings. |
| `--quality=low\|medium\|high\|ultra`, `--divisor=N`, `--levels=N`, `--volume=N` | Override the volume's settings before capturing. |
| `--quality-cycle` | Hold the camera still and switch tier at each stop, to compare presets. |
| `--dump-gi` | Also save what the reduced-resolution GI pass wrote — the first place to look when the image has artefacts the full-rate path does not. |
| `--no-gi`, `--view=cones\|raw` | Capture with the bounce off, or through a voxel debug view. |
| `--rdc` | Ask RenderDoc to capture the frame each stop is shot on, when launched through `renderdoccmd`. |
| `--shots=N`, `--settle=N`, `--warmup=N`, `--out=DIR`, `--pivot=N` | Shape the run: stops, hold per stop, seconds to let the engine compile before the first shot, where the PNGs go. |

## Credits

The voxel cone tracing implementation is Stride's own `Stride.Voxels`, written by
**Sean Boettger** and distributed with the engine under the MIT license. This asset configures it;
it does not reimplement it.

## License

MIT — see [LICENSE.md](LICENSE.md).
