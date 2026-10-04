using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UmlWizard.CodeImport;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeImport.CSharp;

/// <summary>
/// Reads C# with Roslyn, without building it: classes, interfaces, enumerations, structs («struct») and records
/// («record») with their fields, properties, methods and constructors. Static classes get «static».
/// </summary>
public sealed class CSharpCodeImporter : ICodeImporter
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview, DocumentationMode.None);

    /// <summary>Generic types whose (last) type argument is the element of a collection.</summary>
    private static readonly HashSet<string> Collections =
    [
        "IEnumerable", "ICollection", "IList", "IReadOnlyCollection", "IReadOnlyList", "List", "Collection", "ObservableCollection",
        "ReadOnlyCollection", "HashSet", "ISet", "IReadOnlySet", "SortedSet", "LinkedList", "Queue", "Stack", "ImmutableArray",
        "ImmutableList", "ImmutableHashSet", "IImmutableList", "IImmutableSet", "ConcurrentBag", "BlockingCollection",
        "IAsyncEnumerable", "Dictionary", "IDictionary", "IReadOnlyDictionary", "SortedDictionary", "ConcurrentDictionary",
        "ImmutableDictionary", "IImmutableDictionary", "Lookup", "ILookup",
    ];

    public string Id => "CSharp";

    public string DisplayName => "C#";

    public IReadOnlyList<string> FileExtensions { get; } = [".cs"];

    public CodeImportParseResult Parse(IReadOnlyList<SourceFile> files)
    {
        var trees = files.AsParallel().AsOrdered()
            .Select(f => CSharpSyntaxTree.ParseText(f.Text, ParseOptions, f.Path))
            .ToList();
        var loss = new Losses { FilesWithErrors = trees.Count(t => t.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)) };
        var types = trees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            .Select(d => Read(d, loss))
            .ToList();
        return new CodeImportParseResult(types, loss.Notes());
    }

    private sealed class Losses
    {
        public int FilesWithErrors, Events, Indexers, Operators, Destructors, Delegates, ReadonlyFields, ConstraintClauses, AttributeLists, ParameterDetails;

        public List<string> Notes()
        {
            var notes = new List<string>();
            CodeImporter.Add(notes, FilesWithErrors, "file has syntax errors; its declarations were read as far as possible",
                "files have syntax errors; their declarations were read as far as possible");
            CodeImporter.Add(notes, Events, "event was left out", "events were left out");
            CodeImporter.Add(notes, Indexers, "indexer was left out", "indexers were left out");
            CodeImporter.Add(notes, Operators, "operator was left out", "operators were left out");
            CodeImporter.Add(notes, Destructors, "finalizer was left out", "finalizers were left out");
            CodeImporter.Add(notes, Delegates, "nested delegate type was left out", "nested delegate types were left out");
            CodeImporter.Add(notes, ReadonlyFields, "readonly field is shown as a plain attribute", "readonly fields are shown as plain attributes");
            CodeImporter.Add(notes, ConstraintClauses, "generic constraint (where …) was left out", "generic constraints (where …) were left out");
            CodeImporter.Add(notes, ParameterDetails, "parameter lost its modifier (ref, out, in, params) or default value",
                "parameters lost their modifiers (ref, out, in, params) or default values");
            CodeImporter.Add(notes, AttributeLists, "declaration has C# attributes (e.g. [Serializable]), which were left out",
                "declarations have C# attributes (e.g. [Serializable]), which were left out");
            return notes;
        }
    }

    private static ImportedType Read(BaseTypeDeclarationSyntax declaration, Losses loss)
    {
        var isAbstract = declaration.Modifiers.Any(SyntaxKind.AbstractKeyword);
        var isStatic = declaration.Modifiers.Any(SyntaxKind.StaticKeyword);
        loss.AttributeLists += declaration.AttributeLists.Count > 0 ? 1 : 0;
        var type = new ImportedType
        {
            Namespace = string.Join('.', declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString())),
            Name = declaration.Identifier.Text,
            DeclaringTypes = declaration.Ancestors().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(t => t.Identifier.Text).ToList(),
            TypeParameters = (declaration as TypeDeclarationSyntax)?.TypeParameterList?.Parameters.Select(p => p.Identifier.Text).ToList() ?? [],
            Kind = declaration switch
            {
                InterfaceDeclarationSyntax => ImportedTypeKind.Interface,
                EnumDeclarationSyntax => ImportedTypeKind.Enumeration,
                _ when isAbstract => ImportedTypeKind.AbstractClass,
                _ => ImportedTypeKind.Class,
            },
            Stereotype = declaration switch
            {
                RecordDeclarationSyntax r when r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => "record struct",
                RecordDeclarationSyntax => "record",
                StructDeclarationSyntax => "struct",
                ClassDeclarationSyntax when isStatic => "static",
                _ => null,
            },
        };

        if (declaration is EnumDeclarationSyntax enumeration)
        {
            type.Literals.AddRange(enumeration.Members.Select(m => new ImportedLiteral(m.Identifier.Text, m.EqualsValue?.Value.ToString())));
            return type;
        }

        var declared = (TypeDeclarationSyntax)declaration;
        loss.ConstraintClauses += declared.ConstraintClauses.Count > 0 ? 1 : 0;
        foreach (var baseType in declared.BaseList?.Types ?? [])
        {
            type.BaseTypes.Add(Reference(baseType.Type));
        }

        var inInterface = declaration is InterfaceDeclarationSyntax;

        // Positional records declare public properties; other primary constructors are constructors.
        if (declared.ParameterList is { } primary)
        {
            if (declared is RecordDeclarationSyntax)
            {
                foreach (var parameter in primary.Parameters.Where(p => p.Type is not null))
                {
                    type.Attributes.Add(new ImportedAttribute { Name = parameter.Identifier.Text, Type = Reference(parameter.Type!), Visibility = UmlVisibility.Public });
                }
            }
            else
            {
                type.Operations.Add(Constructor(type.Name, UmlVisibility.Public, primary, loss));
            }
        }

        foreach (var member in declared.Members)
        {
            loss.AttributeLists += member.AttributeLists.Count > 0 && member is not BaseTypeDeclarationSyntax ? 1 : 0;
            var visibility = Visibility(member.Modifiers, inInterface ? UmlVisibility.Public : UmlVisibility.Private);
            var memberIsStatic = member.Modifiers.Any(SyntaxKind.StaticKeyword) || member.Modifiers.Any(SyntaxKind.ConstKeyword);
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    loss.ReadonlyFields += field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) ? 1 : 0;
                    foreach (var variable in field.Declaration.Variables)
                    {
                        type.Attributes.Add(new ImportedAttribute
                        {
                            Name = variable.Identifier.Text,
                            Type = Reference(field.Declaration.Type),
                            Visibility = visibility,
                            IsStatic = memberIsStatic,
                            DefaultValue = variable.Initializer?.Value.ToString(),
                        });
                    }

                    break;

                case PropertyDeclarationSyntax property:
                    type.Attributes.Add(new ImportedAttribute
                    {
                        Name = property.Identifier.Text,
                        Type = Reference(property.Type),
                        Visibility = visibility,
                        IsStatic = memberIsStatic,
                        DefaultValue = property.Initializer?.Value.ToString(),
                    });
                    break;

                case MethodDeclarationSyntax method:
                    loss.ConstraintClauses += method.ConstraintClauses.Count > 0 ? 1 : 0;
                    var generic = method.TypeParameterList is { } list ? $"<{string.Join(", ", list.Parameters.Select(p => p.Identifier.Text))}>" : string.Empty;
                    var operation = new ImportedOperation
                    {
                        Name = method.Identifier.Text + generic,
                        ReturnType = method.ReturnType.ToString(),
                        Visibility = method.ExplicitInterfaceSpecifier is null ? visibility : UmlVisibility.Public,
                        IsStatic = memberIsStatic,
                        IsAbstract = method.Modifiers.Any(SyntaxKind.AbstractKeyword),
                    };
                    operation.Parameters.AddRange(Parameters(method.ParameterList, loss));
                    type.Operations.Add(operation);
                    break;

                case ConstructorDeclarationSyntax constructor:
                    var ctor = Constructor(type.Name, visibility, constructor.ParameterList, loss);
                    ctor.IsStatic = memberIsStatic;
                    type.Operations.Add(ctor);
                    break;

                case EventDeclarationSyntax or EventFieldDeclarationSyntax:
                    loss.Events++;
                    break;
                case IndexerDeclarationSyntax:
                    loss.Indexers++;
                    break;
                case OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax:
                    loss.Operators++;
                    break;
                case DestructorDeclarationSyntax:
                    loss.Destructors++;
                    break;
                case DelegateDeclarationSyntax:
                    loss.Delegates++;
                    break;
            }
        }

        return type;
    }

    private static ImportedOperation Constructor(string name, UmlVisibility visibility, ParameterListSyntax parameters, Losses loss)
    {
        var operation = new ImportedOperation { Name = name, Visibility = visibility };
        operation.Parameters.AddRange(Parameters(parameters, loss));
        return operation;
    }

    private static IEnumerable<UmlParameter> Parameters(ParameterListSyntax list, Losses loss)
    {
        foreach (var parameter in list.Parameters)
        {
            loss.ParameterDetails += parameter.Modifiers.Count > 0 || parameter.Default is not null ? 1 : 0;
            yield return new UmlParameter { Name = parameter.Identifier.Text, Type = parameter.Type?.ToString() };
        }
    }

    /// <summary>A type as used, with the type it refers to through nullable types, arrays and collections.</summary>
    private static TypeRef Reference(TypeSyntax written)
    {
        var syntax = written;
        var many = false;
        var optional = false;
        while (true)
        {
            switch (syntax)
            {
                case NullableTypeSyntax nullable:
                    optional = true;
                    syntax = nullable.ElementType;
                    continue;
                case ArrayTypeSyntax array:
                    many = true;
                    syntax = array.ElementType;
                    continue;
                case GenericNameSyntax g when Collections.Contains(g.Identifier.Text):
                    many = true;
                    syntax = g.TypeArgumentList.Arguments[^1];
                    continue;
                case QualifiedNameSyntax { Right: GenericNameSyntax g } when Collections.Contains(g.Identifier.Text):
                    many = true;
                    syntax = g.TypeArgumentList.Arguments[^1];
                    continue;
            }

            break;
        }

        var (name, arity) = syntax switch
        {
            GenericNameSyntax g => (g.Identifier.Text, g.TypeArgumentList.Arguments.Count),
            QualifiedNameSyntax { Right: GenericNameSyntax g } => (g.Identifier.Text, g.TypeArgumentList.Arguments.Count),
            QualifiedNameSyntax q => (q.Right.Identifier.Text, 0),
            AliasQualifiedNameSyntax a => (a.Name.Identifier.Text, a.Name is GenericNameSyntax g ? g.TypeArgumentList.Arguments.Count : 0),
            SimpleNameSyntax s => (s.Identifier.Text, 0),
            _ => ((string?)null, 0),
        };
        return new TypeRef(written.ToString(), name, arity, many, optional);
    }

    /// <summary>
    /// The UML visibility of C# modifiers; <paramref name="implicit"/> when there are none (private for class members,
    /// public for interface members). <c>protected internal</c> becomes protected and <c>private protected</c> private.
    /// </summary>
    private static UmlVisibility Visibility(SyntaxTokenList modifiers, UmlVisibility @implicit) =>
        modifiers.Any(SyntaxKind.PublicKeyword) ? UmlVisibility.Public
        : modifiers.Any(SyntaxKind.ProtectedKeyword) && !modifiers.Any(SyntaxKind.PrivateKeyword) ? UmlVisibility.Protected
        : modifiers.Any(SyntaxKind.InternalKeyword) ? UmlVisibility.Package
        : modifiers.Any(SyntaxKind.PrivateKeyword) ? UmlVisibility.Private
        : @implicit;
}
