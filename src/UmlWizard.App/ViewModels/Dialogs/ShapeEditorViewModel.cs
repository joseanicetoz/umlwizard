using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>
/// Fill, border colour and border thickness of an element (Format → Shape). Each part either uses the theme's colour for
/// the kind of element (the default) or a colour chosen in the colour picker or typed as <c>#RRGGBB</c>.
/// </summary>
public sealed partial class ShapeEditorViewModel : DialogViewModel
{
    private bool _showingDefault;

    private readonly string _title;
    private readonly RenderTheme _theme;
    private readonly SKColor? _defaultFill;
    private readonly SKColor? _defaultBorder;
    private readonly decimal _defaultWidth;

    /// <param name="style">The element's current style; <c>null</c> when it uses the defaults.</param>
    /// <param name="defaultFill">The theme's fill for the element (<c>null</c>: none, as for text boxes).</param>
    /// <param name="defaultBorder">The theme's border colour for the element (<c>null</c>: none).</param>
    /// <param name="theme">The theme the diagram is shown in, for the preview.</param>
    /// <param name="defaultBorderWidth">The border thickness the element has by default (the preferences' for its diagram type).</param>
    public ShapeEditorViewModel(
        string title, ShapeStyle? style, SKColor? defaultFill, SKColor? defaultBorder, RenderTheme theme, double? defaultBorderWidth = null)
        : this(title, style?.Fill, style?.Border, style?.BorderWidth, defaultFill, defaultBorder,
            defaultBorderWidth ?? ShapeStyle.DefaultBorderWidth, theme, isLine: false)
    {
    }

    private ShapeEditorViewModel(
        string title, string? fill, string? border, double? width, SKColor? defaultFill, SKColor? defaultBorder, double defaultWidth,
        RenderTheme theme, bool isLine)
    {
        _title = title;
        _theme = theme;
        _defaultFill = defaultFill;
        _defaultBorder = defaultBorder;
        _defaultWidth = (decimal)defaultWidth;
        IsLine = isLine;

        UseDefaultFill = ShapeColors.Parse(fill) is null;
        FillHex = fill is not null && !UseDefaultFill ? fill.ToUpperInvariant() : DefaultHex(defaultFill);
        UseDefaultBorder = ShapeColors.Parse(border) is null;
        BorderHex = border is not null && !UseDefaultBorder ? border.ToUpperInvariant() : DefaultHex(defaultBorder ?? theme.Border);
        BorderWidth = (decimal)(width ?? defaultWidth);
    }

    /// <summary>
    /// The Format Line dialog of a connector: the colour and thickness of its line (shown in the Border section), no fill.
    /// </summary>
    public static ShapeEditorViewModel ForLine(string title, LineStyle? style, RenderTheme theme, IDialogService? dialogs = null) =>
        new(title, null, style?.Color, style?.Width, null, theme.Line, LineStyle.DefaultWidth, theme, isLine: true) { Dialogs = dialogs };

    public override string Title => _title;

    /// <summary>True for a connector: the dialog sets its line, and has no fill.</summary>
    public bool IsLine { get; }

    public bool HasFill => !IsLine;

    /// <summary>The heading of the colour and thickness section: "Border", or "Line" for a connector.</summary>
    public string BorderHeading => IsLine ? Strings.Dialog_Shape_Line : Strings.Dialog_Shape_Border;

    public decimal MinBorderWidth => (decimal)(IsLine ? LineStyle.MinWidth : ShapeStyle.MinBorderWidth);

    public decimal MaxBorderWidth => (decimal)(IsLine ? LineStyle.MaxWidth : ShapeStyle.MaxBorderWidth);

    /// <summary>The default fill, shown next to "Default"; transparent when the element has none.</summary>
    public IBrush DefaultFillBrush => Brush(_defaultFill);

    public IBrush DefaultBorderBrush => Brush(_defaultBorder);

    /// <summary>Shown instead of a swatch when the element has no fill or border by default.</summary>
    public bool HasNoDefaultFill => _defaultFill is null;

