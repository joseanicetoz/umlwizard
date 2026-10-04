using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;
using UmlWizard.Core.Rendering;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>
/// The colour picker: a hue and saturation spectrum with a brightness slider, the colour in numbers (HSV and RGB) or as
/// <c>#RRGGBB</c>, the basic colours, and the user's custom colours, kept between runs. Hue, saturation and value are
/// what the colour is; the red, green and blue values and the hex text follow them, and set them when typed.
/// </summary>
public sealed partial class ColorPickerViewModel : DialogViewModel
{
    /// <summary>How many custom colours are kept.</summary>
    public const int CustomColorCount = 16;

    /// <summary>The basic colours: six rows of eight, by family (reds, oranges and yellows, greens, cyans and blues, blues and purples, then magentas and greys).</summary>
    public static IReadOnlyList<ColorSwatch> BasicColors { get; } =
    [
        .. new[]
        {
            "#F08080", "#E53935", "#E02870", "#F090C0", "#8B3A3A", "#7B3F3F", "#801040", "#3A0A0A",
            "#F0A060", "#F08A50", "#8B4A1A", "#FFFF99", "#FFFF33", "#8B8B2A", "#8B8B50", "#1A3A10",
            "#A0FF80", "#A0FF40", "#60FF40", "#80FF60", "#70FF90", "#307A20", "#3A7A4A", "#1A3A3A",
            "#A0FFFF", "#60FFFF", "#3A8080", "#4A7A7A", "#2F7FFF", "#3070B0", "#1F4E8C", "#0028FF",
            "#0A1A8C", "#00108C", "#0A0A40", "#8080FF", "#8080C0", "#6A20E0", "#3A1080", "#6A207A",
            "#F080F0", "#E040E0", "#802080", "#3A1040", "#000000", "#808080", "#C0C0C0", "#FFFFFF",
        }.Select(hex => new ColorSwatch(hex)),
    ];

    /// <summary>Where the custom colours are kept; in memory only when not set (e.g. in tests).</summary>
    public static ISettingsStore CustomColorStore { get; set; } = new InMemorySettingsStore();

    private readonly string _title;
    private bool _updating;

    /// <param name="title">Says what the colour is for, e.g. "Fill".</param>
    /// <param name="color">The colour to start from.</param>
    public ColorPickerViewModel(string title, SKColor color)
    {
        _title = title;
        var saved = CustomColorStore.Load().CustomColors;
        CustomColors = [.. Enumerable.Range(0, CustomColorCount).Select(i => new CustomColorSlot(i < saved.Count ? Normalize(saved[i]) : null))];
        SetColor(color);
    }

    public override string Title => _title;

    #region The colour

    /// <summary>The hue, from 0 to 360 degrees.</summary>
    [ObservableProperty]
    public partial double Hue { get; set; }

    /// <summary>The saturation, from 0 (grey) to 1.</summary>
    [ObservableProperty]
    public partial double Saturation { get; set; }

    /// <summary>The value (brightness), from 0 (black) to 1.</summary>
    [ObservableProperty]
    public partial double Value { get; set; }

    partial void OnHueChanged(double value) => OnHsvChanged();

    partial void OnSaturationChanged(double value) => OnHsvChanged();

    partial void OnValueChanged(double value) => OnHsvChanged();

    /// <summary>The colour chosen.</summary>
    public SKColor Color => FromHsv(Hue, Saturation, Value);

    /// <summary>The colour chosen, as <c>#RRGGBB</c>.</summary>
    public string Hex => ShapeColors.ToHex(Color);

    public IBrush PreviewBrush => new SolidColorBrush(Avalonia.Media.Color.FromRgb(Color.Red, Color.Green, Color.Blue));

    /// <summary>The colour as typed: <c>#RRGGBB</c> (the <c>#</c> may be left out) sets it; anything else waits.</summary>
    [ObservableProperty]
    public partial string HexText { get; set; } = string.Empty;

    partial void OnHexTextChanged(string value)
    {
        if (!_updating && Normalize(value) is { } hex && ShapeColors.Parse(hex) is { } color)
        {
            SetColor(color, keepHexText: true);
        }
    }

    /// <summary>Sets the colour from outside, e.g. a swatch.</summary>
    public void SetColor(SKColor color, bool keepHexText = false)
    {
        var (h, s, v) = ToHsv(color);
        _updating = true;
        try
        {
            // A grey has no hue, and black no saturation either: keep them, so the spectrum does not jump.
            if (s > 0 && v > 0)
            {
                Hue = h;
            }

            if (v > 0)
            {
                Saturation = s;
            }

            Value = v;
        }
        finally
        {
            _updating = false;
        }

        OnHsvChanged(keepHexText);
    }

