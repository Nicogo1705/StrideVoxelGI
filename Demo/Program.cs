using System.Linq;
using Demo;
using Stride.Engine;
using Stride.Graphics;

// The whole demo: open the game, which loads the scene named in GameSettings. Anything clever
// belongs in the scene or in a script, not here — this file exists so `dotnet run` has a Main,
// and so the store can launch the demo the same way on every operating system.
using var game = new Game();

// A Debug build turns on the D3D11 debug layer, and Stride answers every validation message by
// dumping the whole render scope tree. Voxelization trips one such message each frame - it rebinds
// the clipmap for writing while the previous frame's lighting pass still has it bound for reading,
// which D3D resolves on its own - so the console fills with the same tree forever and nothing else
// is readable. The demo has no use for the debug layer; run the engine's own tests to get it back.
game.GraphicsDeviceManager.DeviceCreationFlags = DeviceCreationFlags.None;

// One exception, and it earns its place: the asset compiler resolves a scene's script tags against
// the assemblies it has loaded, and the executable's own is not always among them. Stride 4.3 drops
// BasicCameraController out of the scene with
//
//   Unable to resolve tag [!Demo.BasicCameraController,Demo]
//
// and replaces it with an inert object, leaving the camera frozen - which is precisely when you
// most want to fly around and look. Attaching the controller here skips type resolution entirely
// and costs nothing when the scene already carries one.
game.Script.AddTask(async () =>
{
    await game.Script.NextFrame();

    var scene = game.SceneSystem.SceneInstance?.RootScene;
    if (scene is null)
        return;

    foreach (var entity in scene.Entities.ToList())
    {
        if (entity.Get<CameraComponent>() is null || entity.Get<BasicCameraController>() is not null)
            continue;

        entity.Add(new BasicCameraController());
    }
});

game.Run();
