using System;
using System.IO;
using System.Linq;
using Csl;
using Stride.Core.Mathematics;
using Half = Stride.Core.Mathematics.Half;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;

namespace Demo;

/// <summary>
/// Checks that the water shaders written in C# compute exactly what the SDSL they replaced did.
/// </summary>
/// <remarks>
/// <para>
/// Two runs of the demo, one with <c>--water-shaders=reference</c> (the original .sdsl, embedded)
/// and one without (the C# shaders), each with <c>--water-steps=N</c>: the water takes N steps on
/// the same terrain, the amounts and the drawn field are read back and written to a file, and the
/// second run compares its file with the first's. Amounts are multiples of 1/256 in half floats and
/// every step is a pure function of the previous one, so the two must be identical to the bit.
/// </para>
/// <para>
/// One process per run rather than both sets in one: a shader class is found by name, and both sets
/// declare the same names on purpose, so the parameter keys match.
/// </para>
/// </remarks>
public static class WaterEquivalence
{
    /// <summary>Steps to run before reading back; zero leaves the demo alone.</summary>
    public static int Steps { get; set; }

    /// <summary>"reference" or "csharp": which shaders ran, and the name of the dump.</summary>
    public static string Mode { get; private set; } = "csharp";

    /// <summary>Where the dumps go; the executable's directory by default.</summary>
    public static string? OutputDirectory { get; set; }

    /// <summary>Runs the original .sdsl water shaders instead of the C# ones, from the embedded copies.</summary>
    public static void UseReferenceShaders()
    {
        Mode = "reference";
        var assembly = typeof(WaterEquivalence).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Reference/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            // Reference/VoxelWaterStep.sdsl -> VoxelWaterStep
            var shaderName = Path.GetFileNameWithoutExtension(resource.Substring("Reference/".Length));
            ShaderSourceRegistry.Add(shaderName, reader.ReadToEnd(), "Effects/Reference/" + shaderName + ".sdsl.txt");
        }
    }

    /// <summary>Takes the steps, reads back, writes the dump, compares with the other mode's dump when there is one, and quits.</summary>
    public static void Run(Game game, VoxelWater water)
    {
        for (int i = 0; i < Steps; i++)
            water.Step();

        var commandList = game.GraphicsContext.CommandList;
        var amounts = water.Amounts.GetData<Half>(commandList);
        var field = water.Field.GetData<byte>(commandList, 0, 0);
        var occupancy = water.Occupancy.Texture.GetData<byte>(commandList, 0, 0);

        var directory = OutputDirectory is { } given ? (Path.IsPathRooted(given) ? given : Path.Combine(AppContext.BaseDirectory, given)) : AppContext.BaseDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"water-{Mode}-{Steps}.bin");
        using (var file = new BinaryWriter(File.Create(path)))
        {
            file.Write(water.Samples);
            file.Write(Steps);
            file.Write(amounts.Length);
            foreach (var amount in amounts)
                file.Write(amount.RawValue);
            file.Write(field.Length);
            file.Write(field);
            file.Write(occupancy.Length);
            file.Write(occupancy);
        }
        Console.WriteLine($"[water] {Mode}: {Steps} steps over {water.Samples}^3 samples written to {path}");

        var other = Path.Combine(directory, $"water-{(Mode == "reference" ? "csharp" : "reference")}-{Steps}.bin");
        int exitCode = 0;
        if (File.Exists(other))
        {
            var mine = File.ReadAllBytes(path);
            var theirs = File.ReadAllBytes(other);
            int first = -1;
            int differences = 0;
            for (int i = 0; i < Math.Max(mine.Length, theirs.Length); i++)
            {
                if (i >= mine.Length || i >= theirs.Length || mine[i] != theirs[i])
                {
                    if (first < 0) first = i;
                    differences++;
                }
            }
            if (differences == 0)
            {
                Console.WriteLine($"[water] IDENTICAL: amounts, field and occupancy match {Path.GetFileName(other)} bit for bit");
            }
            else
            {
                Console.WriteLine($"[water] DIFFERENT: {differences} bytes differ from {Path.GetFileName(other)}, first at byte {first}");
                exitCode = 1;
            }
        }
        else
        {
            Console.WriteLine($"[water] no {Path.GetFileName(other)} to compare with yet; run the other mode with the same --water-steps");
        }

        Environment.ExitCode = exitCode;
        game.Exit();
    }
}
