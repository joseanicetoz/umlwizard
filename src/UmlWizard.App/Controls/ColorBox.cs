using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using UmlWizard.App.Resources;
using UmlWizard.App.ViewModels.Dialogs;

namespace UmlWizard.App.Controls;

/// <summary>
/// The input of a colour, wherever the application asks for one: a swatch of the colour, which opens the colour picker
/// (through <see cref="EditCommand"/>), and the colour as <c>#RRGGBB</c>, which can also be typed. While no colour is
/// set, the swatch and the box's watermark show the default.
/// </summary>
public sealed class ColorBox : UserControl
{
    /// <summary>The colour as typed, <c>#RRGGBB</c>; empty or <c>null</c> for none.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ColorBox, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>The default colour, <c>#RRGGBB</c>: shown while <see cref="Text"/> is not a colour.</summary>
    public static readonly StyledProperty<string?> DefaultColorProperty =
        AvaloniaProperty.Register<ColorBox, string?>(nameof(DefaultColor));

    /// <summary>Opens the colour picker; the command sets the colour it returns.</summary>
    public static readonly StyledProperty<ICommand?> EditCommandProperty =
        AvaloniaProperty.Register<ColorBox, ICommand?>(nameof(EditCommand));

    private readonly Button _swatch;
    private readonly TextBox _box;

    public ColorBox()
    {
        _swatch = new Button { Classes = { "swatch" }, Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(_swatch, Strings.ColorBox_Edit_Tip);
        _box = new TextBox { Width = 110, MaxLength = 9, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(_box, Strings.Preferences_Color_Tip);
        _box[!TextBox.TextProperty] = this[!TextProperty];
        _box[!TextBox.WatermarkProperty] = this[!DefaultColorProperty];
        _swatch[!Button.CommandProperty] = this[!EditCommandProperty];
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _swatch, _box } };
        UpdateSwatch();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? DefaultColor
    {
        get => GetValue(DefaultColorProperty);
        set => SetValue(DefaultColorProperty, value);
    }

    public ICommand? EditCommand
    {
        get => GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }

    /// <summary>The swatch button, which opens the colour picker (for tests).</summary>
    internal Button Swatch => _swatch;

    /// <summary>The <c>#RRGGBB</c> box (for tests).</summary>
    internal TextBox Box => _box;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == DefaultColorProperty)
        {
            UpdateSwatch();
        }
    }

    private void UpdateSwatch()
    {
        var hex = ColorPickerViewModel.Normalize(Text) ?? ColorPickerViewModel.Normalize(DefaultColor);
        _swatch.Background = hex is null ? Brushes.Transparent : SolidColorBrush.Parse(hex);
    }
}
