using System;
using System.Linq;
using Stride.Profiling;
using StrideVoxelGI;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using Stride.Rendering.Compositing;

namespace Demo.Shell;

/// <summary>
/// The home screen and the switch between demos.
/// </summary>
/// <remarks>
/// <para>
/// One entity survives every switch - this one - and it carries the menu, the camera the menu is
/// seen through, and the input that leaves a demo. Everything else in the root scene belongs to
/// whichever demo is running and is removed when another is chosen.
/// </para>
/// <para>
/// The menu keeps a camera of its own so the compositor's camera slot is never empty, which it would
/// otherwise be for as long as the menu is up. That camera is disabled while a demo runs, so the
/// demo's own camera has the slot to itself.
/// </para>
/// </remarks>
public sealed class DemoShell : SyncScript
{
    /// <summary>Name of the entity carrying the camera the menu is seen through.</summary>
    public const string MenuCameraName = "MenuCamera";

    /// <summary>Returns to the menu from inside a demo.</summary>
    public Keys BackKey { get; set; } = Keys.F1;

    /// <summary>Cycles the profiler: off, frame rate, CPU events, GPU events.</summary>
    /// <remarks>
    /// On the shell rather than in a demo because it is the one question every demo raises - what
    /// is this costing - and because the answer for the voxel passes only exists on the GPU page.
    /// </remarks>
    public Keys ProfilerKey { get; set; } = Keys.F2;

    /// <summary>Next page of profiler results, when they do not fit on one.</summary>
    /// <summary>
    /// Pages through the profiler.
    /// </summary>
    /// <remarks>
    /// Not N. The voxel GI package binds N for its own paging, and two demos run it, so pressing N
    /// there moved both at once.
    /// </remarks>
    public Keys ProfilerPageKey { get; set; } = Keys.F3;

    /// <summary>Profiler page to open at startup, from --profiler.</summary>
    public VoxelGIProfilerPage Profiler { get; set; } = VoxelGIProfilerPage.Off;

    /// <summary>Saves what is on screen, with Ctrl held, in any demo.</summary>
    /// <remarks>
    /// Ctrl+S, the chord the voxel GI overlay taught - but the overlay only exists in the demos
    /// that run it, so the key is the shell's now and the overlay's own copy is switched off
    /// where the scene is built. Under Ctrl so it stays clear of S, which flies the camera back.
    /// </remarks>
    public Keys ScreenshotKey { get; set; } = Keys.S;

    /// <summary>Where <see cref="ScreenshotKey"/> puts its files. Relative paths resolve next to the executable.</summary>
    public string ScreenshotDirectory { get; set; } = "Screenshots";

    /// <summary>What the last screenshot came to, shown for a few seconds under the key list.</summary>
    private string? screenshotStatus;
    private float screenshotStatusLeft;

    /// <summary>Where to save an automatic set of shots, from --shot. Null takes none.</summary>
    public string? ShotDirectory { get; set; }

    /// <summary>Frames between two shots when set; each pose is taken ShotRepeat times, so a
    /// burst of consecutive frames shows what flickers. ShotPose keeps only the pose named.</summary>
    public int ShotInterval { get; set; }
    public int ShotRepeat { get; set; } = 1;
    public string? ShotPose { get; set; }

    /// <summary>
    /// Whether a capture run quits once its last shot is saved, or hands the scene back.
    /// </summary>
    /// <remarks>
    /// Static because the argument is read before the shell exists. Leaving it on screen is how a
    /// capture that shows nothing gets checked against a pair of eyes.
    /// </remarks>
    public static bool ExitAfterShots = true;

    /// <summary>Height of one line of the on-screen key list.</summary>
    private const int LineHeight = 20;

    /// <summary>Where the bottom of the screen is, for a list that grows upwards from it.</summary>
    private int ScreenHeight => GraphicsDevice.Presenter?.BackBuffer?.Height ?? 1080;

    /// <summary>The capture pass, if one was asked for. Rides whichever demo is opened.</summary>
    public CaptureTour? Tour { get; set; }

    private DemoMenu menu = null!;
    private UIComponent ui = null!;
    private Entity menuCameraEntity = null!;
    private CameraComponent menuCamera = null!;

    private int selected;
    private int? running;

