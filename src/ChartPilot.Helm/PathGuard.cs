namespace ChartPilot.Helm;

/// <summary>
/// Path normalisation and the containment check used to keep chart and values paths
/// underneath the configured allowlist root.
/// </summary>
public static class PathGuard
{
    private static readonly StringComparison Comparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>Expands a path to a full path without a trailing directory separator.</summary>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path.Trim());
        return TrimSeparator(full);
    }

    /// <summary>Normalises <paramref name="path"/>, resolving it against <paramref name="baseDirectory"/> when relative.</summary>
    public static string NormalizeAgainst(string baseDirectory, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var trimmed = path.Trim();
        var full = Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(baseDirectory, trimmed));
        return TrimSeparator(full);
    }

    /// <summary>True when <paramref name="candidate"/> is the root itself or sits underneath it.</summary>
    public static bool IsUnder(string root, string candidate)
    {
        var normalizedRoot = TrimSeparator(Path.GetFullPath(root));
        var normalizedCandidate = TrimSeparator(Path.GetFullPath(candidate));

        if (normalizedCandidate.Equals(normalizedRoot, Comparison))
        {
            return true;
        }

        var prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(prefix, Comparison);
    }

    /// <summary>
    /// Normalises <paramref name="path"/> and throws when it escapes <paramref name="root"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The path resolves outside the allowlist root.</exception>
    public static string EnsureUnder(string root, string baseDirectory, string path, string label)
    {
        var normalized = NormalizeAgainst(baseDirectory, path);
        if (!IsUnder(root, normalized))
        {
            throw new ArgumentException(
                $"{label} '{normalized}' resolves outside the allowlist root '{TrimSeparator(Path.GetFullPath(root))}'.",
                nameof(path));
        }

        return normalized;
    }

    private static string TrimSeparator(string path)
    {
        if (path.Length <= 1)
        {
            return path;
        }

        // Keep drive and filesystem roots intact ("C:\", "/").
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 || (trimmed.Length == 2 && trimmed[1] == ':') ? path : trimmed;
    }
}
