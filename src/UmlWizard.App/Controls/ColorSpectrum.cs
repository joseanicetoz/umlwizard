using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using UmlWizard.App.ViewModels.Dialogs;

namespace UmlWizard.App.Controls;

/// <summary>
/// The colour picker's spectrum: hue from left to right, saturation from the top (full) to the bottom (none), at full
/// brightness. A ring marks the chosen colour; clicking or dragging chooses another.
/// </summary>
public sealed class ColorSpectrum : Control
{
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<ColorSpectrum, double>(nameof(Hue), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<ColorSpectrum, double>(nameof(Saturation), 1, defaultBindingMode: BindingMode.TwoWay);

    private const double MarkerRadius = 7;

    // Red, yellow, green, cyan, blue, magenta and back to red.
    private static readonly IBrush Hues = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = [.. Enumerable.Range(0, 7).Select(i => new GradientStop(ToColor(ColorPickerViewModel.FromHsv(i * 60, 1, 1)), i / 6d))],
    };

    private static readonly IBrush Whiten = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = [new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Colors.White, 1)],
    };

    static ColorSpectrum()
    {
        AffectsRender<ColorSpectrum>(HueProperty, SaturationProperty);
        FocusableProperty.OverrideDefaultValue<ColorSpectrum>(true);
    }

    public double Hue
    {
        get => GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    public double Saturation
    {
        get => GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        using (context.PushClip(new RoundedRect(bounds, 4)))
        {
            context.FillRectangle(Hues, bounds);
            context.FillRectangle(Whiten, bounds);
        }

        // The marker: a white ring with a dark edge, readable on every colour.
        var center = new Point(Hue / 360 * bounds.Width, (1 - Saturation) * bounds.Height);
        context.DrawEllipse(null, new Pen(Brushes.Black, 3), center, MarkerRadius, MarkerRadius);
        context.DrawEllipse(null, new Pen(Brushes.White, 1.5), center, MarkerRadius, MarkerRadius);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Choose(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured == this)
        {
            Choose(e.GetPosition(this));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    /// <summary>Chooses the colour at a point, kept within the spectrum.</summary>
    internal void Choose(Point point)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        Hue = Math.Clamp(point.X / Bounds.Width, 0, 1) * 360;
        Saturation = 1 - Math.Clamp(point.Y / Bounds.Height, 0, 1);
    }

    private static Color ToColor(SkiaSharp.SKColor color) => Color.FromRgb(color.Red, color.Green, color.Blue);
}
