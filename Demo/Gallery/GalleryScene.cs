using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Rendering.Colors;
using Stride.Rendering.Lights;
using Stride.Rendering.Voxels;
using Stride.Rendering.Voxels.VoxelGI;
using StrideVoxelGI;

namespace Demo.Gallery;

/// <summary>
/// Assembles the hall, its twenty exhibits, the visitor and the voxel volume that lights it all.
/// </summary>
/// <remarks>
/// The volume is anchored, not attached to the visitor. A room has fixed walls: a volume that
/// follows the camera re-snaps its clipmap rings at every step, and the indirect light visibly
/// swims behind you. Following is for an open world you cannot enclose.
/// </remarks>
public static class GalleryScene
{
    /// <summary>The palette, so an exhibit can repaint itself when the visitor asks.</summary>
    public static GalleryPalette? Palette { get; private set; }

    public static void Build(Game game)
    {
        var scene = game.SceneSystem.SceneInstance?.RootScene;
        if (scene is null)
            return;

        // The Cornell box that ships with the demo is not part of this: clear it out rather than
        // build a gallery inside it.
        scene.Entities.Clear();

        Palette = new GalleryPalette(game.GraphicsDevice);
        var hall = new GalleryHall(game.GraphicsDevice, scene, Palette);

        hall.BuildShell();
        GalleryExhibits.Build(hall);

        var visitor = BuildVisitor(game, scene, hall);
        BuildVolume(scene, visitor);
        BuildFill(scene);
        CalmThePostChain(game);
    }

    /// <summary>
    /// A flat ambient term, off by default. Diagnostic, not lighting: the hall has no analytic
    /// light at all, so every surface in it is shaded from the voxels alone and a metal - which has
    /// no diffuse lobe to fall back on - is black wherever the cone comes back empty. Ambient is
    /// the one light that writes <c>envLightSpecularColor</c> as well as the diffuse one
    /// (LightSimpleAmbient.sdsl sets both), so switching it on puts a floor under the metals
    /// without touching a single voxel. What it lifts was the cone returning nothing; what stays
    /// black after it is something else.
    /// </summary>
    private static void BuildFill(Scene scene)
    {
        if (FillIntensity <= 0f)
            return;

        var entity = new Entity("Fill");
        entity.Add(new LightComponent
        {
            Intensity = FillIntensity,
            Type = new LightAmbient { Color = new ColorRgbProvider(new Color3(1f, 0.94f, 0.85f)) },
        });
        scene.Entities.Add(entity);
    }

    /// <summary>
    /// Strength of the ambient floor. Small on purpose, and no longer only a diagnostic.
    /// </summary>
    /// <remarks>
    /// It arrived to prove that the metals were black because their environment specular term
    /// returned zero rather than because the cones found nothing - ambient is the one light that
    /// writes envLightSpecularColor as well as the diffuse one, so it put a floor under them
    /// without touching a voxel. It earned a second job on the way out: a constant term shrinks the
    /// *relative* size of the GI's temporal wobble, and this volume follows the camera with one
    /// clipmap ring refreshed per frame, so the indirect light does move as you walk.
    /// <para>
    /// The value is the whole argument. At 0.2 the floor is louder than what a mirror reflects and
    /// every metal reads as frosted glass, because a mirror is nothing but its contrast. At 0.05 it
    /// steadies the room and stays under the reflections. It is a stabiliser, not a light: if the
    /// hall needs it to be legible, the bounce is wrong, not this.
    /// </para>
    /// </remarks>
    private const float FillIntensity = 0.05f;

    /// <summary>
    /// Softens the post chain for a room lit by emissive surfaces.
    /// </summary>
    /// <remarks>
    /// Every source here is a bright quad, and the default chain answers a bright quad with a lens
    /// flare and a four-armed streak. Twenty lit cases and eight cornice slots then read as thirty
    /// starbursts hanging in the air, which is all anyone sees - and none of it is the bounce the
    /// hall is here to show. The bloom stays, at a third of its strength: a light source that does
    /// not glow at all reads as painted on.
    /// </remarks>
    private static void CalmThePostChain(Game game)
    {
        if (PostEffectsToggle.FindPostEffects(game.SceneSystem.GraphicsCompositor?.Game) is not { } effects)
            return;

        effects.LensFlare.Enabled = false;
        effects.LightStreak.Enabled = false;
        effects.Bloom.Amount = 0.1f;
        effects.Bloom.Radius = 6f;

        // Screen-space reflections in front of the voxel cone, which is the arrangement every
        // engine that does this well arrives at: the screen trace is sharp and nearly free but
        // knows only what is on screen, the cone knows the whole room but integrates a mip and
        // hands back a smear. One covers the other's hole. Without it a mirror in this hall has
        // nothing but the cone, and reads as black. Turning it on here rather than in the
        // compositor asset leaves the Cornell box - and the screenshots taken of it - alone.
        effects.LocalReflections.Enabled = true;
    }

