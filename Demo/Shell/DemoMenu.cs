using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace Demo.Shell;

/// <summary>
/// The home screen: a title, one card per demo, and a footer saying how to drive it.
/// </summary>
/// <remarks>
/// Built in code rather than authored, for the same reason the gallery is: an asset would have to be
/// opened in the editor to change a word, and everything here is words.
/// </remarks>
public sealed class DemoMenu
{
    private static readonly Color Ink = new(232, 234, 240);
    private static readonly Color InkDim = new(150, 156, 170);
    private static readonly Color Accent = new(255, 196, 112);
    private static readonly Color Backdrop = new(16, 17, 21, 245);
    private static readonly Color CardIdle = new(30, 32, 39, 255);
    private static readonly Color CardHot = new(52, 47, 40, 255);

    private readonly List<Button> cards = [];
    private readonly List<TextBlock> names = [];
    private readonly List<TextBlock> taglines = [];
    private readonly TextBlock hint;

    /// <summary>The page to hand a <see cref="Stride.Engine.UIComponent"/>.</summary>
    public UIPage Page { get; }

    /// <summary>Raised when a card is clicked. The keyboard path goes through the shell instead.</summary>
    public event Action<int>? Activated;

    /// <summary>Raised when the pointer moves onto a card, so hovering and the arrow keys agree.</summary>
    public event Action<int>? Highlighted;

    public DemoMenu(SpriteFont regular, SpriteFont bold, IReadOnlyList<DemoEntry> entries)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 760,
        };

        stack.Children.Add(new TextBlock
        {
            Font = bold,
            Text = "STRIDE VOXEL GI",
            TextSize = 40,
            TextColor = Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
        });

        stack.Children.Add(new TextBlock
        {
            Font = regular,
            Text = "Three demos of one idea: light that has bounced.",
            TextSize = 19,
            TextColor = InkDim,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 34),
        });

        for (int i = 0; i < entries.Count; ++i)
        {
            var index = i;
            var entry = entries[i];

            var name = new TextBlock
            {
                Font = bold,
                Text = entry.Name,
                TextSize = 24,
                TextColor = Ink,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var tagline = new TextBlock
            {
                Font = regular,
                Text = entry.Tagline,
                TextSize = 17,
                TextColor = InkDim,
                WrapText = true,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0),
            };

            var content = new StackPanel { Orientation = Orientation.Vertical };
            content.Children.Add(name);
            content.Children.Add(tagline);

            var card = new Button
            {
                Content = content,
                NotPressedImage = null,
                PressedImage = null,
                MouseOverImage = null,
                BackgroundColor = CardIdle,
                Padding = new Thickness(22, 16, 22, 18),
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            card.Click += (_, _) => Activated?.Invoke(index);
            card.MouseOverStateChanged += (_, args) =>
            {
                if (args.NewValue != MouseOverState.MouseOverNone)
                    Highlighted?.Invoke(index);
            };

            cards.Add(card);
            names.Add(name);
            taglines.Add(tagline);
            stack.Children.Add(card);
        }

        hint = new TextBlock
        {
            Font = regular,
            Text = "Up / Down to choose      Enter to start      Escape to quit",
            TextSize = 16,
            TextColor = InkDim,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 26, 0, 0),
        };
        stack.Children.Add(hint);

        var root = new Grid { BackgroundColor = Backdrop };
        root.Children.Add(stack);

        Page = new UIPage { RootElement = root };
        Select(0);
    }

    /// <summary>Moves the highlight. The only visual state this screen has.</summary>
    public void Select(int index)
    {
        for (int i = 0; i < cards.Count; ++i)
        {
            var on = i == index;
            cards[i].BackgroundColor = on ? CardHot : CardIdle;
            names[i].TextColor = on ? Accent : Ink;
            taglines[i].TextColor = on ? Ink : InkDim;
        }
    }
}
