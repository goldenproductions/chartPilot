using ChartPilot.Core.Checks;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Values;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Profiles;

/// <summary>Raised when a profile YAML file cannot be turned into a <see cref="Profile"/>.</summary>
public sealed class ProfileParseException : Exception
{
    public ProfileParseException(string message) : base(message)
    {
    }

    public ProfileParseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// The profile catalog: the built-ins, plus anything an organization has added at runtime.
/// <para>
/// Profiles are data. Built-ins are declared in <see cref="BuiltInProfiles"/> so they are compiled,
/// discoverable and testable; <see cref="LoadFromFile"/> reads the same shape from YAML so a
/// platform team can add its own golden path without rebuilding ChartPilot.
/// </para>
/// </summary>
public sealed class ProfileStore : IProfileStore
{
    private readonly Dictionary<string, Profile> _byId;
    private readonly List<Profile> _ordered;

    public ProfileStore() : this(BuiltInProfiles.All)
    {
    }

    public ProfileStore(IEnumerable<Profile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        _ordered = [];
        _byId = new Dictionary<string, Profile>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles)
        {
            Add(profile);
        }

        Default = _byId.TryGetValue(BuiltInProfiles.Default.Id, out var fallback)
            ? fallback
            : _ordered.Count > 0
                ? _ordered[0]
                : BuiltInProfiles.Default;
    }

    public IReadOnlyList<Profile> Profiles => _ordered;

    public Profile Default { get; }

    public Profile? Get(string id)
        => id is not null && _byId.TryGetValue(id.Trim(), out var profile) ? profile : null;

    /// <summary>
    /// Adds or replaces a profile. Replacing is by id, so re-loading an edited file does not
    /// accumulate duplicates in the catalog.
    /// </summary>
    public void Add(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (_byId.TryGetValue(profile.Id, out var existing))
        {
            _ordered[_ordered.IndexOf(existing)] = profile;
        }
        else
        {
            _ordered.Add(profile);
        }

        _byId[profile.Id] = profile;
    }

