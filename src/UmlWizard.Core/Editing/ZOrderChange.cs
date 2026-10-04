using UmlWizard.Core.Notation;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Editing;

/// <summary>A change of the stacking (z) order of a diagram node.</summary>
public enum ZOrderChange
{
    BringToFront,
    BringForward,
    SendBackward,
    SendToBack,
}
