using UmlWizard.App.ViewModels;

namespace UmlWizard.App.Tests;

/// <summary>File names made from project and diagram names are the same on every platform, so projects move between them.</summary>
public sealed class SafeFileNameTests
{
    [Theory]
    [InlineData("Online Shop", "Online Shop")]
    [InlineData("Shop: v2", "Shop_ v2")]
    [InlineData("a<b>c\"d/e\\f|g?h*i", "a_b_c_d_e_f_g_h_i")]
    [InlineData("tab\there", "tab_here")]
    [InlineData("Draft...", "Draft")]
    [InlineData("  Spaced  ", "Spaced")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.png", "_nul.png")]
    [InlineData("com1", "_com1")]
    [InlineData("Console", "Console")]
    [InlineData("Diagrams v1.2", "Diagrams v1.2")]
    [InlineData("Größe – Übersicht", "Größe – Übersicht")]
    [InlineData("...", "Untitled")]
    public void Names_BecomeFileNamesThatWorkOnWindowsMacOSAndLinux(string name, string expected)
    {
        Assert.Equal(expected, MainWindowViewModel.SafeFileName(name));
    }
}
