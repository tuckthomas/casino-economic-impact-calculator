// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

namespace SaveNEIN.Server.Services.Gravity;

public enum DemandSpecification
{
    AgiShare,
    EligibleAdultPerCapita
}

public sealed record AgiShareDemandInput(
    string OriginKey,
    double RealIncomeMass,
    double GamingIncomeShare,
    double OriginAdjustment = 1d);

public sealed record PerCapitaDemandInput(
    string OriginKey,
    double EligibleAdults,
    double BaseGamingExpenditurePerAdult,
    double IncomeMetric,
    double RegionalReferenceIncome,
    double IncomeElasticity,
    double MinimumIncomeAdjustment,
    double MaximumIncomeAdjustment);

public sealed record OriginDemandResult(
    string OriginKey,
    DemandSpecification Specification,
    double Demand,
    double IncomeAdjustment,
    bool IncomeAdjustmentWasBounded);

public interface IOriginDemandService
{
    OriginDemandResult CalculateAgiShare(AgiShareDemandInput input);
    OriginDemandResult CalculatePerCapita(PerCapitaDemandInput input);
}

public sealed class OriginDemandService : IOriginDemandService
{
    public OriginDemandResult CalculateAgiShare(AgiShareDemandInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        RequireKey(input.OriginKey, nameof(input.OriginKey));
        RequireNonNegativeFinite(input.RealIncomeMass, nameof(input.RealIncomeMass));
        RequireNonNegativeFinite(input.GamingIncomeShare, nameof(input.GamingIncomeShare));
        RequireNonNegativeFinite(input.OriginAdjustment, nameof(input.OriginAdjustment));

        var demand = input.RealIncomeMass * input.GamingIncomeShare * input.OriginAdjustment;
        RequireFiniteResult(demand, "AGI-share demand");
        return new OriginDemandResult(
            input.OriginKey,
            DemandSpecification.AgiShare,
            demand,
            input.OriginAdjustment,
            false);
    }

    public OriginDemandResult CalculatePerCapita(PerCapitaDemandInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        RequireKey(input.OriginKey, nameof(input.OriginKey));
        RequireNonNegativeFinite(input.EligibleAdults, nameof(input.EligibleAdults));
        RequireNonNegativeFinite(input.BaseGamingExpenditurePerAdult, nameof(input.BaseGamingExpenditurePerAdult));
        RequirePositiveFinite(input.IncomeMetric, nameof(input.IncomeMetric));
        RequirePositiveFinite(input.RegionalReferenceIncome, nameof(input.RegionalReferenceIncome));
        RequireFinite(input.IncomeElasticity, nameof(input.IncomeElasticity));
        RequireNonNegativeFinite(input.MinimumIncomeAdjustment, nameof(input.MinimumIncomeAdjustment));
        RequireNonNegativeFinite(input.MaximumIncomeAdjustment, nameof(input.MaximumIncomeAdjustment));
        if (input.MinimumIncomeAdjustment > input.MaximumIncomeAdjustment)
        {
            throw new ArgumentException("The minimum income adjustment cannot exceed the maximum.", nameof(input));
        }

        var unboundedAdjustment = Math.Pow(
            input.IncomeMetric / input.RegionalReferenceIncome,
            input.IncomeElasticity);
        RequireFiniteResult(unboundedAdjustment, "income adjustment");
        var incomeAdjustment = Math.Clamp(
            unboundedAdjustment,
            input.MinimumIncomeAdjustment,
            input.MaximumIncomeAdjustment);
        var demand = input.EligibleAdults * input.BaseGamingExpenditurePerAdult * incomeAdjustment;
        RequireFiniteResult(demand, "eligible-adult demand");

        return new OriginDemandResult(
            input.OriginKey,
            DemandSpecification.EligibleAdultPerCapita,
            demand,
            incomeAdjustment,
            incomeAdjustment != unboundedAdjustment);
    }