    public bool HasNoDefaultBorder => _defaultBorder is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomFill), nameof(PreviewFill), nameof(PreviewText))]
    public partial bool UseDefaultFill { get; set; }

    public bool IsCustomFill => !UseDefaultFill;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewFill), nameof(PreviewText))]
    public partial string FillHex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomBorder), nameof(PreviewBorder))]
    public partial bool UseDefaultBorder { get; set; }

    public bool IsCustomBorder => !UseDefaultBorder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBorder))]
    public partial string BorderHex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewThickness), nameof(PreviewLineHeight), nameof(PreviewBorder))]
    public partial decimal? BorderWidth { get; set; }

    /// <summary>The fill the preview shows: the chosen one, the default, or the canvas when there is none.</summary>
    public IBrush PreviewFill => Brush(ChosenFill ?? _defaultFill ?? _theme.CanvasBackground);

    /// <summary>The border the preview shows; transparent when a text box keeps its default (none).</summary>
    public IBrush PreviewBorder => Brush(ChosenBorder ?? (HasNoDefaultBorder && !HasCustomWidth ? null : _defaultBorder ?? _theme.Border));

    public Avalonia.Thickness PreviewThickness => new((double)Math.Clamp(BorderWidth ?? MinBorderWidth, MinBorderWidth, MaxBorderWidth));

    /// <summary>The height of the line the preview of a connector shows: its thickness.</summary>
    public double PreviewLineHeight => (double)Math.Clamp(BorderWidth ?? MinBorderWidth, MinBorderWidth, MaxBorderWidth);

    /// <summary>The text colour on the preview: the theme's, or whichever reads better on a custom fill.</summary>
    public IBrush PreviewText => Brush(ChosenFill is { } fill ? ShapeColors.TextColorsOn(fill).Text : _theme.Text);

    private SKColor? ChosenFill => UseDefaultFill ? null : ShapeColors.Parse(FillHex);

    private SKColor? ChosenBorder => UseDefaultBorder ? null : ShapeColors.Parse(BorderHex);

    private bool HasCustomWidth => BorderWidth is { } width && width != _defaultWidth;

    /// <summary>Opens the colour pickers; without it (e.g. in tests) the swatches do nothing.</summary>
    public IDialogService? Dialogs { get; init; }

    /// <summary>Opens the colour picker on the fill; the colour chosen becomes the element's own.</summary>
    [RelayCommand]
    private async Task EditFill()
    {
        if (Dialogs is not null &&
            await ColorPickerViewModel.PickAsync(Dialogs, Strings.Dialog_Shape_Fill, ChosenFill ?? _defaultFill ?? SKColors.White) is { } hex)
        {
            SetFill(hex);
        }
    }

    /// <summary>Opens the colour picker on the border (or the line of a connector).</summary>
    [RelayCommand]
    private async Task EditBorder()
    {
        if (Dialogs is not null &&
            await ColorPickerViewModel.PickAsync(Dialogs, BorderHeading, ChosenBorder ?? _defaultBorder ?? _theme.Border) is { } hex)
        {
            SetBorder(hex);
        }
    }

    /// <summary>Gives the element a fill of its own.</summary>
    public void SetFill(string hex)
    {
        UseDefaultFill = false;
        FillHex = hex;
    }

    /// <summary>Gives the element a border (or line) colour of its own.</summary>
    public void SetBorder(string hex)
    {
        UseDefaultBorder = false;
        BorderHex = hex;
    }

    // Typing a colour other than the default makes it the element's own.
    partial void OnFillHexChanged(string value)
    {
        if (!_showingDefault && UseDefaultFill && ShapeColors.Parse(value) is not null && value.Trim() != DefaultHex(_defaultFill))
        {
            UseDefaultFill = false;
        }
    }

    partial void OnBorderHexChanged(string value)
    {
        if (!_showingDefault && UseDefaultBorder && ShapeColors.Parse(value) is not null && value.Trim() != DefaultHex(_defaultBorder ?? _theme.Border))
        {
            UseDefaultBorder = false;
        }
    }

    /// <summary>Puts every part back to the theme's defaults.</summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        UseDefaultFill = true;
        UseDefaultBorder = true;
        BorderWidth = _defaultWidth;
    }

    /// <summary>The chosen style; <c>null</c> when everything is the default.</summary>
    public ShapeStyle? ToStyle()
    {
        var style = new ShapeStyle
        {
            Fill = ChosenFill is { } fill ? ShapeColors.ToHex(fill) : null,
            Border = ChosenBorder is { } border ? ShapeColors.ToHex(border) : null,
            BorderWidth = HasCustomWidth ? (double)BorderWidth!.Value : null,
        };
        return style.IsEmpty ? null : style;
    }

    /// <summary>The chosen line of a connector (see <see cref="ForLine"/>); <c>null</c> when it is the default.</summary>
    public LineStyle? ToLineStyle()
    {
        var style = new LineStyle
        {
            Color = ChosenBorder is { } color ? ShapeColors.ToHex(color) : null,
            Width = HasCustomWidth ? (double)BorderWidth!.Value : null,
        };
        return style.IsEmpty ? null : style;
    }

    protected override string? Validate()
    {
        if ((!UseDefaultFill && ShapeColors.Parse(FillHex) is null) || (!UseDefaultBorder && ShapeColors.Parse(BorderHex) is null))
        {
            return Strings.Dialog_Shape_InvalidColor;
        }

        return BorderWidth is not { } width || width < MinBorderWidth || width > MaxBorderWidth
            ? Loc.Format(Strings.Dialog_Shape_WidthRange, MinBorderWidth, MaxBorderWidth)
            : null;
    }

    partial void OnUseDefaultFillChanged(bool value)
    {
        // Show the default colour while it is used, and start a custom fill from it.
        if (value)
        {
            _showingDefault = true;
            FillHex = DefaultHex(_defaultFill);
            _showingDefault = false;
        }
    }

    partial void OnUseDefaultBorderChanged(bool value)
    {
        if (value)
        {
            _showingDefault = true;
            BorderHex = DefaultHex(_defaultBorder ?? _theme.Border);
            _showingDefault = false;
        }
    }

    private static string DefaultHex(SKColor? color) => color is { } c ? ShapeColors.ToHex(c) : "#FFFFFF";

    private static IBrush Brush(SKColor? color) => color is { } c
        ? new SolidColorBrush(Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue))
        : Brushes.Transparent;
}

/// <summary>A colour offered to pick, e.g. a basic colour of the colour picker.</summary>
public sealed record ColorSwatch(string Hex)
{
    // Made when first shown: brushes belong to the UI thread, and the basic colours are made with their class.
    public IBrush Brush => field ??= SolidColorBrush.Parse(Hex);
}
