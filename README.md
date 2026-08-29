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
| `Ctrl`+`S` | Save a PNG of the frame to `Screenshots/`. |
| right-drag, `WASD`/`ZQSD` | Fly the camera. The keyboard only moves it while the right button is held, so the letter keys above stay free the rest of the time. `C` and `E`/`Space` go down and up, `Shift` goes faster. |

## The knobs that matter

- **`VolumeSize`** — the voxelized cube is centred on the entity and is *all* the GI knows about.
  Geometry outside it does not bounce light. Parent the entity to your camera or player for an
  open world; leave it fixed for a room.
- **`Quality`** — picks clipmap resolution (64³ → 256³), voxel layout and cone count together.
  `VoxelSize` (shown in the debug overlay) is `VolumeSize / resolution`: a 26-unit volume at
  Medium gives ~20 cm voxels. That number, not the preset name, is what decides whether a
  doorframe survives voxelization.
- **`BounceIntensity`** — Stride's own `LightVoxel.BounceIntensityScale` defaults to **0**, which
  is why a hand-built voxel GI setup renders exactly nothing. Here it defaults to 1.
- **Anisotropic layout** (High and above) — stores six directional values per voxel instead of
  one. Six times the memory, but a surface stops receiving light that reached the voxel from
  behind it, which is what most "light leaks through my wall" reports actually are.
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

`Demo/` is a Cornell box: red wall, green wall, two boxes, a chrome sphere and a spotlight
pointing at an emissive ceiling panel. With GI off it is flat and the shadows are black. With GI
on, the red and green walls bleed onto the white boxes, the ceiling lights up from bounce alone,
and the sphere reflects the room through the specular cone.

```
dotnet run --project Demo
```

## Credits

The voxel cone tracing implementation is Stride's own `Stride.Voxels`, written by
**Sean Boettger** and distributed with the engine under the MIT license. This asset configures it;
it does not reimplement it.

## License

MIT — see [LICENSE.md](LICENSE.md).
