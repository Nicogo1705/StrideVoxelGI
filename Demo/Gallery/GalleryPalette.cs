using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

namespace Demo.Gallery;

/// <summary>
/// The materials and the two textures the hall is built from, made once and shared.
/// </summary>
/// <remarks>
/// A gallery at night is mostly one warm stone and one dark plaster; what carries the room is the
/// light landing on them, not the variety of the surfaces. The exceptions earn their place: brass
/// for what the hand touches, glass for what it must not, and a black so matte it reads as a hole.
/// </remarks>
public sealed class GalleryPalette
{
    /// <summary>
    /// What a material was asked for, kept so the room can quote itself.
    /// </summary>
    /// <remarks>
    /// A <see cref="Material"/> is a compiled shader graph and does not hand its inputs back, so
    /// the only place these numbers exist is the call that built it. Recording them here is what
    /// lets the plaque print the surface a visitor is looking at: the hall is a reference for
    /// people who want the look in their own game, and a reference that will not tell you its
    /// values is a postcard.
    /// </remarks>
    public readonly record struct MaterialSpec(string Kind, Color3 Colour, float Roughness, float Metalness, float Emission)
    {
        public override string ToString() => Emission > 0f
            ? $"{Kind,-9} colour {Colour.R:0.00} {Colour.G:0.00} {Colour.B:0.00}   emissive x{Emission:0.#}"
            : $"{Kind,-9} albedo {Colour.R:0.00} {Colour.G:0.00} {Colour.B:0.00}   rough {Roughness:0.00}   metal {Metalness:0.0}";
    }

    private readonly Dictionary<Material, MaterialSpec> specs = new();

    /// <summary>What this material was built from, or null if it did not come from here.</summary>
    public MaterialSpec? Describe(Material? material)
        => material is not null && specs.TryGetValue(material, out var spec) ? spec : null;

    private Material Remember(Material material, MaterialSpec spec)
    {
        specs[material] = spec;
        return material;
    }

    private readonly GraphicsDevice device;

    public GalleryPalette(GraphicsDevice device)
    {
        this.device = device;

        FloorTexture = Checker(128, new Color3(0.30f, 0.28f, 0.26f), new Color3(0.22f, 0.20f, 0.19f), 4);
        PlasterTexture = Speckle(128, new Color3(0.55f, 0.52f, 0.48f), 0.06f, 1723);

        BronzeTexture = Speckle(128, new Color3(0.58f, 0.43f, 0.20f), 0.14f, 913);

        Floor = Textured(FloorTexture, new Vector2(10, 10), 0.40f);
        Bronze = Textured(BronzeTexture, new Vector2(2, 2), 0.45f);
        Plaster = Textured(PlasterTexture, new Vector2(6, 3), 0.70f);
        Stone = Diffuse(new Color3(0.34f, 0.32f, 0.30f), 0.65f);
        Alcove = Diffuse(new Color3(0.62f, 0.58f, 0.52f), 0.9f);
        // Polished, not satin. At 0.28 the reflection lobe is wide enough that the cone returns a
        // smear whatever its aperture, and both the screen-space pass and the voxel cone give their
        // best on a tight lobe - so the brass in this hall is museum brass, kept polished.
        Brass = Metal(new Color3(0.72f, 0.55f, 0.24f), 0.12f);
        Chrome = Metal(new Color3(0.95f, 0.95f, 0.97f), 0.04f);
        Steel = Metal(new Color3(0.56f, 0.57f, 0.60f), 0.35f);
        Soot = Diffuse(new Color3(0.02f, 0.02f, 0.02f), 1.0f);
        Slate = Diffuse(new Color3(0.13f, 0.13f, 0.14f), 0.6f);
        Chalk = Diffuse(new Color3(0.92f, 0.90f, 0.86f), 0.95f);
        Crimson = Diffuse(new Color3(0.55f, 0.05f, 0.05f), 0.9f);
        Viridian = Diffuse(new Color3(0.05f, 0.45f, 0.16f), 0.9f);
        Cobalt = Diffuse(new Color3(0.08f, 0.16f, 0.55f), 0.9f);
        Glass = Transparent(new Color4(0.72f, 0.80f, 0.84f, 0.12f), 0.03f);
        LampOff = Metal(new Color3(0.30f, 0.29f, 0.27f), 0.25f);
    }

