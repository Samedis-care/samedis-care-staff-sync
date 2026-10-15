using FluentAssertions;
using SamedisCare.Api.Http;
using SamedisCare.Api.Lookup;
using SamedisCare.Api.V4.Public;
using SamedisCare.Helper.Logging;
using Xunit;

namespace SamedisStaffSync.Tests;

/// <summary>
/// Answers every lookup from a fixed set of titles and records every write. The department
/// "Radiologie" exists, everything else is a miss.
/// </summary>
internal sealed class FakeTenant : IApiClient
{
    public FakeTenant(bool testMode) => TestMode = testMode;

    public bool TestMode { get; }
    public int StatusCode { get; private set; }
    public string LastContent { get; private set; } = string.Empty;
    public string LastError => string.Empty;

    public List<string> Writes { get; } = new();

    public string Get(string resource)
    {
        StatusCode = 200;
        LastContent = resource.StartsWith("departments") && resource.Contains("Radiologie")
            ? "{\"data\":[{\"id\":\"d-1\",\"attributes\":{}}],\"meta\":{\"total\":1}}"
            : "{\"data\":[],\"meta\":{\"total\":0}}";
        return LastContent;
    }

    public string Post(string resource, string content) => Write($"POST {resource}");
    public string Put(string resource, string id, string content) => Write($"PUT {resource}/{id}");
    public string PostDocument(string r, string f, string n) => Write($"POST {r}");

    private string Write(string what)
    {
        Writes.Add(what);
        StatusCode = 201;
        LastContent = "{\"data\":{\"id\":\"new-1\"}}";
        return LastContent;
    }
}

internal sealed class ListLog : ISyncLog
{
    public int Level => 2;
    public List<string> Lines { get; } = new();
    public void Info(string message) => Lines.Add(message);
    public void Warn(string message) => Lines.Add(message);
    public void Error(string message, Exception? ex = null) => Lines.Add(message);
    public void Debug(string message) => Lines.Add(message);
}

public class ResolveOrgIdsTests
{
    private static readonly UniqueOrgData OrgData = new()
    {
        Positions = new List<string> { "Pflegekraft" },
        Departments = new Dictionary<string, DepartmentInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["RAD"] = new DepartmentInfo { Key = "RAD", Title = "Radiologie", CostCenter = "4711" },
            ["NEU"] = new DepartmentInfo { Key = "NEU", Title = "Neurologie" }
        }
    };

    private static (FakeTenant Tenant, ListLog Log, Dictionary<string, string> Positions,
                    Dictionary<string, string> Departments) Resolve(bool testMode)
    {
        var tenant = new FakeTenant(testMode);
        var log = new ListLog();
        var positions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var departments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        OrgDataHelper.ResolveOrgIds(tenant, new ResourceLookup(tenant, "positions"),
                                    new ResourceLookup(tenant, "departments"), OrgData,
                                    createPositions: true, createDepartments: true, log,
                                    positions, departments);
        return (tenant, log, positions, departments);
    }

    // #2869: a dry run created missing positions and PUT every matched department, which
    // also reactivated departments someone had set inactive.
    [Fact]
    public void A_dry_run_writes_nothing_to_the_tenant()
        => Resolve(testMode: true).Tenant.Writes.Should().BeEmpty();

    [Fact]
    public void A_dry_run_still_resolves_what_exists()
    {
        var (_, _, positions, departments) = Resolve(testMode: true);

        departments.Should().Equal(new Dictionary<string, string> { ["RAD"] = "d-1" });
        positions.Should().BeEmpty();
    }

    [Fact]
    public void A_dry_run_says_what_a_live_run_would_write()
        => Resolve(testMode: true).Log.Lines.Should().BeEquivalentTo(
               "Test mode: would create position 'Pflegekraft'.",
               "Test mode: would update department 'Radiologie' (active, cost center).",
               "Test mode: would create department 'Neurologie'.");

    [Fact]
    public void A_live_run_creates_and_updates()
        => Resolve(testMode: false).Tenant.Writes.Should().BeEquivalentTo(
               "POST positions", "PUT departments/d-1", "POST departments");
}