    /// <summary>
    /// Reads a profile from a YAML file. Anything the file leaves out falls back to the same
    /// defaults the record declares, so a three-line profile is a valid profile.
    /// </summary>
    /// <exception cref="ProfileParseException">The file is missing, unreadable, malformed, or has no id.</exception>
    public Profile LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ProfileParseException("A profile path is required.");
        }

        if (!File.Exists(path))
        {
            throw new ProfileParseException($"Profile file '{path}' does not exist.");
        }

        string yaml;
        try
        {
            yaml = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new ProfileParseException($"Profile file '{path}' could not be read: {ex.Message}", ex);
        }

        return Parse(yaml, Path.GetFileName(path));
    }

    /// <summary>Parses a profile from YAML text. Used by <see cref="LoadFromFile"/> and by the tests.</summary>
    /// <exception cref="ProfileParseException">The YAML is malformed or has no id.</exception>
    public static Profile Parse(string yaml, string sourceName)
    {
        ValuesDocument document;
        try
        {
            document = ValuesDocument.Parse(yaml, sourceName);
        }
        catch (ValuesParseException ex)
        {
            throw new ProfileParseException($"Profile '{sourceName}' is not valid YAML: {ex.Message}", ex);
        }

        var id = document.GetString("id")?.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ProfileParseException($"Profile '{sourceName}' does not declare an id.");
        }

        var fallback = new ProfileRequirements();
        var defaultWeights = new ScoreWeights();
        var defaultDeductions = new SeverityDeductions();

        var requirements = new ProfileRequirements(
            RequireReadinessProbe: Flag("requireReadinessProbe", fallback.RequireReadinessProbe),
            RequireLivenessProbe: Flag("requireLivenessProbe", fallback.RequireLivenessProbe),
            RequireResourceRequests: Flag("requireResourceRequests", fallback.RequireResourceRequests),
            RequireResourceLimits: Flag("requireResourceLimits", fallback.RequireResourceLimits),
            RequireNetworkPolicy: Flag("requireNetworkPolicy", fallback.RequireNetworkPolicy),
            RequirePodDisruptionBudget: Flag("requirePodDisruptionBudget", fallback.RequirePodDisruptionBudget),
            RequireMtls: Flag("requireMtls", fallback.RequireMtls),
            RequireAuthorizationPolicy: Flag("requireAuthorizationPolicy", fallback.RequireAuthorizationPolicy),
            RequireDestinationRule: Flag("requireDestinationRule", fallback.RequireDestinationRule),
            RequireServiceMonitor: Flag("requireServiceMonitor", fallback.RequireServiceMonitor),
            RequireStandardLabels: Flag("requireStandardLabels", fallback.RequireStandardLabels),
            RequireValuesSchema: Flag("requireValuesSchema", fallback.RequireValuesSchema),
            RequirePinnedDependencies: Flag("requirePinnedDependencies", fallback.RequirePinnedDependencies),
            RequireReadOnlyRootFilesystem: Flag("requireReadOnlyRootFilesystem", fallback.RequireReadOnlyRootFilesystem),
            RequireNonRoot: Flag("requireNonRoot", fallback.RequireNonRoot),
            DisallowLatestTag: Flag("disallowLatestTag", fallback.DisallowLatestTag),
            DisallowInlineSecrets: Flag("disallowInlineSecrets", fallback.DisallowInlineSecrets),
            AllowPublicIngress: Flag("allowPublicIngress", fallback.AllowPublicIngress),
            MinReplicas: document.GetInt("requirements.minReplicas") ?? fallback.MinReplicas);

        var weights = new ScoreWeights(
            Security: Weight("security", defaultWeights.Security),
            Reliability: Weight("reliability", defaultWeights.Reliability),
            Operability: Weight("operability", defaultWeights.Operability),
            Governance: Weight("governance", defaultWeights.Governance));

        var deductions = new SeverityDeductions(
            Critical: document.GetInt("deductions.critical") ?? defaultDeductions.Critical,
            Warning: document.GetInt("deductions.warning") ?? defaultDeductions.Warning,
            Info: document.GetInt("deductions.info") ?? defaultDeductions.Info);

        return new Profile(
            id,
            document.GetString("name") ?? id,
            document.GetString("description") ?? string.Empty,
            requirements,
            ParseOverrides(document, sourceName),
            ParseDisabledChecks(document),
            weights,
            deductions);

        bool Flag(string name, bool fallbackValue) => document.GetBool("requirements." + name) ?? fallbackValue;

        double Weight(string name, double fallbackValue)
        {
            var raw = document.GetString("weights." + name);
            return double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallbackValue;
        }
    }

    private static IReadOnlyDictionary<string, Severity> ParseOverrides(ValuesDocument document, string sourceName)
    {
        var overrides = new Dictionary<string, Severity>(StringComparer.OrdinalIgnoreCase);

        if (document.Get("severityOverrides") is not YamlMappingNode mapping)
        {
            return overrides;
        }

        foreach (var entry in mapping.Children)
        {
            if (entry.Key is not YamlScalarNode { Value: { Length: > 0 } checkId }
                || entry.Value is not YamlScalarNode { Value: { Length: > 0 } raw })
            {
                continue;
            }

            overrides[checkId] = raw.Trim().ToLowerInvariant() switch
            {
                "info" => Severity.Info,
                "warning" or "warn" => Severity.Warning,
                "critical" or "error" => Severity.Critical,
                _ => throw new ProfileParseException(
                    $"Profile '{sourceName}' maps {checkId} to unknown severity '{raw}'. Use info, warning or critical.")
            };
        }

        return overrides;
    }

    private static IReadOnlyList<string> ParseDisabledChecks(ValuesDocument document)
        => ManifestNavigator.GetSequence(document.Root, "disabledChecks")
            .OfType<YamlScalarNode>()
            .Select(node => node.Value?.Trim())
            .Where(value => !string.IsNullOrEmpty(value))
            .Select(value => value!)
            .ToArray();
}
