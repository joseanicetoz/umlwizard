using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views.Dialogs;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

public sealed class DialogLabelTests
{
    /// <summary>Texts in the input dialogs that are not field labels: sentences, headings, values and a preview.</summary>
    private static readonly string[] NotLabels =
    [
        "{x:Static res:Strings.Dialog_Font_Sample}", "{x:Static res:Strings.Dialog_Shape_Sample}", "{x:Static res:Strings.Dialog_ImportCode_Notice}",
        // The sample window of Preferences → Appearance shows text in the chosen font, not labels.
        "{x:Static res:Strings.App_Name}", "{x:Static res:Strings.Preferences_Preview_Sample}",
        // The warning of Preferences → AI, in its yellow box.
        "{x:Static res:Strings.Preferences_Ai_Privacy}",
    ];

    private static string DialogsFolder([System.Runtime.CompilerServices.CallerFilePath] string testFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "..", "src", "UmlWizard.App", "Views", "Dialogs"));

    [Fact]
    public void EveryFieldLabel_InTheInputDialogs_UsesTheCaptionStyle()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        // The input dialogs, and the Preferences window, whose settings are labelled the same way.
        var files = Directory.GetFiles(DialogsFolder(), "*.axaml").Append(Path.Combine(DialogsFolder(), "..", "PreferencesWindow.axaml")).ToArray();
        Assert.NotEmpty(files);

        var checkedLabels = 0;
        foreach (var file in files)
        {
            // A field label is a TextBlock with fixed text from the string resources, e.g.
            // Text="{x:Static res:Strings.Label_Name}"; bound texts are sentences or values.
            var labels = XDocument.Load(file).Descendants(avalonia + "TextBlock")
                .Where(t => (string?)t.Attribute("Text") is { } text && text.StartsWith("{x:Static res:Strings.") && !NotLabels.Contains(text))
                .Where(t => (string?)t.Attribute("Classes") != "section");
            foreach (var label in labels)
            {
                Assert.True(
                    ((string?)label.Attribute("Classes"))?.Split(' ').Contains("caption") == true,
                    $"{Path.GetFileName(file)}: the label \"{label.Attribute("Text")!.Value}\" does not use the caption style");
                checkedLabels++;
            }
        }

        // Guards the filter above: the dialogs have many labels, so finding none means it no longer matches them.
        Assert.True(checkedLabels > 20, $"Only {checkedLabels} labels found");
    }

    [AvaloniaFact]
    public void Labels_AreDrawnAt11Px_WhileInputsKeepTheirSize()
    {
        var view = new DiagramPropertiesView { DataContext = new DiagramPropertiesViewModel(DiagramDocument.Create("A", DiagramType.Class)) };
        var window = new Window { Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        foreach (var text in new[] { "Name", "Author", "Description", "Type" })
        {
            var label = view.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == text);
            Assert.Equal(11, label.FontSize);
        }

        Assert.All(view.GetLogicalDescendants().OfType<TextBox>(), box => Assert.NotEqual(11, box.FontSize));
        window.Close();
    }
}
