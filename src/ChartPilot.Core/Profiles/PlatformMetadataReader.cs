using ChartPilot.Core.Values;

namespace ChartPilot.Core.Profiles;

/// <summary>
/// Reads the <c>platform</c> block a chart declares in its values:
/// <code>
/// platform:
///   dataClassification: sensitive-personal-data
///   exposure: internal
/// </code>
/// <para>
/// Values are kebab-case in YAML because that is how the rest of the Kubernetes ecosystem spells
/// enumerations. Anything unrecognised reads as Unclassified/Unknown rather than throwing: a typo
/// must not stop a review, and CP-GOV-004 already reports an undeclared classification.
/// </para>
/// </summary>
public sealed class PlatformMetadataReader : IPlatformMetadataReader
{
    /// <summary>The values path holding the data classification.</summary>
    public const string ClassificationPath = "platform.dataClassification";

    /// <summary>The values path holding the exposure.</summary>
    public const string ExposurePath = "platform.exposure";

    public DataClassification ReadClassification(ValuesDocument values)
        => ParseClassification(values?.GetString(ClassificationPath));

    public Exposure ReadExposure(ValuesDocument values)
        => ParseExposure(values?.GetString(ExposurePath));

    /// <summary>Maps the kebab-case YAML spelling onto <see cref="DataClassification"/>.</summary>
    public static DataClassification ParseClassification(string? raw)
        => Normalize(raw) switch
        {
            "public" => DataClassification.Public,
            "internal" => DataClassification.Internal,
            "confidential" => DataClassification.Confidential,
            "sensitivepersonaldata" => DataClassification.SensitivePersonalData,
            _ => DataClassification.Unclassified
        };

    /// <summary>Maps the kebab-case YAML spelling onto <see cref="Exposure"/>.</summary>
    public static Exposure ParseExposure(string? raw)
        => Normalize(raw) switch
        {
            "internal" => Exposure.Internal,
            "public" => Exposure.Public,
            "external" => Exposure.Public,
            _ => Exposure.Unknown
        };

    /// <summary>The canonical kebab-case spelling of a classification, for round-tripping into values.</summary>
    public static string ToYamlValue(DataClassification classification)
        => classification switch
        {
            DataClassification.Public => "public",
            DataClassification.Internal => "internal",
            DataClassification.Confidential => "confidential",
            DataClassification.SensitivePersonalData => "sensitive-personal-data",
            _ => "unclassified"
        };

    /// <summary>The canonical kebab-case spelling of an exposure.</summary>
    public static string ToYamlValue(Exposure exposure)
        => exposure switch
        {
            Exposure.Internal => "internal",
            Exposure.Public => "public",
            _ => "unknown"
        };

    // "sensitive-personal-data", "sensitive_personal_data" and "SensitivePersonalData" all reduce
    // to the same key, so a chart is not punished for guessing the separator.
    private static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[raw.Length];
        var length = 0;

        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
            }
        }

        return new string(buffer[..length]);
    }
}
