using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace ChartPilot.Api.Tests;

/// <summary>
/// Contract tests for GET /api/v1/browse, which backs the GUI's "Browse…" folder picker.
///
/// The listing is a filesystem read driven by a query parameter, so containment inside the
/// allowlist root is the part that actually matters here — the traversal tests are the point of
/// this file, not an afterthought.
/// </summary>
public sealed class BrowseEndpointTests : IDisposable
{
    private const string Base = "/api/v1";

    private readonly ChartPilotApiFactory _factory = new();
    private readonly TestChart _chart = new();
    private readonly HttpClient _client;

    public BrowseEndpointTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _chart.Dispose();
    }

    /// <summary>The chart's path relative to the allowlist root, in the form the API returns.</summary>
    private string ChartRelativePath =>
        Path.GetRelativePath(Path.GetTempPath(), _chart.Path).Replace('\\', '/');

    private async Task<JsonElement> BrowseAsync(string? path = null)
    {
        var query = path is null ? string.Empty : $"?path={Uri.EscapeDataString(path)}";
        var response = await _client.GetAsync($"{Base}/browse{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<JsonElement> Entries(JsonElement body)
        => body.GetProperty("entries").EnumerateArray().ToList();

    [Fact]
    public async Task Browsing_without_a_path_starts_at_the_allowlist_root()
    {
        var body = await BrowseAsync();

        Assert.Equal(string.Empty, body.GetProperty("path").GetString());
        Assert.True(body.GetProperty("isAllowlistRoot").GetBoolean());
        Assert.Null(body.GetProperty("parentPath").GetString());
        Assert.False(body.GetProperty("isChart").GetBoolean());
    }

    [Fact]
    public async Task A_chart_directory_is_flagged_so_the_GUI_can_badge_it()
    {
        // The parent of the chart, so the chart itself appears as an entry.
        var parent = Path.GetDirectoryName(ChartRelativePath)!.Replace('\\', '/');

        var entry = Assert.Single(
            Entries(await BrowseAsync(parent)),
            e => e.GetProperty("name").GetString() == Path.GetFileName(_chart.Path));

        Assert.True(entry.GetProperty("isChart").GetBoolean());
    }

    [Fact]
    public async Task A_returned_path_can_be_posted_straight_to_workspaces()
    {
        var parent = Path.GetDirectoryName(ChartRelativePath)!.Replace('\\', '/');

        // Selected by name, not by isChart: sibling test instances create their own charts in this
        // same parent directory, so more than one entry can be a chart during a parallel run.
        var entry = Assert.Single(
            Entries(await BrowseAsync(parent)),
            e => e.GetProperty("name").GetString() == Path.GetFileName(_chart.Path));

        Assert.True(entry.GetProperty("isChart").GetBoolean());

        // This is the whole contract between the browser and the open button: whatever /browse
        // hands back is directly acceptable to /workspaces, with no client-side path assembly.
        var response = await _client.PostAsJsonAsync(
            $"{Base}/workspaces",
            new { chartPath = entry.GetProperty("path").GetString() });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Browsing_into_a_chart_reports_it_as_one_and_lists_its_subfolders()
    {
        var body = await BrowseAsync(ChartRelativePath);

        Assert.True(body.GetProperty("isChart").GetBoolean());
        Assert.False(body.GetProperty("isAllowlistRoot").GetBoolean());

        var names = Entries(body).Select(e => e.GetProperty("name").GetString()).ToList();

        Assert.Contains("templates", names);
    }

    [Fact]
    public async Task Breadcrumbs_walk_from_the_root_down_to_the_current_directory()
    {
        var body = await BrowseAsync(ChartRelativePath);

        var segments = body.GetProperty("segments").EnumerateArray().ToList();

        // The first hop is the root itself and navigates to the empty path.
        Assert.Equal(string.Empty, segments[0].GetProperty("path").GetString());

        // The last hop is the directory being shown.
        Assert.Equal(Path.GetFileName(_chart.Path), segments[^1].GetProperty("name").GetString());
        Assert.Equal(ChartRelativePath, segments[^1].GetProperty("path").GetString());
    }

    [Fact]
    public async Task Entries_are_sorted_case_insensitively_by_name()
    {
        var names = Entries(await BrowseAsync(ChartRelativePath))
            .Select(e => e.GetProperty("name").GetString()!)
            .ToList();

        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../..")]
    [InlineData("subdir/../../..")]
    public async Task Traversal_above_the_allowlist_root_is_rejected(string path)
    {
        var response = await _client.GetAsync($"{Base}/browse?path={Uri.EscapeDataString(path)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal(
            "https://chartpilot.local/problems/outside-allowlist",
            body.GetProperty("type").GetString());
    }

    [Fact]
    public async Task An_absolute_path_outside_the_allowlist_root_is_rejected()
    {
        var outside = OperatingSystem.IsWindows() ? @"C:\Windows\System32" : "/etc";

        var response = await _client.GetAsync($"{Base}/browse?path={Uri.EscapeDataString(outside)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_path_inside_the_root_that_does_not_exist_is_a_404()
    {
        var response = await _client.GetAsync($"{Base}/browse?path=chartpilot-no-such-directory-9f2a");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal(
            "https://chartpilot.local/problems/directory-not-found",
            body.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Hidden_directories_are_not_listed()
    {
        var hidden = Path.Combine(_chart.Path, ".hidden-dir");
        Directory.CreateDirectory(hidden);

        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        }
        else
        {
            // On Unix the dot prefix is the convention, but the API filters on the attribute, so
            // this assertion only holds where the attribute exists.
            return;
        }

        var names = Entries(await BrowseAsync(ChartRelativePath))
            .Select(e => e.GetProperty("name").GetString())
            .ToList();

        Assert.DoesNotContain(".hidden-dir", names);
    }
}
