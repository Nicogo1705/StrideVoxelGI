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

        // Escape hands the mouse back, a click takes it again: a gallery is not a shooter, and the
        // cursor has to be reachable to close the window.
        if (Input.IsKeyPressed(Keys.Escape))
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