    private void OnHsvChanged(bool keepHexText = false)
    {
        if (_updating)
        {
            return;
        }

        _updating = true;
        try
        {
            var color = Color;
            (Red, Green, Blue) = (color.Red, color.Green, color.Blue);
            (HueField, SaturationField, ValueField) = ((decimal)Math.Round(Hue), (decimal)Math.Round(Saturation * 100), (decimal)Math.Round(Value * 100));
            if (!keepHexText)
            {
                HexText = Hex;
            }
        }
        finally
        {
            _updating = false;
        }

        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(Hex));
        OnPropertyChanged(nameof(PreviewBrush));
    }

    #endregion

    #region The colour in numbers

    [ObservableProperty]
    public partial decimal? Red { get; set; }

    [ObservableProperty]
    public partial decimal? Green { get; set; }

    [ObservableProperty]
    public partial decimal? Blue { get; set; }

    partial void OnRedChanged(decimal? value) => OnRgbChanged();

    partial void OnGreenChanged(decimal? value) => OnRgbChanged();

    partial void OnBlueChanged(decimal? value) => OnRgbChanged();

    private void OnRgbChanged()
    {
        if (!_updating && Red is { } r && Green is { } g && Blue is { } b)
        {
            SetColor(new SKColor(Byte(r), Byte(g), Byte(b)));
        }

        static byte Byte(decimal value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
    }

    /// <summary>The hue in degrees (0–360), as the HSV fields show it.</summary>
    [ObservableProperty]
    public partial decimal? HueField { get; set; }

    /// <summary>The saturation in percent (0–100), as the HSV fields show it.</summary>
    [ObservableProperty]
    public partial decimal? SaturationField { get; set; }

    /// <summary>The value in percent (0–100), as the HSV fields show it.</summary>
    [ObservableProperty]
    public partial decimal? ValueField { get; set; }

    partial void OnHueFieldChanged(decimal? value) => OnHsvFieldsChanged();

    partial void OnSaturationFieldChanged(decimal? value) => OnHsvFieldsChanged();

    partial void OnValueFieldChanged(decimal? value) => OnHsvFieldsChanged();

    private void OnHsvFieldsChanged()
    {
        if (_updating || HueField is not { } h || SaturationField is not { } s || ValueField is not { } v)
        {
            return;
        }

        _updating = true;
        try
        {
            Hue = Math.Clamp((double)h, 0, 360);
            Saturation = Math.Clamp((double)s / 100, 0, 1);
            Value = Math.Clamp((double)v / 100, 0, 1);
        }
        finally
        {
            _updating = false;
        }

        OnHsvChanged();
    }

    #endregion

    #region Basic and custom colours

    /// <summary>Picks a basic or custom colour.</summary>
    [RelayCommand]
    private void Pick(ColorSwatch swatch)
    {
        if (ShapeColors.Parse(swatch.Hex) is { } color)
        {
            SetColor(color);
        }
    }

    /// <summary>The user's colours, in the order they were added; empty slots at the end.</summary>
    public ObservableCollection<CustomColorSlot> CustomColors { get; }

    /// <summary>Adds the colour to the custom colours, in the first empty slot (or the first one, when all are taken), and keeps them.</summary>
    [RelayCommand]
    private void AddCustomColor()
    {
        var hex = Hex;
        if (CustomColors.Any(c => c.Hex == hex))
        {
            return;
        }

        var slot = CustomColors.FirstOrDefault(c => c.Hex is null) ?? CustomColors[0];
        CustomColors[CustomColors.IndexOf(slot)] = new CustomColorSlot(hex);
        var colors = CustomColors.Select(c => c.Hex).OfType<string>().ToList();
        var settings = CustomColorStore.Load();
        CustomColorStore.Save(settings with { CustomColors = colors });
    }

    [RelayCommand]
    private void PickCustom(CustomColorSlot slot)
    {
        if (slot.Hex is { } hex && ShapeColors.Parse(hex) is { } color)
        {
            SetColor(color);
        }
    }

    #endregion

    protected override string? Validate() => null;

    /// <summary>Opens the picker on <paramref name="color"/>; the chosen colour as <c>#RRGGBB</c>, or <c>null</c> when cancelled.</summary>
    public static async Task<string?> PickAsync(IDialogService dialogs, string title, SKColor color)
    {
        var picker = new ColorPickerViewModel(Loc.Format(Strings.ColorPicker_Title, title), color);
        return await dialogs.ShowDialogAsync(picker) ? picker.Hex : null;
    }

    /// <summary><c>#RRGGBB</c> in capitals, from text with or without the <c>#</c>; <c>null</c> when it is not a colour.</summary>
    internal static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        var color = ShapeColors.Parse(trimmed.StartsWith('#') ? trimmed : "#" + trimmed);
        return color is { } c ? ShapeColors.ToHex(c) : null;
    }

    /// <summary>A colour from hue (0–360), saturation and value (0–1).</summary>
    public static SKColor FromHsv(double hue, double saturation, double value)
    {
        var h = (hue % 360 + 360) % 360 / 60;
        var c = value * saturation;
        var x = c * (1 - Math.Abs(h % 2 - 1));
        var (r, g, b) = (int)h switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        var m = value - c;
        return new SKColor(ToByte(r + m), ToByte(g + m), ToByte(b + m));

        static byte ToByte(double channel) => (byte)Math.Clamp(Math.Round(channel * 255), 0, 255);
    }

    /// <summary>The hue (0–360), saturation and value (0–1) of a colour.</summary>
    public static (double Hue, double Saturation, double Value) ToHsv(SKColor color)
    {
        var (r, g, b) = (color.Red / 255d, color.Green / 255d, color.Blue / 255d);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var hue = delta == 0 ? 0
            : max == r ? 60 * ((g - b) / delta % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        return ((hue + 360) % 360, max == 0 ? 0 : delta / max, max);
    }
}

/// <summary>A slot of the custom colours: a colour, or empty.</summary>
public sealed record CustomColorSlot(string? Hex)
{
    public bool IsEmpty => Hex is null;

    public IBrush Brush => field ??= Hex is null ? Brushes.Transparent : SolidColorBrush.Parse(Hex);

    public ColorSwatch? Swatch => Hex is null ? null : new ColorSwatch(Hex);
}