    private static void RequireKey(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty key is required.", parameterName);
        }
    }

    internal static void RequireFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "A finite value is required.");
        }
    }

    internal static void RequireNonNegativeFinite(double value, string parameterName)
    {
        RequireFinite(value, parameterName);
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "A non-negative value is required.");
        }
    }

    internal static void RequirePositiveFinite(double value, string parameterName)
    {
        RequireFinite(value, parameterName);
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "A positive value is required.");
        }
    }

    internal static void RequireFiniteResult(double value, string calculationName)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException($"{calculationName} produced a non-finite result.");
        }
    }
}

public enum MissingFacilityAttributeBehavior
{
    Reject,
    UseReferenceValue
}

public sealed record FacilityFeatureTerm(
    string Key,
    double? Value,
    double ReferenceValue,
    double Coefficient,
    double Offset = 1d);

public sealed record StructuralAttractivenessInput(
    string FacilityKey,
    IReadOnlyCollection<FacilityFeatureTerm> Features,
    MissingFacilityAttributeBehavior MissingAttributeBehavior = MissingFacilityAttributeBehavior.Reject);

public sealed record ObservedGgrAttractivenessInput(
    string FacilityKey,
    double StabilizedObservedGgr,
    double ReferenceObservedGgr,
    bool IsProposedFacility);

public sealed record FacilityAttractivenessResult(
    string FacilityKey,
    double NormalizedAttraction,
    double LogNormalizedAttraction,
    string Specification,
    IReadOnlyList<string> Warnings);

public interface IFacilityAttractivenessService
{
    FacilityAttractivenessResult CalculateStructural(StructuralAttractivenessInput input);
    FacilityAttractivenessResult CalculateObservedGgr(ObservedGgrAttractivenessInput input);
}

public sealed class FacilityAttractivenessService : IFacilityAttractivenessService
{
    public FacilityAttractivenessResult CalculateStructural(StructuralAttractivenessInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        RequireKey(input.FacilityKey, nameof(input.FacilityKey));
        if (input.Features.Count == 0)
        {
            throw new ArgumentException("At least one calibrated facility feature is required.", nameof(input));
        }
        if (input.Features.Select(feature => feature.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Features.Count)
        {
            throw new ArgumentException("Facility feature keys must be unique.", nameof(input));
        }

        var warnings = new List<string>();
        var logAttraction = 0d;
        foreach (var feature in input.Features)
        {
            RequireKey(feature.Key, nameof(feature.Key));
            OriginDemandService.RequireNonNegativeFinite(feature.ReferenceValue, nameof(feature.ReferenceValue));
            OriginDemandService.RequireFinite(feature.Coefficient, nameof(feature.Coefficient));
            OriginDemandService.RequirePositiveFinite(feature.Offset, nameof(feature.Offset));

            var value = feature.Value;
            if (value is null)
            {
                if (input.MissingAttributeBehavior == MissingFacilityAttributeBehavior.Reject)
                {
                    throw new InvalidOperationException(
                        $"Facility '{input.FacilityKey}' is missing required feature '{feature.Key}'.");
                }

                value = feature.ReferenceValue;
                warnings.Add($"Feature '{feature.Key}' was missing and used its reference value.");
            }

            OriginDemandService.RequireNonNegativeFinite(value.Value, feature.Key);
            var normalizedFeature = (value.Value + feature.Offset) / (feature.ReferenceValue + feature.Offset);
            logAttraction += feature.Coefficient * Math.Log(normalizedFeature);
        }

        OriginDemandService.RequireFiniteResult(logAttraction, "structural log-attraction");
        var attraction = Math.Exp(logAttraction);
        OriginDemandService.RequireFiniteResult(attraction, "structural attraction");
        if (attraction <= 0)
        {
            throw new InvalidOperationException("Structural attraction must be positive.");
        }

        return new FacilityAttractivenessResult(
            input.FacilityKey,
            attraction,
            logAttraction,
            "structural-physical-mass",
            warnings);
    }

    public FacilityAttractivenessResult CalculateObservedGgr(ObservedGgrAttractivenessInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        RequireKey(input.FacilityKey, nameof(input.FacilityKey));
        if (input.IsProposedFacility)
        {
            throw new InvalidOperationException(
                "A proposed facility cannot derive competitive mass from the projected GGR being solved for.");
        }

        OriginDemandService.RequirePositiveFinite(input.StabilizedObservedGgr, nameof(input.StabilizedObservedGgr));
        OriginDemandService.RequirePositiveFinite(input.ReferenceObservedGgr, nameof(input.ReferenceObservedGgr));
        var attraction = input.StabilizedObservedGgr / input.ReferenceObservedGgr;
        return new FacilityAttractivenessResult(
            input.FacilityKey,
            attraction,
            Math.Log(attraction),
            "observed-ggr-incumbent-mass",
            Array.Empty<string>());
    }

    private static void RequireKey(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty key is required.", parameterName);
        }
    }
}

