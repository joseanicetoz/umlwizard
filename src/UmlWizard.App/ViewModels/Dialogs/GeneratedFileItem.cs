using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;
using UmlWizard.CodeGeneration;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>A file the code generator will write, and whether it replaces one that is already there.</summary>
public sealed record GeneratedFileItem(string Path, bool Exists);
