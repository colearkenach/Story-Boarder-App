using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace StoryBoarder.Controls;

/// <summary>
/// Centered, wrapped text that shrinks its font until it fits, the way Miro sticky notes do.
/// </summary>
public sealed class FitText : Control
{
    private const double MinFontSize = 4;
    private static readonly Typeface Face = new(FontFamily.Default, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<FitText, string?>(nameof(Text));

    public static readonly StyledProperty<double> MaxFontSizeProperty =
        AvaloniaProperty.Register<FitText, double>(nameof(MaxFontSize), 28);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<FitText>();

    private double? _widestWordPerEm; // width of the longest word at a font size of 1, cached per text

    static FitText()
    {
        AffectsRender<FitText>(TextProperty, MaxFontSizeProperty, ForegroundProperty);
        ClipToBoundsProperty.OverrideDefaultValue<FitText>(true);
    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double MaxFontSize { get => GetValue(MaxFontSizeProperty); set => SetValue(MaxFontSizeProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) _widestWordPerEm = null;
    }

    // Take whatever space the parent gives; the font size adapts to it.
    protected override Size MeasureOverride(Size availableSize) => default;

    public override void Render(DrawingContext context)
    {
        var box = Bounds.Size;
        if (string.IsNullOrEmpty(Text) || box.Width < 1 || box.Height < 1) return;

        var text = Layout(MaxFontSize, box);
        if (!Fits(text, MaxFontSize, box))
        {
            // Binary search for the largest font size that still fits.
            double lo = MinFontSize, hi = MaxFontSize;
            text = Layout(lo, box);
            while (hi - lo > 0.5)
            {
                var mid = (lo + hi) / 2;
                var candidate = Layout(mid, box);
                if (Fits(candidate, mid, box)) (lo, text) = (mid, candidate);
                else hi = mid;
            }
        }
        context.DrawText(text, new Point(0, Math.Max(0, (box.Height - text.Height) / 2)));
    }

    private FormattedText Layout(double fontSize, Size box) =>
        new(Text!, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, fontSize, Foreground)
        {
            MaxTextWidth = box.Width,
            TextAlignment = TextAlignment.Center,
        };

    // The longest word has to fit on one line, so words never get split mid-way.
    private bool Fits(FormattedText text, double fontSize, Size box) =>
        text.Height <= box.Height && WidestWordPerEm() * fontSize <= box.Width;

    private double WidestWordPerEm()
    {
        if (_widestWordPerEm is { } cached) return cached;
        const double size = 100; // measure large and scale down, for precision
        var widest = 0.0;
        foreach (var word in Text!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct())
        {
            var measured = new FormattedText(word, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, null);
            widest = Math.Max(widest, measured.Width);
        }
        return (_widestWordPerEm = widest / size).Value;
    }
}
