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
    public Keys ProfilerPageKey { get; set; } = Keys.N;

    /// <summary>Profiler page to open at startup, from --profiler.</summary>
    public VoxelGIProfilerPage Profiler { get; set; } = VoxelGIProfilerPage.Off;

    /// <summary>Where to save an automatic set of shots, from --shot. Null takes none.</summary>
    public string? ShotDirectory { get; set; }

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

        // The traced pass is a compositor object rather than a script, so its key lives here.
        if (running == DemoCatalog.VoxelGrid)
        {
            if (Input.IsKeyPressed(Keys.V))
                VoxelGridDemo.PassEnabled = !VoxelGridDemo.PassEnabled;
            if (Input.IsKeyPressed(Keys.B))
                VoxelGridDemo.Smooth = !VoxelGridDemo.Smooth;
            if (Input.IsKeyPressed(Keys.C))
                VoxelGridDemo.CycleColliderForm();

            // Both on screen at once, because the whole point of being able to change them is to see
            // where the drawn body and the solid one agree and where they do not.
            DebugText.Print($"drawn:    {VoxelGridDemo.SurfaceName}", new Int2(16, 160));
            DebugText.Print($"collider: {VoxelGridDemo.ColliderForm}", new Int2(16, 180));
        }

        // A demo never has to be guessed at: its keys are on screen while it runs.
        var y = 16;
        foreach (var line in DemoCatalog.Entries[running.Value].Controls)
        {
            DebugText.Print(line, new Int2(16, y));
            y += 20;
        }
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

        // The traced grid is a compositor pass rather than an entity, so it is switched here rather
        // than torn down with the scene.
        VoxelGridDemo.PassEnabled = index == DemoCatalog.VoxelGrid;

        pendingDemo = index;
        pendingDelay = 1;
    }

    private void BuildDemo(int index)
    {
        ClearScene();

        DemoCatalog.Entries[index].Build(HostGame, menuCameraEntity);

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
        VoxelGridDemo.PassEnabled = false;

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
        var shooter = new AutoShot { Directory = ShotDirectory!, Prefix = $"demo{index}" };

        if (index == DemoCatalog.VoxelGrid)
        {
            var extent = VoxelGridDemo.Extent;
            var centre = new Vector3(extent * 0.5f, extent * 0.35f, extent * 0.5f);
            shooter.Poses.Add((centre + new Vector3(0, extent * 0.45f, -extent * 0.95f), centre, "front"));
            shooter.Poses.Add((centre + new Vector3(extent * 0.8f, extent * 0.30f, -extent * 0.55f), centre, "corner"));
            shooter.Poses.Add((centre + new Vector3(0, extent * 1.15f, -extent * 0.25f), centre, "above"));
            shooter.Poses.Add((centre + new Vector3(-extent * 0.2f, extent * 0.06f, -extent * 0.42f), centre, "grazing"));
        }
        else
        {
            var from = FindActiveCamera().Transform.Position;
            shooter.Poses.Add((from, from + Vector3.UnitZ, "default"));
        }

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
