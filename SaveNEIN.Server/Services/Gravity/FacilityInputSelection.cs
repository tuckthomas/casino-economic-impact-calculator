// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Shared.Modeling;

namespace SaveNEIN.Server.Services.Gravity;

/// <summary>One physical-input catalog for availability, selection and calculation.</summary>
internal static class FacilityInputSelection
{
    internal sealed record Definition(string Key, string Label, string Coefficient, string Reference, string Offset,
        Func<CasinoCompetitor, double?> Incumbent, Func<DevelopmentProgram, double?> Proposed)
    {
        internal string[] ParameterKeys => [Coefficient, Reference, Offset];
    }

    internal static readonly Definition[] Definitions =
    [
        new("gaming-positions", "Gaming positions", "facility.gaming_positions_coefficient", "facility.reference_gaming_positions", "facility.gaming_positions_offset",
            item => GravityModelExecutionService.KnownGamingPositionCount(item), item => item.SlotOrVltPositions + item.TableGameCount),
        new("table-games", "Table games", "facility.table_games_coefficient", "facility.reference_table_games", "facility.table_games_offset",
            item => item.TableGameCount, item => item.TableGameCount),
        new("hotel-rooms", "Hotel rooms", "facility.hotel_rooms_coefficient", "facility.reference_hotel_rooms", "facility.hotel_rooms_offset",
            item => item.HotelRoomCount, item => item.HotelRoomCount),
        new("gaming-floor-square-feet", "Gaming floor area", "facility.gaming_floor_coefficient", "facility.reference_gaming_floor_square_feet", "facility.gaming_floor_square_feet_offset",
            item => item.GamingFloorSquareFeet, item => item.GamingFloorSquareFeet),
        new("food-beverage-venues", "Food and beverage venues", "facility.food_beverage_coefficient", "facility.reference_food_beverage_venues", "facility.food_beverage_venues_offset",
            item => item.FoodBeverageVenueCount, item => item.FoodBeverageVenueCount),
        new("entertainment-capacity", "Entertainment capacity", "facility.entertainment_capacity_coefficient", "facility.reference_entertainment_capacity", "facility.entertainment_capacity_offset",
            item => item.EventCapacity, item => item.EventCapacity),
        new("capital-cost", "Capital cost", "facility.capital_scale_coefficient", "facility.reference_capital_cost", "facility.capital_cost_offset",
            item => item.DevelopmentCost is { } value ? Convert.ToDouble(value) : null, item => item.CapitalCost is { } value ? Convert.ToDouble(value) : null),
        new("highway-access", "Highway access", "facility.highway_access_coefficient", "facility.reference_highway_access", "facility.highway_access_offset",
            item => item.HasInterchangeAccess is { } value ? value ? 1 : 0 : null, _ => null)
    ];

    internal static FacilityInputAvailability[] Availability(DevelopmentProgram? program,
        IReadOnlyCollection<CasinoCompetitor> competitors, string attractionSpecification)
    {
        if (attractionSpecification is not (FacilityAttractionSpecifications.Structural or
            FacilityAttractionSpecifications.ObservedGgr or FacilityAttractionSpecifications.HybridObservedGgr))
            throw new ArgumentException("Unsupported attraction specification.");
        // Observed-GGR incumbents do not use physical attraction. Hybrid mode
        // requires a common complete physical field for its potential fallbacks.
        var required = attractionSpecification == FacilityAttractionSpecifications.ObservedGgr ? [] : competitors;
        return Definitions.Select(definition =>
        {
            var missing = required.Where(item => !Usable(definition.Incumbent(item)))
                .Select(item => item.StableVenueId).ToList();
            if (program is not null && !Usable(definition.Proposed(program))) missing.Insert(0, "proposed-facility");
            return new FacilityInputAvailability(definition.Key, definition.Label, definition.Coefficient,
                definition.ParameterKeys, missing.Count == 0, missing.ToArray(), missing.Count == 0 ? null :
                    $"Unavailable: missing {definition.Label.ToLowerInvariant()} for {missing.Count} required facility/facilities. No reference value will be substituted.");
        }).ToArray();
    }

    internal static string[] Resolve(IReadOnlyCollection<string>? selected, FacilityInputAvailability[] availability)
    {
        var keys = selected?.ToArray() ?? availability.Where(item => item.Available).Select(item => item.Key).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ArgumentException("Facility input selections must not contain duplicates.");
        foreach (var key in keys)
        {
            var item = availability.SingleOrDefault(item => item.Key == key)
                ?? throw new ArgumentException($"Unknown facility input '{key}'.");
            if (!item.Available) throw new ArgumentException($"Input '{item.DisplayName}' cannot be selected. {item.UnavailableReason}");
        }
        return keys.Order(StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyCollection<FacilityFeatureTerm> Features(CasinoCompetitor item, IReadOnlyDictionary<string, double> parameters) =>
        Definitions.Select(definition => Term(definition, definition.Incumbent(item), parameters)).ToArray();

    internal static IReadOnlyCollection<FacilityFeatureTerm> Features(DevelopmentProgram item, IReadOnlyDictionary<string, double> parameters) =>
        Definitions.Select(definition => Term(definition, definition.Proposed(item), parameters)).ToArray();

    internal static FacilityFeatureTerm[] ActiveTerms(IReadOnlyCollection<FacilityFeatureTerm> terms, IReadOnlyCollection<string> selected)
    {
        var active = terms.Where(term => selected.Contains(term.Key, StringComparer.Ordinal) && term.Coefficient != 0).ToArray();
        return active;
    }

    internal static double Calculate(IFacilityAttractivenessService service, string key, IReadOnlyCollection<FacilityFeatureTerm> active) =>
        active.Count == 0 ? 1d : service.CalculateStructural(new(key, active, MissingFacilityAttributeBehavior.Reject)).NormalizedAttraction;

    private static bool Usable(double? value) => value is { } number && double.IsFinite(number) && number >= 0;
    private static FacilityFeatureTerm Term(Definition definition, double? value, IReadOnlyDictionary<string, double> parameters) =>
        new(definition.Key, value, parameters[definition.Reference], parameters[definition.Coefficient], parameters[definition.Offset]);
}