    public Texture FloorTexture { get; }
    public Texture PlasterTexture { get; }
    public Texture BronzeTexture { get; }

    public Material Floor { get; }
    public Material Plaster { get; }
    public Material Stone { get; }
    public Material Alcove { get; }
    public Material Brass { get; }

    /// <summary>
    /// Brass to look at, not to shade like. A metalness-1 surface has no diffuse at all, so in a
    /// room whose only light is the bounce it has nothing but the specular cone - and that cone is
    /// scaled by the bounce intensity, so at any sane setting a brass object here is a black
    /// object. This is the same warm ochre with the metalness taken off and a grain put on: the
    /// diffuse cones light it, and it reads as bronze because bronze is mostly its colour.
    /// </summary>
    public Material Bronze { get; }
    public Material Chrome { get; }
    public Material Steel { get; }
    public Material Soot { get; }

    /// <summary>
    /// A dark grey that still returns something. Soot is the right colour for a thing meant to read
    /// as a hole, and the wrong one for anything you want to see the light on: at two percent it
    /// stays black however well it is lit, which looks like the lighting failing rather than the
    /// surface working.
    /// </summary>
    public Material Slate { get; }
    public Material Chalk { get; }
    public Material Crimson { get; }
    public Material Viridian { get; }
    public Material Cobalt { get; }
    public Material Glass { get; }

    /// <summary>A lamp that is off is still a lamp: the fixture stays, the emission goes.</summary>
    public Material LampOff { get; }

