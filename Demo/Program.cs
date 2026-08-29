using Stride.Engine;

// The whole demo: open the game, which loads the scene named in GameSettings. Anything clever
// belongs in the scene or in a script, not here — this file exists so `dotnet run` has a Main,
// and so the store can launch the demo the same way on every operating system.
using var game = new Game();
game.Run();
