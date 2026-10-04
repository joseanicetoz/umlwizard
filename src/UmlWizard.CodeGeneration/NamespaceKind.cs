namespace UmlWizard.CodeGeneration;

/// <summary>What a language calls the scope its types are declared in.</summary>
public enum NamespaceKind
{
    /// <summary>C#, C++.</summary>
    Namespace,

    /// <summary>Java, Kotlin.</summary>
    Package,

    /// <summary>Ruby; TypeScript, whose modules are files in folders that follow it.</summary>
    Module,
}