    // What to put on screen next frame. The camera processor attaches and detaches cameras while
    // drawing, so removing one camera and adding another within a single update leaves it seeing
    // both at once - and two cameras on one slot is an error, not a warning. Emptying the scene and
    // filling it are therefore a frame apart.
    private int? pendingDemo;
    private bool pendingMenu;

    // Start and the first Update run in the same frame, so a switch asked for from Start would
    // build in the very frame that emptied the scene. One tick of delay makes the gap real.
    private int pendingDelay;

    /// <summary>
    /// Leaves the running demo after this many frames, for a run nobody is sitting at. Zero waits
    /// for the key.
    /// </summary>
    public int AutoBackAfterFrames { get; set; }

    private int framesInDemo;

    private Stride.Rendering.Images.PostProcessingEffects? postEffects;
    private bool defaultLensFlare, defaultLightStreak, defaultLocalReflections;
    private float defaultBloomAmount, defaultBloomRadius;

    /// <summary>The concrete game, which the demo builders and Exit both need.</summary>
    private Game HostGame => (Game)Game;

    /// <summary>Which demo to open immediately, skipping the menu. Null starts on the menu.</summary>
    public int? StartWith { get; set; }

    public override void Start()
    {
        var regular = Content.Load<SpriteFont>("MenuFont");
        var bold = Content.Load<SpriteFont>("MenuFontBold");

        menu = new DemoMenu(regular, bold, DemoCatalog.Entries);
        menu.Activated += Launch;
        menu.Highlighted += index =>
        {
            selected = index;
            menu.Select(index);
        };

        ui = new UIComponent
        {
            Page = menu.Page,
            IsFullScreen = true,
            Resolution = new Vector3(1280, 720, 1000),
            RenderGroup = RenderGroup.Group31,
        };

        // The camera comes with the entity rather than being added here: a frame drawn with no
        // camera at all walks into the post chain without a depth buffer and takes the game down,
        // and Start runs late enough for that to happen.
        // The menu is seen through a camera of its own, on an entity of its own, so that leaving the
        // menu removes it the same way everything else is removed. Sharing the shell's entity meant
        // taking a component off a live entity to free the camera slot, which the engine does not
        // enjoy - two cameras on one slot is an error, and disabling one does not vacate it.
        menuCameraEntity = SceneSystem.SceneInstance.RootScene.Entities.First(entity => entity.Name == MenuCameraName);
        menuCamera = menuCameraEntity.Get<CameraComponent>();
        menuCamera.Slot = SceneSystem.GraphicsCompositor.Cameras[0].ToSlotId();

        Entity.Add(ui);

        // The post chain belongs to the compositor, which outlives every scene: a demo that changes
        // it changes it for whatever runs next. The gallery turns screen space reflections on, and
        // those read a normals buffer that a scene without meshes never produces - so leaving the
        // gallery for the menu used to take the game down. Remembered here, restored on every
        // switch, so a demo can set what it likes and own nothing.
        postEffects = PostEffectsToggle.FindPostEffects(SceneSystem.GraphicsCompositor?.Game);
        if (postEffects is not null)
        {
            defaultLensFlare = postEffects.LensFlare.Enabled;
            defaultLightStreak = postEffects.LightStreak.Enabled;
            defaultLocalReflections = postEffects.LocalReflections.Enabled;
            defaultBloomAmount = postEffects.Bloom.Amount;
            defaultBloomRadius = postEffects.Bloom.Radius;
        }

        ApplyProfiler();

        if (StartWith is { } start)
            Launch(start);
        else
            ShowMenu();
    }


