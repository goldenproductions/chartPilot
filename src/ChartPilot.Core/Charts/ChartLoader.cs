using ChartPilot.Core.Manifests;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Charts;

/// <summary>
/// Reads a chart directory into a <see cref="ChartModel"/> without shelling out to Helm.
/// Strictly read-only: nothing is ever written to the chart directory.
/// </summary>
public sealed class ChartLoader : IChartLoader
{
    private const string ChartFileName = "Chart.yaml";
    private const string AlternateChartFileName = "Chart.yml";
    private const string SchemaFileName = "values.schema.json";
    private const string DefaultValuesFileName = "values.yaml";
    private const string AlternateDefaultValuesFileName = "values.yml";
    private const string TemplatesDirectoryName = "templates";
    private const string EnvironmentPrefix = "values-";

    private static readonly string[] SuppressionFileNames = [".chartpilot.yaml", ".chartpilot.yml"];
    private static readonly string[] TemplateExtensions = [".yaml", ".yml", ".tpl"];

    /// <inheritdoc />
    public bool IsChartDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return Directory.Exists(path) && FindChartFile(path) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    /// <exception cref="ChartLoadException">
    /// The directory does not exist, has no Chart.yaml, or Chart.yaml cannot be read or parsed.
    /// </exception>
    public ChartModel Load(string chartDirectory)
    {
        if (string.IsNullOrWhiteSpace(chartDirectory))
        {
            throw new ChartLoadException("A chart directory must be supplied.", chartDirectory);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(chartDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ChartLoadException($"The path {Quote(chartDirectory)} is not usable.", chartDirectory, ex);
        }

        if (!Directory.Exists(fullPath))
        {
            throw new ChartLoadException($"The chart directory {Quote(fullPath)} does not exist.", fullPath);
        }

        var chartFile = FindChartFile(fullPath)
                        ?? throw new ChartLoadException(
                            $"{Quote(fullPath)} is not a Helm chart: no {ChartFileName} was found.", fullPath);

        var metadata = ReadChartMetadata(chartFile, fullPath);

        var name = ManifestNavigator.GetString(metadata, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            name = new DirectoryInfo(fullPath).Name;
        }

        var version = ManifestNavigator.GetString(metadata, "version") ?? "0.0.0";

        var schemaJson = TryReadText(Path.Combine(fullPath, SchemaFileName));
        var templates = ReadTemplates(fullPath);

        var detectedKinds = templates
            .SelectMany(t => t.DetectedKinds)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        return new ChartModel(
            ChartPath: fullPath,
            Name: name,
            Version: version,
            AppVersion: ManifestNavigator.GetString(metadata, "appVersion"),
            Description: ManifestNavigator.GetString(metadata, "description"),
            Type: ManifestNavigator.GetString(metadata, "type"),
            KubeVersion: ManifestNavigator.GetString(metadata, "kubeVersion"),
            Maintainers: ReadMaintainers(metadata),
            Dependencies: ReadDependencies(metadata),
            ValuesFiles: ReadValuesFiles(fullPath),
            HasValuesSchema: schemaJson is not null,
            ValuesSchemaJson: schemaJson,
            Templates: templates,
            DetectedKinds: detectedKinds,
            HasSuppressionsFile: SuppressionFileNames.Any(f => File.Exists(Path.Combine(fullPath, f))));
    }

    private static string Quote(string value) => "'" + value + "'";

    private static string? FindChartFile(string chartDirectory)
    {
        var primary = Path.Combine(chartDirectory, ChartFileName);
        if (File.Exists(primary))
        {
            return primary;
        }

        var alternate = Path.Combine(chartDirectory, AlternateChartFileName);
        return File.Exists(alternate) ? alternate : null;
    }

    private static YamlMappingNode ReadChartMetadata(string chartFile, string chartDirectory)
    {
        string text;
        try
        {
            text = File.ReadAllText(chartFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ChartLoadException($"{Quote(chartFile)} could not be read: {ex.Message}", chartDirectory, ex);
        }

        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(text);
            stream.Load(reader);
        }
        catch (YamlException ex)
        {
            throw new ChartLoadException(
                $"{Quote(chartFile)} is not valid YAML (line {ex.Start.Line}, column {ex.Start.Column}): {ex.Message}",
                chartDirectory,
                ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            // YamlDotNet's scanner does not always wrap malformed input in a YamlException
            // (an unterminated flow sequence, for example, surfaces as InvalidOperationException).
            throw new ChartLoadException(
                $"{Quote(chartFile)} is not valid YAML: {ex.Message}",
                chartDirectory,
                ex);
        }

        if (stream.Documents.Count == 0)
        {
            throw new ChartLoadException($"{Quote(chartFile)} is empty.", chartDirectory);
        }

        if (stream.Documents[0].RootNode is not YamlMappingNode mapping)
        {
            throw new ChartLoadException($"The root of {Quote(chartFile)} must be a YAML mapping.", chartDirectory);
        }

        return mapping;
    }

    private static IReadOnlyList<ChartMaintainer> ReadMaintainers(YamlMappingNode metadata)
    {
        var maintainers = new List<ChartMaintainer>();

        foreach (var entry in ManifestNavigator.GetSequence(metadata, "maintainers"))
        {
            if (entry is not YamlMappingNode mapping)
            {
                continue;
            }

            var name = ManifestNavigator.GetString(mapping, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            maintainers.Add(new ChartMaintainer(
                name,
                ManifestNavigator.GetString(mapping, "email"),
                ManifestNavigator.GetString(mapping, "url")));
        }

        return maintainers;
    }

    private static IReadOnlyList<ChartDependency> ReadDependencies(YamlMappingNode metadata)
    {
        var dependencies = new List<ChartDependency>();

        foreach (var entry in ManifestNavigator.GetSequence(metadata, "dependencies"))
        {
            if (entry is not YamlMappingNode mapping)
            {
                continue;
            }

            var name = ManifestNavigator.GetString(mapping, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var tags = ManifestNavigator.GetSequence(mapping, "tags")
                .OfType<YamlScalarNode>()
                .Select(s => s.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToArray();

            dependencies.Add(new ChartDependency(
                name,
                ManifestNavigator.GetString(mapping, "version"),
                ManifestNavigator.GetString(mapping, "repository"),
                ManifestNavigator.GetString(mapping, "condition"),
                tags));
        }

        return dependencies;
    }

    private static IReadOnlyList<ValuesFileInfo> ReadValuesFiles(string chartDirectory)
    {
        var files = new List<ValuesFileInfo>();

        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(chartDirectory, "values*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return files;
        }

        foreach (var path in candidates)
        {
            var fileName = Path.GetFileName(path);
            var extension = Path.GetExtension(fileName);

            if (!extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (fileName.Equals(DefaultValuesFileName, StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals(AlternateDefaultValuesFileName, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(new ValuesFileInfo(fileName, path, null, true));
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(fileName);
            if (!stem.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var environment = stem[EnvironmentPrefix.Length..];
            if (environment.Length == 0)
            {
                continue;
            }

            files.Add(new ValuesFileInfo(fileName, path, environment, false));
        }

        return files
            .OrderByDescending(f => f.IsDefault)
            .ThenBy(f => f.EnvironmentName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<TemplateFileInfo> ReadTemplates(string chartDirectory)
    {
        var templatesDirectory = Path.Combine(chartDirectory, TemplatesDirectoryName);
        if (!Directory.Exists(templatesDirectory))
        {
            return [];
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(templatesDirectory, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var templates = new List<TemplateFileInfo>();

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file);
            if (!TemplateExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            templates.Add(new TemplateFileInfo(
                ToRelativePath(chartDirectory, file),
                TemplateKindScanner.ScanFile(file)));
        }

        return templates
            .OrderBy(t => t.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ToRelativePath(string chartDirectory, string file)
        => Path.GetRelativePath(chartDirectory, file).Replace('\\', '/');

    private static string? TryReadText(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