    /// <summary>The visitor: a camera at eye height, and the two scripts that drive it.</summary>
    private static Entity BuildVisitor(Game game, Scene scene, GalleryHall hall)
    {
        var camera = new CameraComponent
        {
            Projection = CameraProjectionMode.Perspective,
            VerticalFieldOfView = 68f,
            NearClipPlane = 0.08f,
            FarClipPlane = 220f,
            Slot = game.SceneSystem.GraphicsCompositor.Cameras[0].ToSlotId(),
        };

        var visitor = new Entity("Visitor") { camera };
        visitor.Transform.Position = new Vector3(0, 1.68f, GalleryHall.HalfLength - 3f);
        visitor.Transform.Rotation = Quaternion.RotationY(MathUtil.Pi);

        // The lamp the visitor carries: a ball of light with nothing around it, hung off the camera
        // so it travels with them. Hidden until L, and hidden means not voxelized.
        var lantern = hall.Ball("Lantern", new Vector3(0.42f, -0.34f, -0.8f), 0.2f,
                                Palette!.Emissive(new Color3(0.62f, 0.86f, 1f), 14f), visitor);

        visitor.Add(new GalleryPlayer
        {
            Bounds = new BoundingBox(
                new Vector3(-GalleryHall.HalfWidth + 0.6f, 0, -GalleryHall.HalfLength + 1.2f),
                new Vector3(GalleryHall.HalfWidth - 0.6f, 0, GalleryHall.HalfLength - 1.2f)),
            Lantern = lantern,
        });

        visitor.Add(new GalleryHud());

        // No PostEffectsToggle here. It exists to bisect a screen artefact in the Cornell box by
        // switching the bloom off while the artefact is on screen; in the hall its readout lands on
        // top of the volume line and its key is one more thing to explain, for a switch a visitor
        // has no use for. The gallery's post chain is set once, in CalmThePostChain.

        scene.Entities.Add(visitor);
        return visitor;
    }