    public override void Update()
    {
        if (pendingDemo is not null || pendingMenu)
        {
            if (pendingDelay > 0)
            {
                pendingDelay--;
                return;
            }


            if (pendingDemo is { } demo)
            {
                pendingDemo = null;
                BuildDemo(demo);
            }
            else
            {
                pendingMenu = false;
                BuildMenu();
            }

            return;
        }

        if (AutoBackAfterFrames > 0 && running is not null && ++framesInDemo == AutoBackAfterFrames)
        {
            ShowMenu();
            return;
        }

        UpdateProfilerInput();

        if (running is null)
        {
            UpdateMenuInput();
            return;
        }

        if (Input.IsKeyPressed(BackKey) || Input.IsKeyPressed(Keys.Escape))
        {
            ShowMenu();
            return;
        }

        if ((Input.IsKeyDown(Keys.LeftCtrl) || Input.IsKeyDown(Keys.RightCtrl)) && Input.IsKeyPressed(ScreenshotKey))
            TakeScreenshot();

        // Along the bottom, not the top, and the whole list: every key a demo answers to is here,
        // in the same place in every demo, with the shell's own tools on the first line.
        //
        // Two of the three demos run the voxel GI package, which draws its own list of settings down
        // the top left corner. Printing here as well put two overlays through each other, both
        // unreadable. The bottom is empty in every scene, and staying out of the way is cheaper than
        // asking each scene where it has room.
        var lines = DemoCatalog.Entries[running.Value].Controls;
        var extra = (running == DemoCatalog.VoxelGrid ? 4 : 0) + (screenshotStatus is null ? 0 : 1);
        var y = ScreenHeight - 16 - (lines.Length + extra) * LineHeight;

        foreach (var line in lines)
        {
            DebugText.Print(line, new Int2(16, y));
            y += LineHeight;
        }

        if (screenshotStatus is not null)
        {
            DebugText.Print(screenshotStatus, new Int2(16, y));
            y += LineHeight;

            screenshotStatusLeft -= (float)Game.UpdateTime.Elapsed.TotalSeconds;
            if (screenshotStatusLeft <= 0)
                screenshotStatus = null;
        }

        // The grid's switches are static, so their keys live here rather than on a script.
        if (running == DemoCatalog.VoxelGrid)
        {
            if (Input.IsKeyPressed(Keys.N))
                VoxelGridDemo.CycleDither();
            if (Input.IsKeyPressed(Keys.B))
                VoxelGridDemo.CycleSurface();
            if (Input.IsKeyPressed(Keys.C))
                VoxelGridDemo.CycleColliderForm();
            if (Input.IsKeyPressed(Keys.X))
                VoxelGridDemo.MatchColliderToSurface();
            if (Input.IsKeyPressed(Keys.O))
                VoxelGridDemo.CastShadows = !VoxelGridDemo.CastShadows;
            if (Input.IsKeyPressed(Keys.G) && !Input.IsKeyDown(Keys.LeftCtrl) && !Input.IsKeyDown(Keys.RightCtrl))
                VoxelGridDemo.ToggleGI();
            if (Input.IsKeyPressed(Keys.L))
                VoxelGridDemo.LightsEnabled = !VoxelGridDemo.LightsEnabled;

            // Both on screen at once, because the whole point of being able to change them is to see
            // where the drawn body and the solid one agree and where they do not.
            DebugText.Print($"drawn    [B] : {VoxelGridDemo.Surface}", new Int2(16, y));
            DebugText.Print($"collider [C] : {VoxelGridDemo.ColliderForm}", new Int2(16, y + LineHeight));
            DebugText.Print($"aim          : {VoxelGridDemo.AimStatus}", new Int2(16, y + LineHeight * 2));
            DebugText.Print($"shadows  [O] : {(VoxelGridDemo.CastShadows ? "cast" : "not cast")}      voxel GI [G] : {(VoxelGridDemo.GIEnabled ? "around the camera" : "off")}      lights [L] : {(VoxelGridDemo.LightsEnabled ? "sun and ambient" : "the arch alone")}      boundary [N] : {VoxelGridDemo.Dither}", new Int2(16, y + LineHeight * 3));
        }

    }

