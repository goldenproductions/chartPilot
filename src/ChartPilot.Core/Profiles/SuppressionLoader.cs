using System.Globalization;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Values;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Profiles;

/// <summary>An entry in <c>.chartpilot.yaml</c> that could not be accepted, and why.</summary>
/// <param name="Index">The zero-based position in the <c>suppress</c> list, so a user can find it.</param>
public sealed record RejectedSuppression(int Index, string? Id, string Problem);

/// <summary>Everything <c>.chartpilot.yaml</c> yielded: the usable entries and the rejected ones.</summary>
public sealed record SuppressionLoadResult(
    IReadOnlyList<Suppression> Suppressions,
    IReadOnlyList<RejectedSuppression> Rejected)
{
    public static readonly SuppressionLoadResult Empty = new([], []);
}

/// <summary>
/// Reads the optional <c>.chartpilot.yaml</c> next to a chart.
/// <para>
/// A reason is mandatory: an exception nobody had to justify is indistinguishable from an oversight,
/// and it is what turns a policy tool into one teams route around. An entry without one is rejected
/// rather than honoured, so the finding it tried to waive comes back.
/// </para>
/// </summary>
public sealed class SuppressionLoader : ISuppressionLoader
{
    /// <summary>The file name ChartPilot looks for in the chart directory.</summary>
    public const string FileName = ".chartpilot.yaml";

    /// <summary>Accepted alternative extension, so a chart may use either spelling.</summary>
    public const string AlternateFileName = ".chartpilot.yml";

    public IReadOnlyList<Suppression> Load(string chartDirectory) => LoadDetailed(chartDirectory).Suppressions;

    /// <summary>
    /// Loads the file and keeps the rejected entries, so the CLI and the review report can explain
    /// why a suppression the author wrote did not take effect.
    /// </summary>
    public SuppressionLoadResult LoadDetailed(string chartDirectory)
    {
        var path = ResolvePath(chartDirectory);
        if (path is null)
        {
            return SuppressionLoadResult.Empty;
        }

        string yaml;
        try
        {
            yaml = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return SuppressionLoadResult.Empty;
        }

        return Parse(yaml, Path.GetFileName(path));
    }

    /// <summary>The full path of the suppression file for a chart directory, or null when there is none.</summary>
    public static string? ResolvePath(string chartDirectory)
    {
        if (string.IsNullOrWhiteSpace(chartDirectory) || !Directory.Exists(chartDirectory))
        {
            return null;
        }

        foreach (var candidate in new[] { FileName, AlternateFileName })
        {
            var path = Path.Combine(chartDirectory, candidate);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>Parses the suppression file content. Malformed YAML yields nothing rather than throwing.</summary>
    public static SuppressionLoadResult Parse(string yaml, string sourceName)
    {
        ValuesDocument document;
        try
        {
            document = ValuesDocument.Parse(yaml, sourceName);
        }
        catch (ValuesParseException)
        {
            return SuppressionLoadResult.Empty;
        }

        var entries = ManifestNavigator.GetSequence(document.Root, "suppress");
        if (entries.Count == 0)
        {
            entries = ManifestNavigator.GetSequence(document.Root, "suppressions");
        }

        var accepted = new List<Suppression>();
        var rejected = new List<RejectedSuppression>();

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i] is not YamlMappingNode entry)
            {
                rejected.Add(new RejectedSuppression(i, null, "is not a mapping"));
                continue;
            }

            var id = ManifestNavigator.GetString(entry, "id")?.Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                rejected.Add(new RejectedSuppression(i, null, "has no check id"));
                continue;
            }

            var reason = ManifestNavigator.GetString(entry, "reason")?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                rejected.Add(new RejectedSuppression(i, id, "has no reason"));
                continue;
            }

            var rawExpiry = ManifestNavigator.GetString(entry, "expires")?.Trim();
            DateOnly? expires = null;

            if (!string.IsNullOrWhiteSpace(rawExpiry))
            {
                if (!DateOnly.TryParseExact(rawExpiry, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsed))
                {
                    rejected.Add(new RejectedSuppression(i, id, $"has an unparseable expiry '{rawExpiry}' (expected yyyy-MM-dd)"));
                    continue;
                }

                expires = parsed;
            }

            var resource = ManifestNavigator.GetString(entry, "resource")?.Trim();

            accepted.Add(new Suppression(
                id,
                string.IsNullOrWhiteSpace(resource) ? null : resource,
                reason,
                expires));
        }

        return new SuppressionLoadResult(accepted, rejected);
    }
}
