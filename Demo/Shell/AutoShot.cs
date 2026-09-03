using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;

namespace Demo.Shell;

/// <summary>
/// Puts the camera through a few poses, saves a PNG at each, and quits.
/// </summary>
/// <remarks>
/// So that a change to a shader can be looked at without anyone having to be at the machine. The
/// poses are chosen to show a surface from several angles at once, because most of what goes wrong
/// in a traced surface - a normal pointing the wrong way, a silhouette that steps, a hole that only
/// opens from one side - is invisible from exactly one viewpoint.
/// </remarks>
public sealed class AutoShot : SyncScript
{
    /// <summary>Where the PNGs go. Relative paths resolve next to the executable.</summary>
    public string Directory { get; set; } = "Shots";

    /// <summary>Frames to let the game load, compile its shaders and settle before the first shot.</summary>
    public int WarmupFrames { get; set; } = 110;

    /// <summary>
    /// Frames a pose is held before it is read back.
    /// </summary>
    /// <remarks>
    /// A whole second, not a handful of frames. Moving the camera and reading the back buffer almost
    /// immediately catches whatever was still settling - an effect that accumulates over frames, a
    /// shader compiled on the frame it is first needed, a buffer uploaded at the end of the last
    /// update - and the capture then reports a state the game is never actually in.
    /// </remarks>
    public int FramesBetween { get; set; } = 60;

    /// <summary>Frames to keep running after the last shot, before quitting.</summary>
    /// <remarks>
    /// Long enough to look at. A run that quits the instant it has written its files leaves nothing
    /// to check the files against.
    /// </remarks>
    public int TailFrames { get; set; } = 480;

    /// <summary>
    /// Whether the game quits once the last shot is saved.
    /// </summary>
    /// <remarks>
    /// Off leaves the scene on screen with the camera on the last pose and control handed back,
    /// which is how a capture that shows nothing gets checked against a pair of eyes: either the
    /// scene is empty too, or the capture is lying about it.
    /// </remarks>
    public bool ExitWhenDone { get; set; } = true;

    /// <summary>Prefix for the file names.</summary>
    public string Prefix { get; set; } = "shot";

    /// <summary>Where the camera looks from and at, in world space.</summary>
    public List<(Vector3 From, Vector3 To, string Name)> Poses { get; } = [];

    private int frame;
    private int posed;
    private int saved;
    private int tail;

    public override void Update()
    {
        frame++;
        if (frame < WarmupFrames)
            return;

        // One pose per interval: moved to on the first frame of it, read back on the last, because
        // the frame on screen during an interval is the pose set at the start of it.
        if ((frame - WarmupFrames) % FramesBetween != 0)
            return;

        // The pose held during the interval that just ended. Saving happens before posing, and on
        // its own counter, so the last pose is saved too - trailing it off the end of the run is how
        // it went missing before, silently, leaving one fewer file than there are poses.
        if (saved < posed)
            Save(Poses[saved++].Name);

        if (posed >= Poses.Count)
        {
            if (saved >= Poses.Count && ExitWhenDone && ++tail * FramesBetween >= TailFrames)
                ((Game)Game).Exit();
            return;
        }

        var camera = SceneSystem.SceneInstance.RootScene.Entities
            .FirstOrDefault(entity => entity.Get<CameraComponent>() is { Enabled: true });
        if (camera is null)
            return;

        var pose = Poses[posed++];
        camera.Transform.Position = pose.From;
        camera.Transform.Rotation = LookAt(pose.From, pose.To);
        camera.Transform.UpdateWorldMatrix();
    }

    /// <summary>Rotation that looks from one point to another, with no roll.</summary>
    private static Quaternion LookAt(Vector3 from, Vector3 to)
    {
        var forward = Vector3.Normalize(to - from);
        var yaw = MathF.Atan2(-forward.X, -forward.Z);
        var pitch = MathF.Asin(MathUtil.Clamp(forward.Y, -1f, 1f));
        return Quaternion.RotationYawPitchRoll(yaw, pitch, 0);
    }

    private void Save(string name)
    {
        try
        {
            var directory = Path.IsPathRooted(Directory) ? Directory : Path.Combine(AppContext.BaseDirectory, Directory);
            System.IO.Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{Prefix}-{name}.png");

            using var image = GraphicsDevice.Presenter.BackBuffer.GetDataAsImage(((Game)Game).GraphicsContext.CommandList);

            // The back buffer's alpha is whatever the last pass left there; PNG keeps it, and the
            // capture then opens washed out over white.
            var pixels = image.PixelBuffer[0];
            if (pixels.PixelSize == 4)
            {
                var bytes = pixels.GetPixels<byte>();
                for (int i = 3; i < bytes.Length; i += 4)
                    bytes[i] = byte.MaxValue;
                pixels.SetPixels(bytes);
            }

            using var stream = File.Create(path);
            image.Save(stream, ImageFileType.Png);
        }
        catch (Exception exception)
        {
            Log.Error($"Could not save a shot: {exception.Message}");
        }
    }
}