    private void TakeScreenshot()
    {
        try
        {
            var name = DemoCatalog.Entries[running!.Value].Name.Replace(' ', '-').ToLowerInvariant();
            var path = AutoShot.SaveBackBuffer(HostGame, ScreenshotDirectory, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            screenshotStatus = $"saved {path}";
        }
        catch (Exception exception)
        {
            screenshotStatus = $"screenshot failed: {exception.Message}";
        }

        screenshotStatusLeft = 4f;
    }

    private void UpdateProfilerInput()
    {
        if (Input.IsKeyPressed(ProfilerKey))
        {
            Profiler = Profiler == VoxelGIProfilerPage.Gpu ? VoxelGIProfilerPage.Off : Profiler + 1;
            ApplyProfiler();
        }

        if (Profiler != VoxelGIProfilerPage.Off && Input.IsKeyPressed(ProfilerPageKey))
        {
            GameProfiler.CurrentResultPage = Input.IsKeyDown(Keys.LeftShift) || Input.IsKeyDown(Keys.RightShift)
                ? Math.Max(1, GameProfiler.CurrentResultPage - 1)
                : GameProfiler.CurrentResultPage + 1;
        }
    }

    private void ApplyProfiler()
    {
        if (Profiler == VoxelGIProfilerPage.Off)
        {
            GameProfiler.DisableProfiling();
            return;
        }

        GameProfiler.EnableProfiling();
        GameProfiler.CurrentResultPage = 1;
        GameProfiler.FilteringMode = Profiler switch
        {
            VoxelGIProfilerPage.Cpu => GameProfilingResults.CpuEvents,
            VoxelGIProfilerPage.Gpu => GameProfilingResults.GpuEvents,
            _ => GameProfilingResults.Fps,
        };
    }

    private void UpdateMenuInput()
    {
        var count = DemoCatalog.Entries.Count;

        if (Input.IsKeyPressed(Keys.Down) || Input.IsKeyPressed(Keys.S))
            Move(1, count);
        if (Input.IsKeyPressed(Keys.Up) || Input.IsKeyPressed(Keys.W))
            Move(-1, count);

        if (Input.IsKeyPressed(Keys.Enter) || Input.IsKeyPressed(Keys.Space))
            Launch(selected);

        if (Input.IsKeyPressed(Keys.Escape))
            HostGame.Exit();

        for (int i = 0; i < count && i < 9; ++i)
        {
            if (Input.IsKeyPressed(Keys.D1 + i) || Input.IsKeyPressed(Keys.NumPad1 + i))
                Launch(i);
        }
    }

    private void Move(int delta, int count)
    {
        selected = (selected + delta + count) % count;
        menu.Select(selected);
    }

    /// <summary>Empties the scene, and asks for the chosen demo to be built next frame.</summary>
    private void Launch(int index)
    {

        ResetPostChain();

        running = index;
        selected = index;

        ui.Enabled = false;
        HostGame.IsMouseVisible = false;

        pendingDemo = index;
        pendingDelay = 1;
    }

    private void BuildDemo(int index)
    {
        ClearScene();

        DemoCatalog.Entries[index].Build(HostGame, menuCameraEntity);

        // The shell's tools that ride the camera, the same in every demo. The camera is cleared
        // with the rest of the scene on the way out, so they are mounted again with the next one.
        menuCameraEntity.Add(new PostEffectsToggle());

        if (ShotDirectory is not null)
            MountShooter(index);

        // The capture pass rides the demo's own camera, so --capture frames whatever was asked for
        // rather than whichever scene the game happened to start on.
        if (Tour is not null && FindActiveCamera().Get<CaptureTour>() is null)
            FindActiveCamera().Add(Tour);
    }

    /// <summary>Empties the scene, and asks for the menu to come back next frame.</summary>
    private void ShowMenu()
    {
        ResetPostChain();

        // Nothing on the menu is 3D, so screen space reflections have nothing to reflect - and they
        // read a normals buffer that a scene with no meshes never produces.
        if (postEffects is not null)
            postEffects.LocalReflections.Enabled = false;

        running = null;

        pendingMenu = true;
        pendingDelay = 1;
    }

    private void BuildMenu()
    {
        ClearScene();


        ui.Enabled = true;

        HostGame.IsMouseVisible = true;
        Input.UnlockMousePosition();

        menu.Select(selected);
    }

    /// <summary>Adds the automatic capture, with viewpoints that suit the demo being shown.</summary>
    private void MountShooter(int index)
    {
        var shooter = new AutoShot { Directory = ShotDirectory!, Prefix = $"demo{index}", ExitWhenDone = ExitAfterShots };

        if (index == DemoCatalog.VoxelGrid)
        {
            var extent = VoxelGridDemo.Extent;
            var centre = new Vector3(extent * 0.5f, extent * 0.35f, extent * 0.5f);
            shooter.Poses.Add((centre + new Vector3(0, extent * 0.45f, -extent * 0.95f), centre, "front"));
            shooter.Poses.Add((centre + new Vector3(extent * 0.8f, extent * 0.30f, -extent * 0.55f), centre, "corner"));
            shooter.Poses.Add((centre + new Vector3(0, extent * 1.15f, -extent * 0.25f), centre, "above"));
            shooter.Poses.Add((centre + new Vector3(-extent * 0.2f, extent * 0.06f, -extent * 0.42f), centre, "grazing"));
            // From well outside the box, and from far out: a field is walked from where the ray
            // enters it, and both the reach and the float it walks with used to be the camera's.
            shooter.Poses.Add((centre + new Vector3(extent * 1.2f, extent * 0.8f, -extent * 3.0f), centre, "far"));
            shooter.Poses.Add((centre + new Vector3(extent * 3.0f, extent * 2.0f, -extent * 8.0f), centre, "veryfar"));
            // Looking exactly along a grid axis from a cell plane: the centre column's ray then lies
            // on that plane, which a walk once took for its own exit and never left.
            shooter.Poses.Add((new Vector3(extent * 0.5f, extent * 0.75f, -extent * 0.35f), new Vector3(extent * 0.5f, extent * 0.35f, extent * 0.5f), "axis"));

            // Close enough on the sphere that a cell is a good many pixels across. What goes wrong in
            // a traced surface goes wrong at the scale of one cell - a facet on the wrong plane, a
            // normal that flips, a join that steps - and every pose above is too far out to show it.
            var ball = VoxelGridDemo.SphereCentre;
            var standoff = VoxelGridDemo.SphereRadius + 1.6f;
            shooter.Poses.Add((ball + new Vector3(-standoff * 0.45f, standoff * 0.35f, -standoff * 0.82f), ball, "sphere"));
        }
        else
        {
            // Where the demo put its camera, looking where the demo pointed it: the Cornell box is
            // open on one side, and a fixed direction looked out of it at nothing.
            var transform = FindActiveCamera().Transform;
            var from = transform.Position;
            var forward = Vector3.Transform(-Vector3.UnitZ, transform.Rotation);
            shooter.Poses.Add((from, from + forward, "default"));
        }

        if (ShotPose is not null)
            shooter.Poses.RemoveAll(pose => pose.Name != ShotPose);
        if (ShotRepeat > 1)
        {
            var once = shooter.Poses.ToList();
            shooter.Poses.Clear();
            foreach (var pose in once)
                for (int i = 0; i < ShotRepeat; i++)
                    shooter.Poses.Add((pose.From, pose.To, $"{pose.Name}-{i:D2}"));
        }
        if (ShotInterval > 0)
            shooter.FramesBetween = ShotInterval;
        Entity.Add(shooter);
    }

    /// <summary>Puts the compositor's post chain back the way the asset had it.</summary>
    private void ResetPostChain()
    {
        if (postEffects is null)
            return;

        postEffects.LensFlare.Enabled = defaultLensFlare;
        postEffects.LightStreak.Enabled = defaultLightStreak;
        postEffects.LocalReflections.Enabled = defaultLocalReflections;
        postEffects.Bloom.Amount = defaultBloomAmount;
        postEffects.Bloom.Radius = defaultBloomRadius;
    }

    /// <summary>The camera everything is seen through. There is only one.</summary>
    private Entity FindActiveCamera() => menuCameraEntity;

    /// <summary>
    /// Takes the previous demo back out: its entities, the scenes it brought, and whatever it hung
    /// on the camera - but never the camera.
    /// </summary>
    /// <remarks>
    /// The camera stays because it cannot safely be replaced. A graphics compositor slot holds one
    /// camera, and a slot whose camera was removed along with its entity stays claimed by it, so
    /// the next demo's camera has nowhere to attach. One camera, driven by whichever demo is on
    /// screen, has no such transition to get wrong.
    /// </remarks>
    private void ClearScene()
    {
        var scene = SceneSystem.SceneInstance.RootScene;

        foreach (var child in scene.Children.ToList())
            scene.Children.Remove(child);

        foreach (var entity in scene.Entities.Where(entity => entity != Entity && entity != menuCameraEntity).ToList())
            scene.Entities.Remove(entity);

        // Scripts and children a demo hung on the camera: the flight controls, the walker, its
        // readout, the lantern. Left in place they would drive the next demo as well.
        foreach (var component in menuCameraEntity.Components.Where(component => component is not CameraComponent && component is not TransformComponent).ToList())
            menuCameraEntity.Components.Remove(component);

        foreach (var child in menuCameraEntity.Transform.Children.ToList())
            menuCameraEntity.Transform.Children.Remove(child);
    }
}
