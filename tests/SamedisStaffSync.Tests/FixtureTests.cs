using System.Data;
using FluentAssertions;
using SamedisCare.Helper;
using SamedisCare.Helper.Text;
using Xunit;

namespace SamedisStaffSync.Tests;

/// <summary>
/// The employee import under tests/fixtures/import, read through the same path the tool uses.
/// These tests are about the fixture: that it stays readable, that every reference resolves,
/// and that the awkward rows survive. A fixture that quietly drifts is worse than none,
/// because everything built on it still passes.
/// <para>
/// It is deliberately tied to the external-sync fixtures: the department titles are the ones
/// that import creates, so the employees attach to existing departments rather than to a
/// second set. Run that import first.
/// </para>
/// </summary>
public class FixtureTests
{
    private static DataTable Read()
        => Csv.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "import", "test_daten.csv"),
                    hasHeader: true, delimiter: ",", tableName: "Staff", trimFields: false);

    private static IEnumerable<DataRow> Rows() => Read().AsEnumerable();

    /// <summary>Whether the tool would accept this date. Wrapped so the assertions below can
    /// use it in an expression tree, which cannot contain a discard.</summary>
    private static bool Parses(string? value) => Dates.TryParseGeneralizedTime(value, out _);

    [Fact]
    public void The_file_holds_thirty_employees()
        => Read().Rows.Count.Should().Be(30);

    // Without these the tool aborts with "Invalid Column mapping, stopping import."
    [Fact]
    public void The_mandatory_columns_are_present()
        => Csv.HasColumns(Read(),
                          new[] { "Vorname", "Nachname", "Mitarbeiternr.", "Beitritt am", "Austritt am" })
              .Should().BeTrue();

    [Fact]
    public void The_optional_columns_the_tool_reads_are_present()
        => Csv.HasColumns(Read(),
                          new[] { "E-Mail", "Titel", "Bemerkungen", "Handynummer",
                                  "Positionen", "Abteilungen", "Abteilungstext", "Kostenstelle" })
              .Should().BeTrue();

    // A row without an employee number is skipped, so every row must carry a unique one.
    [Fact]
    public void Every_employee_has_a_unique_number()
    {
        var numbers = Rows().Select(r => r["Mitarbeiternr."].ToString()).ToList();

        numbers.Should().OnlyHaveUniqueItems();
        numbers.Should().OnlyContain(n => !string.IsNullOrWhiteSpace(n));
    }

    // A row whose join date does not parse is skipped entirely.
    [Fact]
    public void Every_join_date_parses()
        => Rows().Select(r => r["Beitritt am"].ToString())
                 .Should().OnlyContain(d => Parses(d));

    [Fact]
    public void A_leaving_date_is_either_empty_or_parses()
        => Rows().Select(r => r["Austritt am"].ToString())
                 .Should().OnlyContain(d => string.IsNullOrEmpty(d) || Parses(d));

    // Someone still employed leaves the field blank -- a date far in the future is rejected
    // by the tool with its own message, so it is not used here.
    [Fact]
    public void Two_employees_have_left_and_the_rest_are_still_there()
        => Rows().Count(r => !string.IsNullOrEmpty(r["Austritt am"].ToString())).Should().Be(2);

    [Fact]
    public void A_leaving_date_never_precedes_the_join_date()
    {
        foreach (var row in Rows().Where(r => !string.IsNullOrEmpty(r["Austritt am"].ToString())))
        {
            Dates.TryParseGeneralizedTime(row["Beitritt am"].ToString(), out var joined).Should().BeTrue();
            Dates.TryParseGeneralizedTime(row["Austritt am"].ToString(), out var left).Should().BeTrue();
            left.Should().BeAfter(joined);
        }
    }

    // --- positions ---------------------------------------------------------------------

    [Fact]
    public void The_five_positions_are_all_used()
        => Rows().Select(r => r["Positionen"].ToString()).Distinct()
                 .Should().BeEquivalentTo(new[] { "Applikationsbetreuer", "MPB", "MPV", "IT",
                                                  "Medizintechnik" });

    [Fact]
    public void Every_employee_holds_exactly_one_position()
        => Rows().Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r["Positionen"].ToString()));

    // --- departments -------------------------------------------------------------------

    // These are the titles external-sync creates. If they drift apart, staff-sync creates a
    // second set of departments instead of attaching to the existing ones -- which looks like
    // a successful run and is exactly the kind of thing nobody notices.
    [Fact]
    public void The_department_titles_are_the_ones_external_sync_imports()
        => Rows().Select(r => r["Abteilungstext"].ToString()).Distinct()
                 .Should().BeEquivalentTo(new[] { "Innere Medizin", "Kardiologie", "Chirurgie",
                                                  "Unfallchirurgie", "Radiologie",
                                                  "Nuklearmedizin", "Anaesthesie", "Zentrallabor" });

    // The department is looked up by its key, and the key is what the row carries in
    // `Abteilungen`; the title comes from `Abteilungstext`. A key used for two titles would
    // make the assignment depend on row order.
    [Fact]
    public void Each_department_key_stands_for_exactly_one_title()
        => Rows().GroupBy(r => r["Abteilungen"].ToString())
                 .Should().OnlyContain(g => g.Select(r => r["Abteilungstext"].ToString())
                                             .Distinct().Count() == 1);

    [Fact]
    public void The_department_key_matches_the_cost_centre()
        => Rows().Should().OnlyContain(r => r["Abteilungen"].ToString() == r["Kostenstelle"].ToString());

    [Fact]
    public void Every_employee_belongs_to_a_department()
        => Rows().Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r["Abteilungen"].ToString()));

    // --- the deliberately awkward rows --------------------------------------------------

    [Fact]
    public void The_awkward_rows_are_still_there()
    {
        var rows = Rows().ToList();

        rows.Should().Contain(r => r["Vorname"].ToString()!.StartsWith(" "),
                              "a padded value must survive into the fixture");
        rows.Should().Contain(r => string.IsNullOrEmpty(r["E-Mail"].ToString()),
                              "an employee without an email address");
        rows.Should().Contain(r => string.IsNullOrEmpty(r["Handynummer"].ToString()),
                              "an employee without a mobile number");
        rows.Should().Contain(r => r["Titel"].ToString() == "Dr. med. dent.",
                              "a longer academic title");
    }

    [Fact]
    public void Email_addresses_use_a_reserved_example_domain()
        => Rows().Select(r => r["E-Mail"].ToString())
                 .Where(e => !string.IsNullOrEmpty(e))
                 .Should().OnlyContain(e => e!.EndsWith("@musterklinik.example"));

    // Live names must never end up in the repository.
    [Fact]
    public void The_fixture_carries_no_live_names()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures",
                                                 "import", "test_daten.csv"));

        text.Should().Contain("Musterklinik".ToLowerInvariant()).And.Contain("Ahrens");
    }
}
