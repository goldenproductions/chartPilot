using System.Reflection;

namespace ChartPilot.Core.Checks;

/// <summary>
/// The rule catalog, built by scanning <see cref="CoreAssemblyMarker.Assembly"/> for
/// <see cref="IResourceCheck"/> implementations and ordering them by id.
/// <para>
/// The scan is what makes adding a rule a one-file change and what makes <c>GET /api/v1/checks</c>
/// free: the catalog is the documentation. Rule ids are unique by construction — a duplicate is a
/// programming error and fails fast at construction time rather than silently shadowing a rule.
/// </para>
/// </summary>
public sealed class CheckCatalog : ICheckCatalog
{
    private readonly Dictionary<string, CheckDescriptor> _byId;

    public CheckCatalog(IEnumerable<IResourceCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        Checks = checks
            .Where(check => check is not null)
            .OrderBy(check => check.Descriptor.Id, StringComparer.Ordinal)
            .ToArray();

        _byId = new Dictionary<string, CheckDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var check in Checks)
        {
            if (!_byId.TryAdd(check.Descriptor.Id, check.Descriptor))
            {
                throw new InvalidOperationException(
                    $"Duplicate check id '{check.Descriptor.Id}'. Rule ids are a permanent contract and must be unique.");
            }
        }

        Descriptors = Checks.Select(check => check.Descriptor).ToArray();
    }

    public IReadOnlyList<CheckDescriptor> Descriptors { get; }

    public IReadOnlyList<IResourceCheck> Checks { get; }

    public CheckDescriptor? Find(string checkId)
        => checkId is not null && _byId.TryGetValue(checkId, out var descriptor) ? descriptor : null;

    /// <summary>
    /// Every concrete <see cref="IResourceCheck"/> in ChartPilot.Core. Used both by the DI
    /// registration and by <see cref="CreateDefault"/>.
    /// </summary>
    public static IReadOnlyList<Type> DiscoverCheckTypes()
        => CoreAssemblyMarker.Assembly
            .GetTypes()
            .Where(type => typeof(IResourceCheck).IsAssignableFrom(type)
                           && type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                           && type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                               binder: null, types: Type.EmptyTypes, modifiers: null) is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The catalog without a DI container. Every rule is parameterless by design, so the CLI, the
    /// tests and the API all end up with exactly the same catalog.
    /// </summary>
    public static CheckCatalog CreateDefault()
        => new(DiscoverCheckTypes().Select(type => (IResourceCheck)Activator.CreateInstance(type, nonPublic: true)!));
}
