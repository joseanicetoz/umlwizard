using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using SkiaSharp;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Notation;
using UmlWizard.Core.Rendering;

namespace UmlWizard.App.ViewModels;

public enum ZoomAction
{
    In,
    Out,
    Reset,
    Fit,
}
