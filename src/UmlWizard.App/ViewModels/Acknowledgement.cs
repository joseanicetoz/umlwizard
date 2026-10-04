using CommunityToolkit.Mvvm.Input;
using System.Reflection;
using System.Runtime.InteropServices;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;

namespace UmlWizard.App.ViewModels;

/// <summary>A third-party component acknowledged in the About dialog.</summary>
public sealed record Acknowledgement(string Name, string License, string Url)
{
    public Uri Uri => new(Url);
}
