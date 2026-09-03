using System;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;

namespace Demo.Gallery;

/// <summary>
/// The visitor: walks the hall at eye height, looks with the mouse, and reads the plaque of
/// whatever is in front of them.
/// </summary>
/// <remarks>
/// No physics. A gallery has a flat floor and walls that do not move, so the bounds are a box and
/// the exhibits are cylinders - which never gets stuck on a corner, never falls through the floor,
/// and leaves the frame budget to the light, which is what the room is about.
/// </remarks>
public class GalleryPlayer : SyncScript
{
    /// <summary>Eye height above the floor, in metres.</summary>
    public float EyeHeight { get; set; } = 1.68f;

    public float WalkSpeed { get; set; } = 3.2f;
    public float RunSpeed { get; set; } = 6.4f;
    public Vector2 LookSpeed { get; set; } = new Vector2(2.6f, 2.2f);

    /// <summary>Take-off speed, in metres per second, and the gravity that brings you back.</summary>
    public float JumpSpeed { get; set; } = 3.6f;

    public float Gravity { get; set; } = 12f;

    public Keys JumpKey { get; set; } = Keys.Space;

    /// <summary>Switches <see cref="Lantern"/>.</summary>
    public Keys LampKey { get; set; } = Keys.L;

    /// <summary>Switches <see cref="Ghosting"/>.</summary>
    /// <remarks>
    /// Unmodified, like every other key the visitor has, and it is only free because the debug
    /// overlay takes Ctrl+G for the GI switch rather than G. The press is ignored while Ctrl is
    /// down so that toggling the GI does not also send the visitor through a wall.
    /// </remarks>
    public Keys GhostKey { get; set; } = Keys.G;

    /// <summary>Rises and sinks in ghost mode.</summary>
    /// <remarks>
    /// Not Ctrl for the descent, which is the usual pairing: Ctrl is the debug overlay's modifier,
    /// so holding it down to sink turns every letter you then walk on into a GI setting.
    /// </remarks>
    public Keys GhostUpKey { get; set; } = Keys.Space;

    public Keys GhostDownKey { get; set; } = Keys.C;

    /// <summary>How much faster the ghost moves than the visitor walks.</summary>
    /// <remarks>
    /// A ghost is here to get above the cornice and behind the alcoves in a couple of seconds, and
    /// it has no floor to judge its speed against - at walking pace the hall reads as enormous and
    /// crossing it takes fifteen seconds.
    /// </remarks>
    public float GhostSpeedFactor { get; set; } = 2.2f;

    /// <summary>
    /// Off the floor and through the walls: moves along the look direction, ignores
    /// <see cref="Bounds"/>, and drops the eye-height and gravity rules entirely.
    /// </summary>
    /// <remarks>
    /// It is a lighting tool, not a cheat. Every argument this hall makes is about where the bounce
    /// comes from, and half of those places - the inside of a cornice slot, the back of an alcove,
    /// the face of a case from above - cannot be stood in front of. The volume follows the visitor,
    /// so flying the camera into a wall also drags the finest clipmap ring in there with it, which
    /// is the only way to see what the ring actually holds at the boundary it is being blamed for.
    /// </remarks>
    public bool Ghosting { get; private set; }

    /// <summary>
    /// A light the visitor carries: an emissive ball hung in front of them, switched by
    /// <see cref="LampKey"/>. It is not a LightComponent - nothing in this hall is - so it lights
    /// the room the same way every other source does, by being voxelized and traced. Which makes it
    /// the one exhibit you can carry: walk it up to a wall and watch the bounce arrive with you.
    /// </summary>
    public Entity? Lantern { get; set; }

    /// <summary>The hall's inner bounds, so the visitor cannot walk through a wall.</summary>
    public BoundingBox Bounds { get; set; } = new BoundingBox(new Vector3(-100), new Vector3(100));

    private float yaw;
    private float pitch;
    private bool looking = true;
    private float height;
    private float vertical;

