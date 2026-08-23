using System.Globalization;
using System.Net;
using System.Text;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Email;

internal sealed record SignupDigestMessage(string Subject, string Content, string MailFormat);

internal static class DailySignupDigestContent
{
    public static SignupDigestMessage Build(
        DateOnly reportDate,
        IReadOnlyList<CoalitionSignup> newSignups,
        IReadOnlyList<CoalitionSignup> allSignups,
        TimeZoneInfo timeZone)
    {
        var dateText = reportDate.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
        if (newSignups.Count == 0)
        {
            return new SignupDigestMessage(
                $"SaveNEIN registrations — {reportDate:yyyy-MM-dd}",
                $"No new registrations were received on {dateText}.",
                "plaintext");
        }

        var html = new StringBuilder();
        html.Append("<h1>Daily SaveNEIN registration report</h1>")
            .Append("<p><strong>").Append(newSignups.Count).Append(" new registration")
            .Append(newSignups.Count == 1 ? string.Empty : "s")
            .Append(" received on ").Append(Encode(dateText)).Append(".</strong></p>")
            .Append("<h2 style=\"margin-top:32px\">New registrations for the day</h2>");
        AppendSignupReport(html, newSignups, timeZone);

        html.Append("<hr style=\"margin:36px 0;border:0;border-top:2px solid #d7dee8\">")
            .Append("<h2>All current registrations</h2>")
            .Append("<p>").Append(allSignups.Count).Append(" total current registration")
            .Append(allSignups.Count == 1 ? string.Empty : "s").Append(".</p>");
        AppendSignupReport(html, allSignups, timeZone);

        return new SignupDigestMessage(
            $"{newSignups.Count} new SaveNEIN registration{(newSignups.Count == 1 ? string.Empty : "s")} — {reportDate:yyyy-MM-dd}",
            html.ToString(),
            "html");
    }

    internal static (DateTime StartUtc, DateTime EndUtc) GetUtcPeriod(DateOnly reportDate, TimeZoneInfo timeZone)
    {
        var localStart = DateTime.SpecifyKind(reportDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);
        return (TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone), TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone));
    }

    private static string FormatAddress(CoalitionSignup signup)
    {
        var line2 = string.IsNullOrWhiteSpace(signup.AddressLine2) ? string.Empty : $", {signup.AddressLine2.Trim()}";
        return $"{signup.AddressLine1}{line2}, {signup.City}, {signup.StateProvince} {signup.PostalCode}";
    }

    private static string FormatPreferences(CoalitionSignup signup)
    {
        var values = new List<string>();
        if (signup.DisplayYardSign) values.Add("Display a yard sign");
        if (signup.WorkEventBooth) values.Add("Work an event booth");
        if (signup.GoDoorToDoor) values.Add("Go door to door");
        if (signup.WriteLetterToEditor) values.Add("Write a letter to the editor");
        if (signup.ShareSocialMedia) values.Add("Share on social media");
        if (signup.WorkPollingSiteElectionDay) values.Add("Work a polling site on Election Day");
        if (signup.MakePhoneCalls) values.Add("Make phone calls");
        if (signup.BeListedAsSupporter) values.Add("Be listed as a supporter");
        return values.Count == 0 ? "No preferences selected" : string.Join(", ", values);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static void AppendSignupReport(StringBuilder html, IReadOnlyList<CoalitionSignup> signups, TimeZoneInfo timeZone)
    {
        foreach (var signup in signups)
        {
            var createdLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(signup.CreatedAtUtc, DateTimeKind.Utc), timeZone);
            html.Append("<section style=\"margin:18px 0;padding:16px;border:1px solid #d7dee8;border-radius:8px\">")
                .Append("<h3 style=\"margin-top:0\">").Append(Encode($"{signup.FirstName} {signup.LastName}")).Append("</h3>")
                .Append("<p><strong>Email:</strong> ").Append(Encode(signup.Email)).Append("<br>")
                .Append("<strong>Address:</strong> ").Append(Encode(FormatAddress(signup))).Append("<br>")
                .Append("<strong>Registered:</strong> ").Append(Encode(createdLocal.ToString("MMM d, yyyy 'at' h:mm tt zzz", CultureInfo.InvariantCulture))).Append("</p>")
                .Append("<p><strong>Ways they want to help:</strong> ").Append(Encode(FormatPreferences(signup))).Append("</p>")
                .Append("</section>");
        }
    }
}
