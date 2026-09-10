using System.Globalization;
using FluentAssertions;
using SamedisStaffSync;
using Xunit;

namespace SamedisStaffSync.Tests;

/// <summary>
/// The join and leaving dates go straight onto live staff records, so what the host's locale
/// does to them matters. This used to format with the ambient culture and then re-parse its
/// own output with <c>Convert.ToDateTime</c>: on en-US, or in a container with no locale, the
/// first row carrying a leaving date threw an uncaught FormatException and aborted the import
/// mid-run; on th-TH the year written to the API was Buddhist. See samedis-care-issues#2886.
/// </summary>
public class StaffDateTests
{
    // "" stands for the invariant culture -- a Linux container with no locale set, which is
    // how the on-prem installs run when nobody configured one.
    private static readonly string[] HostCultures = { "de-DE", "en-US", "th-TH", "ar-SA", "" };

    private static T UnderCulture<T>(string cultureName, Func<T> body)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = cultureName.Length == 0
                ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo(cultureName);
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void A_leaving_date_is_accepted_on_every_host()
    {
        foreach (var culture in HostCultures)
        {
            var verdict = UnderCulture(culture, () =>
            {
                var v = Helper.PrepareStaffDates("05.03.2026", "30.04.2026", out var join, out var left);
                return (v, join, left);
            });

            verdict.v.Should().Be(Helper.StaffDateVerdict.Ok, $"under '{culture}'");
            verdict.join.Should().Be("05.03.2026", $"the API must get a Gregorian date under '{culture}'");
            verdict.left.Should().Be("30.04.2026", $"the API must get a Gregorian date under '{culture}'");
        }
    }

    // The th-TH/ar-SA half of the defect: the comparison succeeded, so nothing failed -- the
    // year on the record was simply wrong.
    [Fact]
    public void No_host_calendar_leaks_into_the_value_sent_to_the_api()
    {
        foreach (var culture in HostCultures)
        {
            var join = UnderCulture(culture, () =>
            {
                Helper.PrepareStaffDates("05.03.2026", null, out var j, out _);
                return j;
            });

            join.Should().EndWith("2026", $"'{culture}' must not put its own calendar's year on the record");
        }
    }

    [Fact]
    public void A_row_without_a_leaving_date_is_still_employed()
    {
        var verdict = Helper.PrepareStaffDates("05.03.2026", "", out var join, out var left);

        verdict.Should().Be(Helper.StaffDateVerdict.Ok);
        join.Should().Be("05.03.2026");
        left.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("irgendwann")]
    public void A_missing_or_unreadable_join_date_is_rejected(string? rawJoin)
        => Helper.PrepareStaffDates(rawJoin, null, out _, out _)
            .Should().Be(Helper.StaffDateVerdict.JoinMissingOrUnreadable);

    [Fact]
    public void An_unreadable_leaving_date_is_rejected()
        => Helper.PrepareStaffDates("05.03.2026", "irgendwann", out _, out _)
            .Should().Be(Helper.StaffDateVerdict.LeftUnreadable);

    // These two checks are the reason the dates were being re-parsed in the first place.
    [Fact]
    public void A_leaving_date_before_the_join_date_is_rejected_on_every_host()
    {
        foreach (var culture in HostCultures)
            UnderCulture(culture, () => Helper.PrepareStaffDates("30.04.2026", "05.03.2026", out _, out _))
                .Should().Be(Helper.StaffDateVerdict.LeftBeforeJoin, $"under '{culture}'");
    }

    [Fact]
    public void A_leaving_date_far_in_the_future_is_rejected_on_every_host()
    {
        var farOut = DateTime.Now.AddYears(11).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        foreach (var culture in HostCultures)
            UnderCulture(culture, () => Helper.PrepareStaffDates("05.03.2026", farOut, out _, out _))
                .Should().Be(Helper.StaffDateVerdict.LeftTooFarInTheFuture, $"under '{culture}'");
    }

    // The round trip HelperSap and LdapHelper rely on: they write this format into the
    // DataTable, the import reads it back.
    [Fact]
    public void The_format_written_into_the_data_table_reads_back_on_every_host()
    {
        foreach (var culture in HostCultures)
        {
            var written = UnderCulture(culture,
                () => new DateTime(2026, 4, 30).ToString(Helper.StaffDateFormat, CultureInfo.InvariantCulture));

            written.Should().Be("30.04.2026", $"under '{culture}'");

            UnderCulture(culture, () => Helper.PrepareStaffDates("05.03.2026", written, out _, out var left) is Helper.StaffDateVerdict.Ok ? left : "rejected")
                .Should().Be("30.04.2026", $"under '{culture}'");
        }
    }
}