public enum TravelFrictionForm
{
    InversePower,
    Exponential
}

public enum MissingRouteBehavior
{
    RejectOrigin,
    ExcludeFacility
}

public static class CaptureSourceCategories
{
    public const string HostJurisdictionIncumbent = "host-jurisdiction-incumbent";
    public const string ExternalCommercialIncumbent = "external-commercial-incumbent";
    public const string TribalOrOtherJurisdictionIncumbent = "tribal-or-other-jurisdiction-incumbent";
    public const string OutsideOption = "outside-option";
    public const string InducedResident = "newly-induced-resident";
    public const string Tourism = "tourism";
    public const string TrafficIntercept = "traffic-intercept";
}

public sealed record GravityParameters(
    double AttractionElasticity,
    TravelFrictionForm FrictionForm,
    double TravelTimeDecay,
    double TravelTimeRegularizationMinutes,
    MissingRouteBehavior MissingRouteBehavior = MissingRouteBehavior.RejectOrigin);

public sealed record GravityAlternativeInput(
    string FacilityKey,
    double Attraction,
    double? NetworkTravelTimeMinutes,
    bool RouteFound,
    double OriginFacilityModifier = 1d,
    string CaptureSourceCategory = CaptureSourceCategories.ExternalCommercialIncumbent,
    bool IsProposedFacility = false);

public sealed record GravityOriginInput(
    string OriginKey,
    double Demand,
    double OutsideOptionWeight,
    IReadOnlyCollection<GravityAlternativeInput> Alternatives);

public sealed record GravityFacilityAllocation(
    string FacilityKey,
    double? NetworkTravelTimeMinutes,
    bool RouteIncluded,
    double? LogWeight,
    double Share,
    double AllocatedDemand,
    string CaptureSourceCategory,
    bool IsProposedFacility);

public sealed record GravityOriginResult(
    string OriginKey,
    double Demand,
    IReadOnlyList<GravityFacilityAllocation> FacilityAllocations,
    double? OutsideOptionLogWeight,
    double OutsideOptionShare,
    double OutsideOptionAllocatedDemand,
    double ShareSum,
    double AllocatedDemandSum);

public interface IGravityModelService
{
    GravityOriginResult Allocate(GravityOriginInput origin, GravityParameters parameters);
}

