using Microsoft.Extensions.Logging.Abstractions;
using SaveNEIN.Server.Services;
using Xunit;

namespace SaveNEIN.Server.Tests;

public class IsochroneScheduleTests
{
    [Theory]
    [InlineData("00:59", false)]
    [InlineData("01:00", true)]
    [InlineData("03:30", true)]
    [InlineData("05:00", false)]
    public void IsWithinWindow_OvernightEasternWindow_UsesStartInclusiveEndExclusive(string time, bool expected)
    {
        var result = IsochroneSchedule.IsWithinWindow(
            TimeOnly.Parse(time),
            new TimeOnly(1, 0),
            new TimeOnly(5, 0));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveTimeZone_UnknownId_FallsBackWithoutThrowing()
    {
        var result = IsochroneSchedule.ResolveTimeZone(
            "not-a-real-time-zone",
            NullLogger.Instance);

        Assert.NotNull(result);
    }

    [Fact]
    public void GetRemainingWindow_OvernightWindow_ReturnsUntilNextEnd()
    {
        var result = IsochroneSchedule.GetRemainingWindow(
            new TimeOnly(23, 30),
            new TimeOnly(5, 0));

        Assert.Equal(TimeSpan.FromHours(5.5), result);
    }

    [Fact]
    public void OrderNationwideCountyBatch_PrioritizesIndianaMidwestThenRemainingStates()
    {
        var candidates = new[]
        {
            new SeedCounty("06", "06001", "Alameda"),
            new SeedCounty("19", "19001", "Adair"),
            new SeedCounty("39", "39001", "Adams"),
            new SeedCounty("18", "18001", "Adams")
        };

        var result = IsochroneSeedingService.OrderNationwideCountyBatch(
            candidates,
            countiesPerBatch: 4,
            priorityStateFips: new[] { "18", "39", "19" });

        Assert.Equal(new[] { "18", "39", "19", "06" }, result.Select(county => county.StateFips));
    }
}
