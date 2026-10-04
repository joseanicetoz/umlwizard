using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SkiaSharp;
using UmlWizard.App.Controls;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views;

namespace UmlWizard.App.Tests;

public sealed class ColorPickerTests
{
    [Fact]
    public void HsvAndRgb_ConvertBothWays()
    {
        foreach (var swatch in ColorPickerViewModel.BasicColors)
        {
            var color = SKColor.Parse(swatch.Hex);
            var (h, s, v) = ColorPickerViewModel.ToHsv(color);
            Assert.Equal(color, ColorPickerViewModel.FromHsv(h, s, v));
        }

        Assert.Equal((0d, 1d, 1d), ColorPickerViewModel.ToHsv(SKColors.Red));
        Assert.Equal(new SKColor(0, 0, 255), ColorPickerViewModel.FromHsv(240, 1, 1));
        Assert.Equal(48, ColorPickerViewModel.BasicColors.Count);
    }

    [AvaloniaFact]
    public void TheColour_IsTheSame_InTheSpectrumTheNumbersAndTheHexBox()
    {
        ColorPickerViewModel.CustomColorStore = new InMemorySettingsStore();
        var picker = new ColorPickerViewModel("Edit Colour — Fill", SKColor.Parse("#336699"));

        Assert.Equal("#336699", picker.HexText);
        Assert.Equal((51m, 102m, 153m), (picker.Red!.Value, picker.Green!.Value, picker.Blue!.Value));
        Assert.Equal(210, picker.Hue, 0);

        // Typing a colour (with or without #) sets it; text that is not one yet waits.
        picker.HexText = "c62828";
        Assert.Equal("#C62828", picker.Hex);
        Assert.Equal(198m, picker.Red);
        picker.HexText = "#12";
        Assert.Equal("#C62828", picker.Hex);

        picker.Green = 255;
        Assert.Equal("#C6FF28", picker.Hex);
        Assert.Equal("#C6FF28", picker.HexText);

        // The HSV fields show hue in degrees, saturation and value in percent.
        picker.HueField = 240;
        picker.SaturationField = 100;
        picker.ValueField = 100;
        Assert.Equal("#0000FF", picker.Hex);

        // A basic colour; a grey keeps the hue, so the spectrum's marker stays where it was.
        picker.PickCommand.Execute(new ColorSwatch("#808080"));
        Assert.Equal("#808080", picker.Hex);
        Assert.Equal(240, picker.Hue, 0);
    }

    [AvaloniaFact]
    public void CustomColours_AreAdded_AndKeptForTheNextTime()
    {
        var store = new InMemorySettingsStore();
        ColorPickerViewModel.CustomColorStore = store;
        var picker = new ColorPickerViewModel("T", SKColor.Parse("#123456"));
        Assert.Equal(ColorPickerViewModel.CustomColorCount, picker.CustomColors.Count);
        Assert.All(picker.CustomColors, c => Assert.True(c.IsEmpty));

        picker.AddCustomColorCommand.Execute(null);
        picker.AddCustomColorCommand.Execute(null);
        picker.HexText = "#ABCDEF";
        picker.AddCustomColorCommand.Execute(null);
        Assert.Equal(["#123456", "#ABCDEF"], store.Load().CustomColors);

        var next = new ColorPickerViewModel("T", SKColors.White);
        Assert.Equal(["#123456", "#ABCDEF"], next.CustomColors.Take(2).Select(c => c.Hex));
        next.PickCustomCommand.Execute(next.CustomColors[1]);
        Assert.Equal("#ABCDEF", next.Hex);
    }

    [AvaloniaFact]
    public void TheView_HasTheColoursOnTheLeft_AndTheSpectrumAndNumbersOnTheRight()
    {
        ColorPickerViewModel.CustomColorStore = new InMemorySettingsStore();
        var picker = new ColorPickerViewModel("Edit Colour — Fill", SKColors.Red);
        var window = new DialogWindow { DataContext = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var spectrum = window.GetLogicalDescendants().OfType<ColorSpectrum>().Single();
        var slider = window.GetLogicalDescendants().OfType<ColorValueSlider>().Single();
        Assert.Equal(48, window.GetLogicalDescendants().OfType<ItemsControl>().Single(i => i.Name == "BasicColors").ItemCount);
        Assert.Equal(ColorPickerViewModel.CustomColorCount, window.GetLogicalDescendants().OfType<ItemsControl>().Single(i => i.Name == "CustomColors").ItemCount);

        // HSV and RGB side by side, always; the colours on the left, the spectrum on the right.
        var numbers = window.GetLogicalDescendants().OfType<NumericUpDown>().ToDictionary(n => n.Name!);
        Assert.All(new[] { "HueBox", "SaturationBox", "ValueBox", "RedBox", "GreenBox", "BlueBox" }, n => Assert.True(numbers[n].IsEffectivelyVisible, n));
        double Left(Visual v) => v.TranslatePoint(default, window)!.Value.X;
        var basic = window.GetLogicalDescendants().OfType<ItemsControl>().Single(i => i.Name == "BasicColors");
        Assert.True(Left(basic) < Left(spectrum));
        Assert.True(Left(numbers["HueBox"]) < Left(numbers["RedBox"]));
        var add = window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AddCustomColorButton");
        Assert.Equal("Add to Custom Colours", add.Content);

        // Clicking the middle of the spectrum: hue 180 (cyan), half saturation; the bottom of the slider: black.
        spectrum.Choose(new Point(spectrum.Bounds.Width / 2, spectrum.Bounds.Height / 2));
        Assert.Equal(180, picker.Hue, 0);
        Assert.Equal(0.5, picker.Saturation, 2);
        Assert.Equal("#80FFFF", picker.Hex);
        slider.Choose(slider.Bounds.Height);
        Assert.Equal(0, picker.Value);
        Assert.Equal("#000000", picker.Hex);
        window.Close();
    }

    [AvaloniaFact]
    public async Task APickerOpenedFromPreferences_OpensAboveIt()
    {
        var main = new Window();
        main.Show();
        var services = new WindowServices(main);
        var preferences = services.ShowPreferencesAsync(new PreferencesViewModel(new AppSettings(), services, _ => { }));
        Dispatcher.UIThread.RunJobs();
        var preferencesWindow = Assert.Single(main.OwnedWindows.OfType<PreferencesWindow>());

        ColorPickerViewModel.CustomColorStore = new InMemorySettingsStore();
        var pick = services.ShowDialogAsync(new ColorPickerViewModel("T", SKColors.Red));
        Dispatcher.UIThread.RunJobs();

        var picker = Assert.Single(preferencesWindow.OwnedWindows.OfType<DialogWindow>());
        picker.Close(true);
        Assert.True(await pick);
        preferencesWindow.Close();
        await preferences;
        main.Close();
    }
}
