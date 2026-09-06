using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;

namespace Demo.Shell;

/// <summary>Where a block of overlay text sits on the screen.</summary>
public enum OverlayAnchor
{
    /// <summary>The right-hand column, first: the settings. The profiler keeps the left edge.</summary>
    TopRight,
    /// <summary>The right-hand column, after the settings: the controls, and what a scene says about its state.</summary>
    BottomLeft,
    /// <summary>The right-hand column, last: short status lines.</summary>
    BottomRight,
    /// <summary>Around the centre, at an offset: a card or a caption.</summary>
    Centre,
}

/// <summary>
/// The one place text is drawn over the frame. Every scene, component and toggle that has
/// something to say registers a named section with an anchor and a function that yields its
/// lines; this script lays the sections out each frame, so nothing ever prints through anything
/// else and the layout is decided once, here.
/// </summary>
/// <remarks>
/// The right edge holds the settings columns and the left edge is left to the engine's profiler,
/// so both can be read at once. Sections under one anchor stack in registration order, by
/// <c>order</c> first; a section whose function yields nothing takes no room.
/// </remarks>
public sealed class DemoOverlay : SyncScript
{
    public const int LineHeight = 18;
    private const int CharWidth = 8;
    private const int Margin = 16;

    private sealed record Section(string Name, OverlayAnchor Anchor, int Order, Func<IEnumerable<string>> Lines, Int2 Offset);

    private static readonly List<Section> sections = [];

    /// <summary>Adds or replaces the section of that name.</summary>
    public static void Register(string name, OverlayAnchor anchor, Func<IEnumerable<string>> lines, int order = 0, Int2 offset = default)
    {
        sections.RemoveAll(s => s.Name == name);
        sections.Add(new Section(name, anchor, order, lines, offset));
    }

    public static void Unregister(string name) => sections.RemoveAll(s => s.Name == name);

    /// <summary>Widest a line may be before it is folded, in characters: the column must not reach the scene.</summary>
    private const int MaxChars = 60;

    /// <summary>
    /// A line wider than the column is folded at its separators - runs of three or more spaces,
    /// which is how a controls line sets its key/description groups apart - into as few lines
    /// as fit. A line with no such runs is kept whole, whatever its width.
    /// </summary>
    private static IEnumerable<string> Wrap(string line)
    {
        if (line.Length <= MaxChars)
        {
            yield return line;
            yield break;
        }
        var groups = System.Text.RegularExpressions.Regex.Split(line.Trim(), @"\s{3,}");
        if (groups.Length <= 1)
        {
            yield return line;
            yield break;
        }
        var current = "";
        foreach (var group in groups)
        {
            var next = current.Length == 0 ? group : current + "   " + group;
            if (next.Length > MaxChars && current.Length > 0)
            {
                yield return current;
                current = group;
            }
            else
                current = next;
        }
        if (current.Length > 0)
            yield return current;
    }

    public override void Update()
    {
        var back = GraphicsDevice.Presenter?.BackBuffer;
        var width = back?.Width ?? 1920;
        var height = back?.Height ?? 1080;

        // One column, down the right edge: the settings first, then the controls, then the short
        // status lines. The left edge is the engine profiler's, so both can be read at once. Only
        // a centred card sits elsewhere.
        static OverlayAnchor Effective(OverlayAnchor anchor) => anchor == OverlayAnchor.Centre ? OverlayAnchor.Centre : OverlayAnchor.TopRight;

        foreach (var group in sections.GroupBy(s => Effective(s.Anchor)))
        {
            // Settings, then controls, then status, each in its own registration order.
            var blocks = group.OrderBy(s => s.Anchor).ThenBy(s => s.Order)
                .Select(s => (s, lines: s.Lines().SelectMany(Wrap).ToList()))
                .Where(b => b.lines.Count > 0)
                .ToList();
            if (blocks.Count == 0)
                continue;

            switch (group.Key)
            {
                case OverlayAnchor.TopRight:
                {
                    // One left edge for the whole column, set by its widest line.
                    var x = width - Margin - blocks.Max(b => b.lines.Max(l => l.Length)) * CharWidth;
                    var y = Margin;
                    foreach (var (_, lines) in blocks)
                    {
                        foreach (var line in lines)
                        {
                            DebugText.Print(line, new Int2(x, y));
                            y += LineHeight;
                        }
                        y += LineHeight;
                    }
                    break;
                }
                case OverlayAnchor.Centre:
                {
                    foreach (var (section, lines) in blocks)
                    {
                        var y = height / 2 + section.Offset.Y;
                        foreach (var line in lines)
                        {
                            DebugText.Print(line, new Int2(width / 2 + section.Offset.X, y));
                            y += LineHeight;
                        }
                    }
                    break;
                }
            }
        }
    }
}