    /// <summary>A surface that emits. The voxelizer stores this radiance, so it lights the room.</summary>
    public Material Emissive(Color3 colour, float intensity) => Remember(Material.New(device, new MaterialDescriptor
    {
        Attributes =
        {
            Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(new Color4(0.02f, 0.02f, 0.02f, 1f))),
            DiffuseModel = new MaterialDiffuseLambertModelFeature(),
            Emissive = new MaterialEmissiveMapFeature(new ComputeColor(new Color4(colour, 1f)))
            {
                Intensity = new ComputeFloat(intensity),
            },
        },
    }), new MaterialSpec("Emissive", colour, 0f, 0f, intensity));

    /// <summary>
    /// The microfacet model, with its environment term switched off the LUT and onto the analytic
    /// approximation.
    /// </summary>
    /// <remarks>
    /// <see cref="MaterialSpecularMicrofacetModelFeature"/> defaults its Environment to
    /// <see cref="MaterialSpecularMicrofacetEnvironmentGGXLUT"/>, which reads the split-sum DFG
    /// term out of a Texture2D declared in the PerMaterial resource group. These materials are
    /// built at runtime with Material.New rather than compiled as assets, and nothing in that path
    /// supplies that texture - so the fetch returns zero and the term becomes
    /// <c>specularColor * 0 + 0</c>. On anything with a diffuse lobe the loss hides; on a metal,
    /// where metalness 1 leaves the environment specular as the whole of the shading, the surface
    /// is black. Not dark - exactly zero, whatever surrounds it, with or without GI, which is why
    /// a ball in a uniformly lit alcove still read as a hole with a bright rim.
    /// <para>
    /// The polynomial variant computes the same term in closed form and needs no texture.
    /// </para>
    /// </remarks>
    private static MaterialSpecularMicrofacetModelFeature Microfacet() => new()
    {
        Environment = new MaterialSpecularMicrofacetEnvironmentGGXPolynomial(),
    };

    public Material Diffuse(Color3 colour, float roughness) => Remember(Material.New(device, new MaterialDescriptor
    {
        Attributes =
        {
            Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(new Color4(colour, 1f))),
            DiffuseModel = new MaterialDiffuseLambertModelFeature(),
            Specular = new MaterialMetalnessMapFeature(new ComputeFloat(0f)),
            SpecularModel = Microfacet(),
            MicroSurface = new MaterialGlossinessMapFeature(new ComputeFloat(1f - roughness)),
        },
    }), new MaterialSpec("Diffuse", colour, roughness, 0f, 0f));

    public Material Metal(Color3 colour, float roughness) => Remember(Material.New(device, new MaterialDescriptor
    {
        Attributes =
        {
            Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(new Color4(colour, 1f))),
            DiffuseModel = new MaterialDiffuseLambertModelFeature(),
            Specular = new MaterialMetalnessMapFeature(new ComputeFloat(1f)),
            SpecularModel = Microfacet(),
            MicroSurface = new MaterialGlossinessMapFeature(new ComputeFloat(1f - roughness)),
        },
    }), new MaterialSpec("Metal", colour, roughness, 1f, 0f));

    private Material Textured(Texture texture, Vector2 scale, float roughness) => Remember(Material.New(device, new MaterialDescriptor
    {
        Attributes =
        {
            Diffuse = new MaterialDiffuseMapFeature(new ComputeTextureColor(texture) { Scale = scale }),
            DiffuseModel = new MaterialDiffuseLambertModelFeature(),
            Specular = new MaterialMetalnessMapFeature(new ComputeFloat(0f)),
            SpecularModel = Microfacet(),
            MicroSurface = new MaterialGlossinessMapFeature(new ComputeFloat(1f - roughness)),
        },
    }), new MaterialSpec("Textured", new Color3(1f, 1f, 1f), roughness, 0f, 0f));

    private Material Transparent(Color4 colour, float roughness) => Remember(Material.New(device, new MaterialDescriptor
    {
        Attributes =
        {
            Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(colour)),
            DiffuseModel = new MaterialDiffuseLambertModelFeature(),
            Specular = new MaterialMetalnessMapFeature(new ComputeFloat(0f)),
            SpecularModel = Microfacet(),
            MicroSurface = new MaterialGlossinessMapFeature(new ComputeFloat(1f - roughness)),
            Transparency = new MaterialTransparencyBlendFeature(),
        },
    }), new MaterialSpec("Glass", new Color3(colour.R, colour.G, colour.B), roughness, 0f, 0f));

    /// <summary>Floor tiles, with the grout line dark enough for the bounce to show it.</summary>
    private Texture Checker(int size, Color3 light, Color3 dark, int tiles)
    {
        var pixels = new Color[size * size];
        var cell = size / tiles;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var checker = ((x / cell) + (y / cell)) % 2 == 0;
                var grout = x % cell < 2 || y % cell < 2;
                var colour = grout ? dark * 0.55f : (checker ? light : dark);
                pixels[y * size + x] = new Color(colour.R, colour.G, colour.B, 1f);
            }
        }

        return Texture.New2D(device, size, size, PixelFormat.R8G8B8A8_UNorm_SRgb, pixels);
    }

    /// <summary>Plaster: a flat colour is a dead surface under a moving light, this one has grain.</summary>
    private Texture Speckle(int size, Color3 baseColour, float amount, int seed)
    {
        var pixels = new Color[size * size];
        var random = new Random(seed);

        for (var i = 0; i < pixels.Length; i++)
        {
            var n = 1f + ((float)random.NextDouble() - 0.5f) * 2f * amount;
            pixels[i] = new Color(baseColour.R * n, baseColour.G * n, baseColour.B * n, 1f);
        }

        return Texture.New2D(device, size, size, PixelFormat.R8G8B8A8_UNorm_SRgb, pixels);
    }
}
