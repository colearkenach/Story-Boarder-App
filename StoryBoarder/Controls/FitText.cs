using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace StoryBoarder.Controls;

/// <summary>
/// Centered, wrapped text that shrinks its font until it fits, the way Miro sticky notes do.
/// </summary>
public sealed class FitText : FrameworkElement
{
    private const double MinFontSize = 4;
    private static readonly Typeface Face = new("Segoe UI");

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(FitText),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxFontSizeProperty = DependencyProperty.Register(
        nameof(MaxFontSize), typeof(double), typeof(FitText),
        new FrameworkPropertyMetadata(28.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(FitText),
        new FrameworkPropertyMetadata(SystemColors.ControlTextBrush,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double MaxFontSize { get => (double)GetValue(MaxFontSizeProperty); set => SetValue(MaxFontSizeProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public FitText() => ClipToBounds = true;

    // Take whatever space the parent gives; the font size adapts to it.
    protected override Size MeasureOverride(Size availableSize) => default;

    protected override void OnRender(DrawingContext dc)
    {
        var box = RenderSize;
        if (string.IsNullOrEmpty(Text) || box.Width < 1 || box.Height < 1) return;

        var text = Layout(MaxFontSize, box);
        if (!Fits(text, box))
        {
            // Binary search for the largest font size that still fits.
            double lo = MinFontSize, hi = MaxFontSize;
            text = Layout(lo, box);
            while (hi - lo > 0.5)
            {
                var mid = (lo + hi) / 2;
                var candidate = Layout(mid, box);
                if (Fits(candidate, box)) (lo, text) = (mid, candidate);
                else hi = mid;
            }
        }
        dc.DrawText(text, new Point(0, Math.Max(0, (box.Height - text.Height) / 2)));
    }

    private FormattedText Layout(double fontSize, Size box) =>
        new(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, fontSize, Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = box.Width,
            TextAlignment = TextAlignment.Center,
        };

    // MinWidth is the widest single word, so words never get split mid-way.
    private static bool Fits(FormattedText text, Size box) =>
        text.Height <= box.Height && text.MinWidth <= box.Width;
}