public sealed class GravityModelService : IGravityModelService
{
    public GravityOriginResult Allocate(GravityOriginInput origin, GravityParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(parameters);
        Validate(origin, parameters);

        var weightedAlternatives = origin.Alternatives
            .Select(alternative => new WeightedAlternative(alternative, CalculateLogWeight(alternative, parameters)))
            .ToArray();
        var outsideLogWeight = origin.OutsideOptionWeight > 0
            ? Math.Log(origin.OutsideOptionWeight)
            : double.NegativeInfinity;
        var finiteLogWeights = weightedAlternatives
            .Where(item => item.LogWeight is not null)
            .Select(item => item.LogWeight!.Value)
            .Append(outsideLogWeight)
            .Where(double.IsFinite)
            .ToArray();
        if (finiteLogWeights.Length == 0)
        {
            throw new InvalidOperationException(
                $"Origin '{origin.OriginKey}' has no reachable positive-weight facility and no outside option.");
        }

        var maximumLogWeight = finiteLogWeights.Max();
        var scaledOutsideWeight = double.IsFinite(outsideLogWeight)
            ? Math.Exp(outsideLogWeight - maximumLogWeight)
            : 0d;
        var scaledFacilityWeights = weightedAlternatives
            .Select(item => item.LogWeight is { } logWeight
                ? Math.Exp(logWeight - maximumLogWeight)
                : 0d)
            .ToArray();
        var denominator = scaledOutsideWeight + scaledFacilityWeights.Sum();
        if (!double.IsFinite(denominator) || denominator <= 0)
        {
            throw new InvalidOperationException("Gravity allocation produced an invalid denominator.");
        }

        var allocations = new List<GravityFacilityAllocation>(weightedAlternatives.Length);
        for (var index = 0; index < weightedAlternatives.Length; index++)
        {
            var weighted = weightedAlternatives[index];
            var share = scaledFacilityWeights[index] / denominator;
            allocations.Add(new GravityFacilityAllocation(
                weighted.Input.FacilityKey,
                weighted.Input.NetworkTravelTimeMinutes,
                weighted.LogWeight is not null,
                weighted.LogWeight,
                share,
                origin.Demand * share,
                weighted.Input.CaptureSourceCategory,
                weighted.Input.IsProposedFacility));
        }

        var outsideShare = scaledOutsideWeight / denominator;
        var shareSum = allocations.Sum(allocation => allocation.Share) + outsideShare;
        var allocatedDemandSum = allocations.Sum(allocation => allocation.AllocatedDemand) +
                                 (origin.Demand * outsideShare);
        OriginDemandService.RequireFiniteResult(shareSum, "gravity share sum");
        OriginDemandService.RequireFiniteResult(allocatedDemandSum, "allocated demand sum");

        return new GravityOriginResult(
            origin.OriginKey,
            origin.Demand,
            allocations,
            double.IsFinite(outsideLogWeight) ? outsideLogWeight : null,
            outsideShare,
            origin.Demand * outsideShare,
            shareSum,
            allocatedDemandSum);
    }

    private static double? CalculateLogWeight(GravityAlternativeInput alternative, GravityParameters parameters)
    {
        if (!alternative.RouteFound || alternative.NetworkTravelTimeMinutes is null)
        {
            if (parameters.MissingRouteBehavior == MissingRouteBehavior.RejectOrigin)
            {
                throw new InvalidOperationException(
                    $"Facility '{alternative.FacilityKey}' is missing a required network route.");
            }

            return null;
        }

        if (alternative.Attraction == 0 || alternative.OriginFacilityModifier == 0)
        {
            return null;
        }

        var logWeight = parameters.AttractionElasticity * Math.Log(alternative.Attraction) +
                        Math.Log(alternative.OriginFacilityModifier);
        logWeight += parameters.FrictionForm switch
        {
            TravelFrictionForm.InversePower =>
                -parameters.TravelTimeDecay * Math.Log(
                    alternative.NetworkTravelTimeMinutes.Value + parameters.TravelTimeRegularizationMinutes),
            TravelFrictionForm.Exponential =>
                -parameters.TravelTimeDecay * alternative.NetworkTravelTimeMinutes.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(parameters), "Unsupported travel-friction form.")
        };
        OriginDemandService.RequireFiniteResult(logWeight, "gravity log-weight");
        return logWeight;
    }

    private static void Validate(GravityOriginInput origin, GravityParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(origin.OriginKey))
        {
            throw new ArgumentException("A non-empty origin key is required.", nameof(origin));
        }
        if (origin.Alternatives.Count == 0)
        {
            throw new ArgumentException("At least one facility alternative is required.", nameof(origin));
        }
        if (origin.Alternatives.Select(item => item.FacilityKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            origin.Alternatives.Count)
        {
            throw new ArgumentException("Facility keys must be unique within an origin.", nameof(origin));
        }

        OriginDemandService.RequireNonNegativeFinite(origin.Demand, nameof(origin.Demand));
        OriginDemandService.RequireNonNegativeFinite(origin.OutsideOptionWeight, nameof(origin.OutsideOptionWeight));
        OriginDemandService.RequireNonNegativeFinite(parameters.AttractionElasticity, nameof(parameters.AttractionElasticity));
        OriginDemandService.RequireNonNegativeFinite(parameters.TravelTimeDecay, nameof(parameters.TravelTimeDecay));
        OriginDemandService.RequirePositiveFinite(
            parameters.TravelTimeRegularizationMinutes,
            nameof(parameters.TravelTimeRegularizationMinutes));

        foreach (var alternative in origin.Alternatives)
        {
            if (string.IsNullOrWhiteSpace(alternative.FacilityKey))
            {
                throw new ArgumentException("A non-empty facility key is required.", nameof(origin));
            }
            if (string.IsNullOrWhiteSpace(alternative.CaptureSourceCategory))
            {
                throw new ArgumentException("A capture-source category is required.", nameof(origin));
            }
            OriginDemandService.RequireNonNegativeFinite(alternative.Attraction, nameof(alternative.Attraction));
            OriginDemandService.RequireNonNegativeFinite(
                alternative.OriginFacilityModifier,
                nameof(alternative.OriginFacilityModifier));
            if (alternative.NetworkTravelTimeMinutes is { } travelTime)
            {
                OriginDemandService.RequireNonNegativeFinite(travelTime, nameof(alternative.NetworkTravelTimeMinutes));
            }
            if (alternative.RouteFound && alternative.NetworkTravelTimeMinutes is null)
            {
                throw new ArgumentException(
                    $"Facility '{alternative.FacilityKey}' is marked route-found without network travel time.",
                    nameof(origin));
            }
        }
    }

    private sealed record WeightedAlternative(GravityAlternativeInput Input, double? LogWeight);
}

