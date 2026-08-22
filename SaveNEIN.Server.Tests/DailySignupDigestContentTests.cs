using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Tests;

public class DailySignupDigestContentTests
{
    private static readonly TimeZoneInfo IndianaTime = TimeZoneInfo.FindSystemTimeZoneById("America/Indiana/Indianapolis");

    [Fact]
    public void Build_WhenNoNewRegistrations_ReturnsPlainTextWithoutReports()
    {
        var existing = new[] { CreateSignup("Existing", "Person", new DateTime(2026, 8, 1, 14, 0, 0, DateTimeKind.Utc)) };

        var message = DailySignupDigestContent.Build(new DateOnly(2026, 8, 20), [], existing, IndianaTime);

        Assert.Equal("plaintext", message.MailFormat);
        Assert.Contains("No new registrations were received", message.Content);
        Assert.DoesNotContain("All current registrations", message.Content);
        Assert.DoesNotContain("Existing Person", message.Content);
    }

    [Fact]
    public void Build_WhenNewRegistrationsExist_IncludesNewAndCompleteReports()
    {
        var existing = CreateSignup("Existing", "Person", new DateTime(2026, 8, 1, 14, 0, 0, DateTimeKind.Utc));
        var added = CreateSignup("New", "Person", new DateTime(2026, 8, 20, 15, 0, 0, DateTimeKind.Utc));

        var message = DailySignupDigestContent.Build(new DateOnly(2026, 8, 20), [added], [existing, added], IndianaTime);

        Assert.Equal("html", message.MailFormat);
        Assert.Contains("New registrations for the day", message.Content);
        Assert.Contains("All current registrations", message.Content);
        Assert.Equal(2, CountOccurrences(message.Content, "New Person"));
        Assert.Contains("Existing Person", message.Content);
    }

    [Fact]
    public void Build_EncodesSubmittedValues()
    {
        var signup = CreateSignup("<script>", "Person", new DateTime(2026, 8, 20, 15, 0, 0, DateTimeKind.Utc));

        var message = DailySignupDigestContent.Build(new DateOnly(2026, 8, 20), [signup], [signup], IndianaTime);

        Assert.DoesNotContain("<script>", message.Content);
        Assert.Contains("&lt;script&gt;", message.Content);
    }

    [Theory]
    [InlineData(2026, 1, 15, 5, 5)]
    [InlineData(2026, 8, 15, 4, 4)]
    public void GetUtcPeriod_UsesIndianaEasternOffset(int year, int month, int day, int expectedStartHour, int expectedEndHour)
    {
        var period = DailySignupDigestContent.GetUtcPeriod(new DateOnly(year, month, day), IndianaTime);

        Assert.Equal(expectedStartHour, period.StartUtc.Hour);
        Assert.Equal(expectedEndHour, period.EndUtc.Hour);
        Assert.Equal(TimeSpan.FromDays(1), period.EndUtc - period.StartUtc);
    }

    private static CoalitionSignup CreateSignup(string firstName, string lastName, DateTime createdAtUtc) => new()
    {
        FirstName = firstName,
        LastName = lastName,
        Email = "person@example.com",
        AddressLine1 = "123 Main St",
        City = "Fort Wayne",
        StateProvince = "IN",
        PostalCode = "46802",
        CreatedAtUtc = createdAtUtc,
        DisplayYardSign = true
    };

    private static int CountOccurrences(string value, string search) =>
        (value.Length - value.Replace(search, string.Empty, StringComparison.Ordinal).Length) / search.Length;
}
