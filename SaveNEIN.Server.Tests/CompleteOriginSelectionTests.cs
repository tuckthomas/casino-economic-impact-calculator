using SaveNEIN.Shared.Modeling;

namespace SaveNEIN.Server.Tests;

public class CompleteOriginSelectionTests
{
    [Fact]
    public async Task LoadsOriginsBeyondApiPageLimit()
    {
        var ids = Enumerable.Range(0, 10_085).Select(index => $"zcta-{index}").ToArray();
        var skips = new List<int>();
        var selected = await CompleteOriginSelection.LoadAsync((skip, _) =>
        {
            skips.Add(skip);
            return Task.FromResult(new OriginSelectionPage(ids.Length, ids.Skip(skip).Take(10_000).ToArray()));
        });
        Assert.Equal(ids, selected);
        Assert.Equal(new[] { 0, 10_000 }, skips);
    }

    [Fact]
    public async Task RejectsEarlyEndInsteadOfReturningPartialStudy()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => CompleteOriginSelection.LoadAsync((skip, _) =>
            Task.FromResult(new OriginSelectionPage(2, skip == 0 ? ["first"] : []))));
    }

    [Fact]
    public async Task RejectsRepeatedOriginAcrossPages()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => CompleteOriginSelection.LoadAsync((_, _) =>
            Task.FromResult(new OriginSelectionPage(2, ["first"]))));
    }

    [Fact]
    public async Task RejectsChangedSelectionCountDuringLoading()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => CompleteOriginSelection.LoadAsync((skip, _) =>
            Task.FromResult(new OriginSelectionPage(skip == 0 ? 2 : 3, [skip == 0 ? "first" : "second"]))));
    }
}