    public override void Start()
    {
        var rotation = Entity.Transform.Rotation;
        var forward = Vector3.TransformNormal(-Vector3.UnitZ, Matrix.RotationQuaternion(rotation));
        yaw = MathF.Atan2(-forward.X, -forward.Z);
        pitch = MathF.Asin(MathUtil.Clamp(forward.Y, -1, 1));

        Capture(true);

        if (Lantern is not null)
        {
            LanternRest = Lantern.Transform.Position;
            lanternScale = Lantern.Transform.Scale;
        }

        SetLantern(false);
    }

    /// <summary>Whether the carried lamp is lit.</summary>
    public bool LanternOn { get; private set; }

    public override void Update()
    {
        var dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;

        // Tab hands the mouse back, a click takes it again: a gallery is not a shooter, and the
        // cursor has to be reachable to close the window. Not Escape - that is the shell's way back
        // to the menu, and one key doing both left the scene every time the mouse was wanted.
        if (Input.IsKeyPressed(Keys.Tab))
            Capture(false);
        else if (!looking && Input.IsMouseButtonPressed(MouseButton.Left))
            Capture(true);

        if (looking)
        {
            yaw -= Input.MouseDelta.X * LookSpeed.X;
            pitch = MathUtil.Clamp(pitch - Input.MouseDelta.Y * LookSpeed.Y, -1.4f, 1.4f);
        }

        var orientation = Quaternion.RotationYawPitchRoll(yaw, pitch, 0);
        Entity.Transform.Rotation = orientation;

        if (Input.IsKeyPressed(LampKey))
            SetLantern(!LanternOn);

        if (Input.IsKeyPressed(GhostKey) && !Input.IsKeyDown(Keys.LeftCtrl) && !Input.IsKeyDown(Keys.RightCtrl))
            SetGhosting(!Ghosting);

        if (Ghosting)
        {
            UpdateGhost(dt);
            AnimateLantern((float)Game.UpdateTime.Total.TotalSeconds);
            return;
        }

        // The floor is flat and there is nothing to land on, so "jumping" is one number: how far
        // above eye height you are. It exists to look over the alcove lintels and under the
        // cornice - the two places the bounce comes from that you cannot otherwise see.
        if (height <= 0 && vertical <= 0 && Input.IsKeyPressed(JumpKey))
            vertical = JumpSpeed;

        if (height > 0 || vertical > 0)
        {
            vertical -= Gravity * dt;
            height += vertical * dt;

            if (height <= 0)
                (height, vertical) = (0, 0);
        }

        var move = Vector3.Zero;
        if (Input.IsKeyDown(Keys.W) || Input.IsKeyDown(Keys.Z) || Input.IsKeyDown(Keys.Up)) move.Z -= 1;
        if (Input.IsKeyDown(Keys.S) || Input.IsKeyDown(Keys.Down)) move.Z += 1;
        if (Input.IsKeyDown(Keys.A) || Input.IsKeyDown(Keys.Q) || Input.IsKeyDown(Keys.Left)) move.X -= 1;
        if (Input.IsKeyDown(Keys.D) || Input.IsKeyDown(Keys.Right)) move.X += 1;

        var position = Entity.Transform.Position;

        if (move != Vector3.Zero)
        {
            move.Normalize();

            // Walk on the floor plane: looking up must not lift the visitor off it.
            var heading = Quaternion.RotationY(yaw);
            move = Vector3.Transform(move, heading);
            move *= (Input.IsKeyDown(Keys.LeftShift) ? RunSpeed : WalkSpeed) * dt;

            position += move;
            position.X = MathUtil.Clamp(position.X, Bounds.Minimum.X, Bounds.Maximum.X);
            position.Z = MathUtil.Clamp(position.Z, Bounds.Minimum.Z, Bounds.Maximum.Z);
        }

        position.Y = EyeHeight + height;
        Entity.Transform.Position = position;

        AnimateLantern((float)Game.UpdateTime.Total.TotalSeconds);
    }

