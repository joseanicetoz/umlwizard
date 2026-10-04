using UmlWizard.Core.Model;

namespace UmlWizard.CodeGeneration;

/// <summary>An operation to generate, with what it overrides or implements.</summary>
public sealed class OperationModel(UmlOperation operation, bool isConstructor, bool isImplicit = false)
{
    public UmlOperation Operation { get; } = operation;

    /// <summary>Named like its class: a constructor.</summary>
    public bool IsConstructor { get; } = isConstructor;

    /// <summary>Not in the diagram: an inherited abstract or interface operation the class has to implement.</summary>
    public bool IsImplicit { get; } = isImplicit;

    public bool IsAbstract { get; internal set; }

    /// <summary>A class derived from the owner overrides it: C# and C++ need it virtual.</summary>
    public bool IsVirtual { get; internal set; }

    /// <summary>Overrides an operation of a base class.</summary>
    public bool IsOverride { get; internal set; }

    /// <summary>Implements an operation of an interface (C++: override, Java: @Override).</summary>
    public bool ImplementsInterface { get; internal set; }

    public bool IsStatic => Operation.IsStatic && !IsConstructor;

    /// <summary>Identifies operations across classes: the name in any casing, and the number of parameters.</summary>
    public string Key => $"{Naming.Snake(Operation.Name)}/{Operation.Parameters.Count}";
}
