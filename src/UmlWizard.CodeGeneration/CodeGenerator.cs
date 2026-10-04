using UmlWizard.Core.Model;

namespace UmlWizard.CodeGeneration;

/// <summary>
/// Generates source code from a class diagram: a type for every class, interface and enumeration with its attributes
/// and operation stubs; inheritance and realization become base types, associations, aggregations and compositions
/// become fields, and attached notes and stereotypes become documentation comments. The language is written by an
/// <see cref="ICodeGenerator"/>, usually from a plug-in.
/// </summary>
public static class CodeGenerator
{
    public static CodeGenerationResult Generate(ClassDiagramModel diagram, ICodeGenerator generator, CodeGenerationOptions options)
    {
        var warnings = new List<string>();
        var model = CodeModel.Build(diagram, generator.MultipleInheritance, warnings);
        var files = generator.Generate(model, options, warnings).ToList();
        return new CodeGenerationResult(files, warnings);
    }
}
