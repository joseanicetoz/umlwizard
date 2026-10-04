using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using UmlWizard.App.Resources;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>
/// Font family, size and style of a diagram element or of the whole diagram. When
/// <see cref="UseInherited"/> is set the element falls back to the diagram font (or the diagram to the default font).
/// </summary>
public sealed partial class FontEditorViewModel : DialogViewModel
{
    public static string DefaultFamilyName => Strings.Dialog_Font_DefaultFamily;

    private static readonly Lazy<string[]> InstalledFamilies = new(() =>
        SKFontManager.Default.GetFontFamilies()
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray());

    private readonly string _title;
    private readonly DiagramFont? _inherited;

    /// <param name="font">The element's or diagram's own font; <c>null</c> when it inherits.</param>
    /// <param name="inherited">The font used when inheriting (<c>null</c> is the default font).</param>
    public FontEditorViewModel(string title, DiagramFont? font, DiagramFont? inherited, string inheritLabel)
    {
        _title = title;
        _inherited = inherited;
        InheritLabel = inheritLabel;

        var shown = font ?? inherited;
        var family = FamilyName(shown);
        Families = InstalledFamilies.Value.Contains(family, StringComparer.OrdinalIgnoreCase) || family == DefaultFamilyName
            ? [DefaultFamilyName, .. InstalledFamilies.Value]
            : [DefaultFamilyName, family, .. InstalledFamilies.Value];
        Family = family;
        Size = (decimal)(shown?.Size ?? DiagramFont.DefaultSize);
        Bold = shown?.Bold ?? false;
        Italic = shown?.Italic ?? false;
        UseInherited = font is null;
    }

    public override string Title => _title;

    public string InheritLabel { get; }

    public IReadOnlyList<string> Families { get; }

    public decimal MinSize => (decimal)DiagramFont.MinSize;

    public decimal MaxSize => (decimal)DiagramFont.MaxSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom))]
    public partial bool UseInherited { get; set; }

    public bool IsCustom => !UseInherited;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewFamily))]
    public partial string Family { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewSize))]
    public partial decimal? Size { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewWeight))]
    public partial bool Bold { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewStyle))]
    public partial bool Italic { get; set; }

    public FontFamily PreviewFamily => new(Family == DefaultFamilyName ? RenderTheme.DefaultFontFamily : Family);

    public double PreviewSize => (double)Math.Clamp(Size ?? MinSize, MinSize, MaxSize);

    public FontWeight PreviewWeight => Bold ? FontWeight.Bold : FontWeight.Normal;

    public FontStyle PreviewStyle => Italic ? FontStyle.Italic : FontStyle.Normal;

    /// <summary>The chosen font, or <c>null</c> to inherit.</summary>
    public DiagramFont? ToFont() => UseInherited
        ? null
        : new DiagramFont
        {
            Family = Family == DefaultFamilyName ? null : Family,
            Size = (double)Size!.Value,
            Bold = Bold,
            Italic = Italic,
        };

    protected override string? Validate() =>
        !UseInherited && (Size is not { } size || size < MinSize || size > MaxSize)
            ? Loc.Format(Strings.Dialog_Font_SizeRange, MinSize, MaxSize)
            : null;

    partial void OnUseInheritedChanged(bool value)
    {
        // Show what the element will look like when it inherits.
        if (value)
        {
            Family = FamilyName(_inherited);
            Size = (decimal)(_inherited?.Size ?? DiagramFont.DefaultSize);
            Bold = _inherited?.Bold ?? false;
            Italic = _inherited?.Italic ?? false;
        }
    }

    private static string FamilyName(DiagramFont? font) =>
        string.IsNullOrWhiteSpace(font?.Family) ? DefaultFamilyName : font.Family;
}