public sealed record EquilibriumOriginInput(
    string OriginKey,
    double Demand,
    double OutsideOptionWeight,
    IReadOnlyCollection<GravityAlternativeInput> Incumbents,
    GravityAlternativeInput ProposedFacility);

public sealed record MarketEquilibriumRequest(
    IReadOnlyCollection<EquilibriumOriginInput> Origins,
    GravityParameters Parameters);

public sealed record FacilityEquilibriumResult(
    string FacilityKey,
    bool IsProposedFacility,
    double BaselineAllocatedDemand,
    double WithProjectAllocatedDemand,
    double ChangeInAllocatedDemand);

public sealed record MarketEquilibriumOriginResult(
    string OriginKey,
    GravityOriginResult Baseline,
    GravityOriginResult WithProject,
    double ProposedFacilityDemand,
    IReadOnlyDictionary<string, double> ProposedCaptureBySource,
    double CaptureReconciliationResidual);

public sealed record MarketEquilibriumResult(
    IReadOnlyList<MarketEquilibriumOriginResult> Origins,
    IReadOnlyList<FacilityEquilibriumResult> Facilities,
    IReadOnlyDictionary<string, double> ProposedCaptureBySource,
    double TotalDemand,
    double ProposedFacilityDemand,
    double ConservationResidual);

public interface IMarketEquilibriumService
{
    MarketEquilibriumResult Calculate(MarketEquilibriumRequest request);
}

