using System.Linq;
using Stride.Engine;
using StrideVoxelGI;

namespace Demo.Shell;

/// <summary>
/// Builds each demo into the root scene, around the one camera the game has.
/// </summary>
/// <remarks>
/// The game starts on an empty scene carrying nothing but the menu, and a demo is loaded when it is
/// chosen. Each is handed the camera to drive; none creates its own.
/// </remarks>
public static class DemoScenes
{
    private static Scene? cornellBox;

    public static void BuildCornellBox(Game game, Entity camera)
    {
        // Loaded once and kept: it is an authored scene, and reloading it would throw away anything
        // that changed while it was on screen.
        cornellBox ??= game.Content.Load<Scene>("MainScene");

        game.SceneSystem.SceneInstance.RootScene.Children.Add(cornellBox);

        // The scene brings cameras of its own. They are switched off and their viewpoint copied onto
        // the camera the shell owns - two cameras cannot share a slot, and this one is already in it.
        var authored = cornellBox.Entities.FirstOrDefault(entity => entity.Get<CameraComponent>() is not null);
        if (authored?.Get<CameraComponent>() is { } authoredCamera)
        {
            authoredCamera.Enabled = false;

            authored.Transform.UpdateWorldMatrix();
            camera.Transform.Position = authored.Transform.WorldMatrix.TranslationVector;
            camera.Transform.Rotation = Stride.Core.Mathematics.Quaternion.RotationMatrix(authored.Transform.WorldMatrix);

            if (camera.Get<CameraComponent>() is { } shellCamera)
            {
                shellCamera.VerticalFieldOfView = authoredCamera.VerticalFieldOfView;
                shellCamera.NearClipPlane = authoredCamera.NearClipPlane;
                shellCamera.FarClipPlane = authoredCamera.FarClipPlane;
            }
        }

        // The overlay's keys under Ctrl, as the gallery has them, so a letter means the same thing
        // in every demo: the asset was authored with bare letters, which is the layout the
        // package documents, and the shell is where the demos are made to agree. Its screenshot
        // is the shell's Ctrl+S now, so the overlay's own is off rather than saving twice.
        foreach (var debug in cornellBox.Entities.SelectMany(entity => entity.Components.OfType<VoxelGIDebug>()))
        {
            debug.RequireControl = true;
            debug.ScreenshotKey = Stride.Input.Keys.None;
        }

        camera.Add(new BasicCameraController());
    }

    public static void BuildGallery(Game game, Entity camera) => Gallery.GalleryScene.Build(game, camera);

    public static void BuildVoxelGrid(Game game, Entity camera) => VoxelGridDemo.Build(game, camera);
}
