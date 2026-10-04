using UmlWizard.App.Resources;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

public sealed record LifelineOption(string Id, string Label)
{
    public override string ToString() => Label;
}