public sealed class MarketEquilibriumService(IGravityModelService gravityModelService) : IMarketEquilibriumService
{
    public MarketEquilibriumResult Calculate(MarketEquilibriumRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Origins.Count == 0)
        {
            throw new ArgumentException("At least one origin is required.", nameof(request));
        }
        if (request.Origins.Select(origin => origin.OriginKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            request.Origins.Count)
        {
            throw new ArgumentException("Equilibrium origin keys must be unique.", nameof(request));
        }

        var originResults = new List<MarketEquilibriumOriginResult>(request.Origins.Count);
        var facilityTotals = new Dictionary<string, MutableFacilityTotal>(StringComparer.OrdinalIgnoreCase);
        var captureTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var origin in request.Origins)
        {
            if (!origin.ProposedFacility.IsProposedFacility)
            {
                throw new ArgumentException(
                    $"Origin '{origin.OriginKey}' proposed alternative must be marked as proposed.",
                    nameof(request));
            }
            if (origin.Incumbents.Any(item => item.IsProposedFacility) ||
                origin.Incumbents.Any(item => string.Equals(
                    item.FacilityKey,
                    origin.ProposedFacility.FacilityKey,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    $"Origin '{origin.OriginKey}' baseline alternatives contain the proposed facility.",
                    nameof(request));
            }

            var baseline = gravityModelService.Allocate(
                new GravityOriginInput(
                    origin.OriginKey,
                    origin.Demand,
                    origin.OutsideOptionWeight,
                    origin.Incumbents),
                request.Parameters);
            var withProject = gravityModelService.Allocate(
                new GravityOriginInput(
                    origin.OriginKey,
                    origin.Demand,
                    origin.OutsideOptionWeight,
                    origin.Incumbents.Append(origin.ProposedFacility).ToArray()),
                request.Parameters);
            var proposedDemand = withProject.FacilityAllocations
                .Single(allocation => allocation.IsProposedFacility)
                .AllocatedDemand;
            var originCapture = CalculateCaptureBySource(baseline, withProject);
            var captureResidual = proposedDemand - originCapture.Values.Sum();
            if (Math.Abs(captureResidual) > Math.Max(1e-7, origin.Demand * 1e-10))
            {
                throw new InvalidOperationException(
                    $"Origin '{origin.OriginKey}' failed proposed-capture reconciliation by {captureResidual}.");
            }

            foreach (var allocation in baseline.FacilityAllocations)
            {
                var total = GetOrAdd(facilityTotals, allocation.FacilityKey, allocation.IsProposedFacility);
                total.Baseline += allocation.AllocatedDemand;
            }
            foreach (var allocation in withProject.FacilityAllocations)
            {
                var total = GetOrAdd(facilityTotals, allocation.FacilityKey, allocation.IsProposedFacility);
                total.WithProject += allocation.AllocatedDemand;
            }
            foreach (var capture in originCapture)
            {
                captureTotals[capture.Key] = captureTotals.GetValueOrDefault(capture.Key) + capture.Value;
            }

            originResults.Add(new MarketEquilibriumOriginResult(
                origin.OriginKey,
                baseline,
                withProject,
                proposedDemand,
                originCapture,
                captureResidual));
        }

        var facilities = facilityTotals
            .Select(pair => new FacilityEquilibriumResult(
                pair.Key,
                pair.Value.IsProposed,
                pair.Value.Baseline,
                pair.Value.WithProject,
                pair.Value.WithProject - pair.Value.Baseline))
            .OrderByDescending(result => result.IsProposedFacility)
            .ThenBy(result => result.FacilityKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var totalDemand = request.Origins.Sum(origin => origin.Demand);
        var proposedTotal = facilities.Single(result => result.IsProposedFacility).WithProjectAllocatedDemand;
        var conservationResidual = proposedTotal - captureTotals.Values.Sum();

        return new MarketEquilibriumResult(
            originResults,
            facilities,
            captureTotals,
            totalDemand,
            proposedTotal,
            conservationResidual);
    }

    private static Dictionary<string, double> CalculateCaptureBySource(
        GravityOriginResult baseline,
        GravityOriginResult withProject)
    {
        var capture = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var withProjectByKey = withProject.FacilityAllocations.ToDictionary(
            allocation => allocation.FacilityKey,
            StringComparer.OrdinalIgnoreCase);
        foreach (var baselineAllocation in baseline.FacilityAllocations)
        {
            var withProjectAmount = withProjectByKey[baselineAllocation.FacilityKey].AllocatedDemand;
            var loss = Math.Max(0d, baselineAllocation.AllocatedDemand - withProjectAmount);
            capture[baselineAllocation.CaptureSourceCategory] =
                capture.GetValueOrDefault(baselineAllocation.CaptureSourceCategory) + loss;
        }

        var outsideLoss = Math.Max(
            0d,
            baseline.OutsideOptionAllocatedDemand - withProject.OutsideOptionAllocatedDemand);
        capture[CaptureSourceCategories.OutsideOption] = outsideLoss;
        return capture;
    }

    private static MutableFacilityTotal GetOrAdd(
        IDictionary<string, MutableFacilityTotal> totals,
        string facilityKey,
        bool isProposed)
    {
        if (!totals.TryGetValue(facilityKey, out var total))
        {
            total = new MutableFacilityTotal { IsProposed = isProposed };
            totals.Add(facilityKey, total);
        }
        else if (total.IsProposed != isProposed)
        {
            throw new InvalidOperationException($"Facility '{facilityKey}' has inconsistent proposed status.");
        }

        return total;
    }

    private sealed class MutableFacilityTotal
    {
        public bool IsProposed { get; init; }
        public double Baseline { get; set; }
        public double WithProject { get; set; }
    }
}
