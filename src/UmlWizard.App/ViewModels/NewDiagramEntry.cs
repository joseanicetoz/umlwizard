using System.Windows.Input;

namespace UmlWizard.App.ViewModels;

/// <summary>An entry of the new-diagram menus: creates a diagram of <see cref="Type"/>.</summary>
public sealed record NewDiagramEntry(string Header, Material.Icons.MaterialIconKind Icon, ICommand Command, UmlWizard.Core.Model.DiagramType Type);
