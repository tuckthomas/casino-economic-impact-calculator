// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public sealed class IndianaTribalGamingFacilityProviderTests
{
    [Fact]
    public void PublishedEvidenceAndFrozenRow_PreserveReviewedFacilityScale()
    {
        IndianaTribalGamingFacilityInventoryProvider.ValidatePublishedEvidence(
            "Four Winds South Bend over 1,900 slots 27 table games 12 table Live Poker 175,000 square feet six restaurants",
            "317 rooms and a ballroom with seating for 800",
            "3000 Prairie Avenue, South Bend, IN 46614");

        var verifiedAt = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
        var row = IndianaTribalGamingFacilityInventoryProvider.CreateSouthBendRow(
            verifiedAt,
            new IndianaTribalGamingFacilityProviderOptions());

        Assert.Equal("USA-IN-TRIBAL-four-winds-casino-south-bend", row.StableVenueId);
        Assert.Equal("Pokagon Band of Potawatomi Indians", row.TribalNationName);
        Assert.Equal("tribal-class-iii-casino", row.FacilityRegime);
        Assert.Equal(1_900, row.SlotOrVltPositions);
        Assert.Equal(27, row.TableGameCount);
        Assert.Equal(12, row.PokerTableCount);
        Assert.Equal(317, row.HotelRoomCount);
        Assert.Equal(800, row.EventCapacity);
        Assert.True(row.IsBorderMarket);
        Assert.Equal(verifiedAt, row.LastVerifiedAt);
    }

    [Fact]
    public void PublishedEvidence_RejectsMissingMaterialAttribute()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            IndianaTribalGamingFacilityInventoryProvider.ValidatePublishedEvidence(
                "Four Winds South Bend over 1,900 slots 27 table games 175,000 square feet six restaurants",
                "317 rooms and a ballroom with seating for 800",
                "3000 Prairie Avenue, South Bend, IN 46614"));

        Assert.Contains("poker-table inventory", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
