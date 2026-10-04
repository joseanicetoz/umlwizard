using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using UmlWizard.App.ViewModels.Dialogs;

namespace UmlWizard.App.Controls;

/// <summary>
/// The colour picker's brightness: a vertical track from the colour at full brightness (top) to black (bottom), with a
/// thumb at the chosen value. Clicking or dragging chooses another value.
/// </summary>
public sealed class ColorValueSlider : Control
{
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<ColorValueSlider, double>(nameof(Hue));

    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<ColorValueSlider, double>(nameof(Saturation));

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ColorValueSlider, double>(nameof(Value), 1, defaultBindingMode: BindingMode.TwoWay);

    private const double TrackWidth = 8;
    private const double ThumbRadius = 7;

    static ColorValueSlider()
    {
        AffectsRender<ColorValueSlider>(HueProperty, SaturationProperty, ValueProperty);
        FocusableProperty.OverrideDefaultValue<ColorValueSlider>(true);
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

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(ThumbRadius * 2 + 2, 0);

    public override void Render(DrawingContext context)
    {
        var top = ColorPickerViewModel.FromHsv(Hue, Saturation, 1);
        var track = new Rect((Bounds.Width - TrackWidth) / 2, ThumbRadius, TrackWidth, Math.Max(0, Bounds.Height - 2 * ThumbRadius));
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Color.FromRgb(top.Red, top.Green, top.Blue), 0), new GradientStop(Colors.Black, 1)],
        };
        context.DrawRectangle(gradient, new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)), 1), track, TrackWidth / 2, TrackWidth / 2);

        var current = ColorPickerViewModel.FromHsv(Hue, Saturation, Value);
        var center = new Point(Bounds.Width / 2, track.Top + (1 - Value) * track.Height);
        context.DrawEllipse(new SolidColorBrush(Color.FromRgb(current.Red, current.Green, current.Blue)), new Pen(Brushes.White, 2), center, ThumbRadius, ThumbRadius);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), 1), center, ThumbRadius + 1, ThumbRadius + 1);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Choose(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured == this)
        {
            Choose(e.GetPosition(this).Y);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    /// <summary>Chooses the value at a height, kept within the track.</summary>
    internal void Choose(double y)
    {
        var height = Bounds.Height - 2 * ThumbRadius;
        if (height > 0)
        {
            Value = 1 - Math.Clamp((y - ThumbRadius) / height, 0, 1);
        }
    }
}