    /// <summary>
    /// Flies: the whole orientation drives the movement, so looking down and walking forward
    /// descends, and nothing clamps or falls.
    /// </summary>
    private void UpdateGhost(float dt)
    {
        var move = Vector3.Zero;
        if (Input.IsKeyDown(Keys.W) || Input.IsKeyDown(Keys.Z) || Input.IsKeyDown(Keys.Up)) move.Z -= 1;
        if (Input.IsKeyDown(Keys.S) || Input.IsKeyDown(Keys.Down)) move.Z += 1;
        if (Input.IsKeyDown(Keys.A) || Input.IsKeyDown(Keys.Q) || Input.IsKeyDown(Keys.Left)) move.X -= 1;
        if (Input.IsKeyDown(Keys.D) || Input.IsKeyDown(Keys.Right)) move.X += 1;

        // The rise and the fall stay world-vertical whatever the head is doing: a ghost lining up
        // on a cornice wants to gain height without also drifting the direction it is looking.
        var lift = 0f;
        if (Input.IsKeyDown(GhostUpKey)) lift += 1;
        if (Input.IsKeyDown(GhostDownKey)) lift -= 1;

        if (move == Vector3.Zero && lift == 0)
            return;

        if (move != Vector3.Zero)
        {
            move.Normalize();
            move = Vector3.Transform(move, Entity.Transform.Rotation);
        }

        move.Y += lift;

        var speed = (Input.IsKeyDown(Keys.LeftShift) ? RunSpeed : WalkSpeed) * GhostSpeedFactor;
        Entity.Transform.Position += move * (speed * dt);
    }

    private void SetGhosting(bool on)
    {
        Ghosting = on;

        // Leaving the ghost puts the visitor back on the floor wherever they happen to be, rather
        // than where they took off from: the jump arc is a single number measured from eye height,
        // and coming back at nine metres up would let it run for the whole way down.
        if (on)
            return;

        (height, vertical) = (0, 0);

        var position = Entity.Transform.Position;
        position.X = MathUtil.Clamp(position.X, Bounds.Minimum.X, Bounds.Maximum.X);
        position.Z = MathUtil.Clamp(position.Z, Bounds.Minimum.Z, Bounds.Maximum.Z);
        position.Y = EyeHeight;
        Entity.Transform.Position = position;
    }

    private void SetLantern(bool on)
    {
        LanternOn = on;

        // Switched by hiding the model rather than by repainting it: an unrendered mesh is not
        // voxelized either, so the light goes out at the source instead of leaving a dark ball
        // still emitting into the grid.
        if (Lantern is null)
            return;

        if (Lantern.Get<ModelComponent>() is { } model)
            model.Enabled = on;

        foreach (var child in Lantern.GetChildren())
        {
            if (child.Get<ModelComponent>() is { } part)
                part.Enabled = on;
        }
    }

    /// <summary>
    /// The ball breathes: a slow bob and a slow swell. A source that sits perfectly still in front
    /// of the camera stops reading as a thing you are carrying and starts reading as a smudge on
    /// the lens - and since it is the light as well as the object, the room breathes with it.
    /// </summary>
    private void AnimateLantern(float time)
    {
        if (!LanternOn || Lantern is null)
            return;

        var origin = LanternRest;
        Lantern.Transform.Position = new Vector3(origin.X, origin.Y + MathF.Sin(time * 1.7f) * 0.03f, origin.Z);
        Lantern.Transform.Scale = lanternScale * (1f + MathF.Sin(time * 2.3f) * 0.06f);
    }

    /// <summary>
    /// Where the ball hangs when it is not breathing, and how big it is. Both are read off the
    /// entity at startup: the swell is a factor of the size it was built at, and writing an
    /// absolute scale into a transform whose scale is the ball's diameter inflates it to a metre.
    /// </summary>
    public Vector3 LanternRest { get; set; } = new Vector3(0.42f, -0.34f, -0.8f);

    private Vector3 lanternScale = Vector3.One;

    private void Capture(bool capture)
    {
        looking = capture;
        Input.UnlockMousePosition();

        if (capture)
            Input.LockMousePosition(true);

        Game.IsMouseVisible = !capture;
    }
}