    /// <summary>
    /// The volume that lights the hall, centred on the visitor.
    /// </summary>
    /// <remarks>
    /// The rings each cover twice the last, so only the finest one - a sixteenth of the volume, six
    /// metres across - holds voxels small enough to draw a shadow edge or a reflection. Anchored at
    /// the middle of the hall, that ring sat in the empty aisle: every alcove was lit out of the
    /// fourth ring, at voxels a third of a metre wide, which is why shadows came out square, why
    /// the colour bleed needed the bounce turned up to see at all, and why the polished balls read
    /// black - a reflection ray that starts inside a voxel wall returns nothing.
    /// <para>
    /// So it follows the visitor. The finest ring is then always around whatever they are looking
    /// at. A following volume re-snaps its rings as you walk and the indirect light swims a little
    /// behind you; in a hall you walk through, that costs less than lighting the exhibits out of
    /// the coarse rings. K anchors it again.
    /// </para>
    /// </remarks>
    private static void BuildVolume(Scene scene, Entity visitor)
    {
        var entity = new Entity("Voxel GI");
        entity.Transform.Position = new Vector3(0, GalleryHall.Height / 2, 0);

        entity.Add(new VoxelGIVolume
        {
            // 144, which is far more than the hall needs to contain - it is 44 by 20 by 7, and
            // the sky slab sits five units past the far wall. The extra is not there to hold
            // anything; it is there because the finest clipmap ring is always exactly `resolution`
            // voxels across, so the only way to widen it is to widen the volume. At 144 over three
            // levels that ring is 36 units and covers nearly the whole nave at once, which is what
            // lets the volume follow the visitor without the light swimming underfoot: a ring that
            // spans the room has almost nothing left to shift when it re-snaps.
            //
            // The voxel pays for it, at 14cm. That is the trade this scene settled on after trying
            // both ends of it, and it is the same answer the room gave every time it was asked:
            // consistent and coarse beats fine and seamed, because what the eye catches is the
            // discontinuity and not the resolution.
            VolumeSize = 144f,
            // Four rings and 256^3, and the two go together - one without the other is a waste.
            //
            // The finest ring is always exactly `resolution` voxels across: its extent is
            // VolumeSize/2^(levels-1) and the voxel is that over the resolution, so their ratio is
            // fixed and every other setting only slides along one line. At 128^3 that line offers
            // six metres of sharp reflection at 4.7cm voxels, or twenty-four at 18.8cm, and never
            // both. Doubling the resolution is the one move that shifts the line; dropping a ring
            // then spends the gain on extent rather than on finer voxels, which is the half worth
            // having here - twelve metres of finest ring at the same 4.7cm the grille of exhibit 9
            // and the shutter of 13 were sized against.
            //
            // It is not free: rings stack along X and directions along Y in one atlas, so this is
            // roughly six times the texels of the 128^3 setup and eight times the voxels to fill
            // each frame. A machine that cannot afford it drops to Ultra with Ctrl+Q and loses the
            // ring extent, not the voxel size.
            ClipMapLevels = 3,
            Quality = VoxelGIQuality.UltraPlus,

            // Diffuse and specular at the same strength, which is what a room lit only by its own
            // surfaces wants: there is no analytic light here to carry the highlights, so the two
            // halves of the voxel data are the whole of the lighting and pulling them apart just
            // makes one of them lie.
            // Two and one, and the ratio matters more than either number. Both read the same voxel
            // data - the diffuse cones for what a surface receives, the specular cone for what a
            // mirror shows - so a room whose walls are multiplied by four while its mirrors are
            // multiplied by a quarter shows those walls sixteen times darker in a reflection than
            // in front of you. Within a factor of two they agree, and a polished sphere finally
            // returns the room at something close to the room's own brightness.
            BounceIntensity = 2f,
            SpecularIntensity = 1f,

            // PhysicallyBased rather than the Heuristic default. Merging eight voxels into one asks
            // by how much to divide their radiance: opacity is a volume and divides by eight, but
            // radiance is read off a surface, and a surface is a 2D projection, so it should fall by
            // four. Heuristic divides by the number of filled sub-voxels with a floor of four, which
            // matches that on an isolated surface but drops back to eight wherever all eight are
            // filled - inside thick walls and large solids, which is exactly where the light was
            // seen draining away one mip at a time. Dividing by four everywhere keeps it, and is
            // why the bounce above could come down from four to two.
            LightFalloff = VoxelAttributeEmissionOpacity.LightFalloffs.PhysicallyBased,

            // The loop gain, and the reason this hall used to go red before it went white. The
            // voxelization pass shades each surface with the previous frame's indirect light and
            // writes it back, so the room feeds itself and the round trip is multiplied by roughly
            // BounceIntensity x SecondBounce x albedo. Left at its default of 1, that product is
            // over two here and the room diverges - in the colour of its largest surface, which is
            // the red runner. 0.4 puts it under one: two or three useful bounces, then extinction.
            SecondBounce = 1f,

            // Thin geometry read as half-transparent and leaked: the grille of exhibit 9 is four
            // voxels thick and the shutter of 13 barely five, and both need their coverage taken
            // seriously to occlude at all.
            // Down from 4. It multiplies the alpha of every partially covered voxel - which is
            // every cell a surface passes through, since walls and floors are shells - so raising
            // it does not just thicken thin geometry, it makes the whole room harder for light to
            // cross, and the bounce dies. It was at 4 to force the grille of exhibit 9 and the
            // shutter of 13 to occlude; both have since been resized to four or five voxels and no
            // longer need the crutch.
            Opacify = 1f,

            // A tight aperture, deliberately. It is not the physical answer - the cone should open
            // by roughly the roughness, and at 0.1 a 0.9-rough plaster wall gets a 5-degree cone
            // and hands back a legible rectangle of the lamp instead of a broad sheen. It is the
            // answer that makes the polished pieces read as polished, which is what the hall is
            // for. The plaster paying for it is a known and accepted cost.
            SpecularConeRatio = 0.1f,

            // Range is what a tight cone needs, and it is cheap: steps are a loop counter, not an
            // allocation, so 512 costs nothing in memory and little on the GPU - the early-out at
            // 0.99 alpha retires any ray that meets a surface within a few of them. What it does
            // buy is reach for the sharp materials, which are the short-sighted ones here: at
            // roughness 0.02 the diameter never leaves one voxel, the step stays at 2.3cm, and 256
            // steps see six metres. It is paid for once, in shader compilation.
            SpecularSteps = 576,

            // And a horizon, because reach without one is how a reflection ends up leaving the
            // building and bringing back a picture of it.
            SpecularRange = 24f,

            SpecularRoughnessCutoff = 1.0f,
            AutoFreeze = false,
            Follow = visitor.Transform,

            // Following the visitor after all, which is only affordable because the finest ring is
            // 24 units wide at three levels: it covers most of the nave at once, so the per-ring
            // snapping that made the light swim underfoot has little left to shift. At four or five
            // levels that ring is six or twelve units, it moves under you constantly, and anchoring
            // is the only way to hold it still.
            //
            // Kept for reference, since it is the trade the setting hides:
            //
            // A volume that moves re-snaps every ring to its own grid as it goes, and in the default
            // single-ring update mode only one of them is refreshed per frame, so the room is lit by
            // several rings each anchored to a different past camera position and each jumping by a
            // different amount. That reads as light swimming underfoot while you walk, and no amount
            // of tuning removes it, because it is the volume moving rather than the lighting being
            // wrong. Following the camera exists for open worlds that cannot fit in a volume. This
            // hall is 44 by 20 by 7 inside a volume of 96: it fits entirely, so it never needed to
            // move. What it costs is that the finest ring now sits at the middle of the nave rather
            // than under the visitor, so the two ends are lit from coarser rings.
        });

        entity.Add(new VoxelGIDebug
        {
            OverlayPosition = new Int2(16, 16),
            RequireControl = true,
            FollowCandidate = visitor.Transform,
        });

        scene.Entities.Add(entity);
    }
}
