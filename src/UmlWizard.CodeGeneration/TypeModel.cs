using UmlWizard.Core.Model;

namespace UmlWizard.CodeGeneration;

/// <summary>A class, interface or enumeration to generate.</summary>
public sealed class TypeModel(UmlClass cls, string name)
{
    public UmlClass Class { get; } = cls;

    /// <summary>The class name as an identifier, unique in the diagram.</summary>
    public string Name { get; } = name;

    public bool IsInterface => Class.Kind == ClassifierKind.Interface;

    public bool IsEnum => Class.Kind == ClassifierKind.Enumeration;

    /// <summary>An abstract class, or a class with an abstract operation.</summary>
    public bool IsAbstract => Class.Kind == ClassifierKind.AbstractClass || Operations.Any(o => o.IsAbstract);

    public List<TypeModel> BaseClasses { get; } = [];

    public List<TypeModel> Interfaces { get; } = [];

    public List<AttributeModel> Attributes { get; } = [];

    public List<AssociationEnd> Ends { get; } = [];

    public List<OperationModel> Operations { get; } = [];

    /// <summary>Types this one uses (dependencies): C++ includes them in its source file.</summary>
    public List<TypeModel> Dependencies { get; } = [];

    /// <summary>The texts of the notes attached to the class, then its stereotype.</summary>
    public List<string> Documentation { get; } = [];

    /// <summary>Another type inherits from or implements this one: C++ gives it a virtual destructor.</summary>
    public bool IsBase { get; internal set; }

    /// <summary>The base classes, nearest first.</summary>
    public IEnumerable<TypeModel> Ancestors()
    {
        var seen = new HashSet<TypeModel>();
        var queue = new Queue<TypeModel>(BaseClasses);
        while (queue.TryDequeue(out var type))
        {
            if (type != this && seen.Add(type))
            {
                yield return type;
                foreach (var next in type.BaseClasses)
                {
                    queue.Enqueue(next);
                }
            }
        }
    }

    /// <summary>Every interface this type implements, also through its base classes and other interfaces.</summary>
    public IEnumerable<TypeModel> AllInterfaces()
    {
        var seen = new HashSet<TypeModel>();
        var queue = new Queue<TypeModel>(Ancestors().Prepend(this).SelectMany(t => t.Interfaces));
        while (queue.TryDequeue(out var type))
        {
            if (type != this && seen.Add(type))
            {
                yield return type;
                foreach (var next in type.Interfaces)
                {
                    queue.Enqueue(next);
                }
            }
        }
    }
}
