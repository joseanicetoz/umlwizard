using System.Text.Json;
using System.Text.Json.Nodes;
using UmlWizard.Core.Model;

namespace UmlWizard.Core.Serialization;

public static class ProjectSerializer
{
    public static string Serialize(UmlProject project) => JsonSerializer.Serialize(project, UmlJson.Options);

    /// <summary>
    /// Reads a project. <paramref name="projectDirectory"/> resolves the diagram files of schema 1 projects,
    /// which are embedded on load; the project is written in the current schema on its next save.
    /// </summary>
    public static UmlProject Deserialize(string json, string? projectDirectory = null)
    {
        UmlProject? project;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                throw new InvalidDataException("The project file is empty.");
            }

            var version = root["schemaVersion"]?.GetValue<int>() ?? 1;
            if (version > UmlProject.CurrentSchemaVersion)
            {
                throw new InvalidDataException($"The project was written by a newer version (schema {version}).");
            }

            if (version < 2)
            {
                EmbedDiagramFiles(root, projectDirectory);
            }

            project = root.Deserialize<UmlProject>(UmlJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"The project file is not valid: {ex.Message}", ex);
        }

        if (project is null)
        {
            throw new InvalidDataException("The project file is empty.");
        }

        foreach (var diagram in project.Diagrams)
        {
            DiagramSerializer.Validate(diagram);
        }

        project.Folders ??= [];
        project.NormalizeFolders();

        var duplicate = project.Diagrams.GroupBy(d => d.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"The project contains more than one diagram with the id '{duplicate.Key}'.");
        }

        project.SchemaVersion = UmlProject.CurrentSchemaVersion;
        return project;
    }

    public static UmlProject Load(string path) =>
        Deserialize(File.ReadAllText(path), GetProjectDirectory(path));

    public static void Save(UmlProject project, string path)
    {
        project.SchemaVersion = UmlProject.CurrentSchemaVersion;
        UmlJson.WriteAllTextAtomic(path, Serialize(project));
    }

    public static string GetProjectDirectory(string projectFilePath) =>
        Path.GetDirectoryName(Path.GetFullPath(projectFilePath))!;

    /// <summary>Replaces the diagram references of a schema 1 project with the contents of the referenced files.</summary>
    private static void EmbedDiagramFiles(JsonObject root, string? projectDirectory)
    {
        if (root["diagrams"] is not JsonArray references)
        {
            return;
        }

        var diagrams = new JsonArray();
        foreach (var reference in references)
        {
            var relativePath = reference?["path"]?.GetValue<string>()
                ?? throw new InvalidDataException("A diagram reference has no path.");
            if (projectDirectory is null)
            {
                throw new InvalidDataException("The project references diagram files, but its location is unknown.");
            }

            // Only files inside the project's folder: an absolute, UNC ("\\server\share") or "../" path in a project
            // from elsewhere could read any file of the user, or make Windows connect to another machine and sign in.
            if (Path.IsPathRooted(relativePath))
            {
                throw new InvalidDataException($"The diagram file '{relativePath}' is not inside the project's folder.");
            }

            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectDirectory)) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(folder, relativePath));
            if (!path.StartsWith(folder,OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"The diagram file '{relativePath}' is not inside the project's folder.");
            }

            try
            {
                diagrams.Add(JsonNode.Parse(File.ReadAllText(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                throw new InvalidDataException($"The diagram file '{relativePath}' could not be read: {ex.Message}", ex);
            }
        }

        root["diagrams"] = diagrams;
    }
}
