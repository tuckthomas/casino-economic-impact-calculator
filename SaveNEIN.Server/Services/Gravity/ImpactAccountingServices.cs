// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

namespace SaveNEIN.Server.Services.Gravity;

public sealed record CannibalizationAccountingInput(
    double HostJurisdictionIncumbentCapture,
    double ExternalCommercialIncumbentCapture,
    double TribalOrOtherJurisdictionCapture,
    double OutsideOptionCapture,
    double InducedResidentGgr,
    double TourismGgr,
    double TrafficGgr);

public sealed record CannibalizationAccountingResult(
    double HostJurisdictionCannibalization,
    double CrossJurisdictionCapture,
    double OutsideOrUnmodeledLeakageCapture,
    double InducedResidentGgr,
    double TourismGgr,
    double TrafficGgr,
    double TransferEffectGgr,
    double MarketExpansionAndImportGgr,
    double StabilizedGgr);

public interface ICannibalizationAccountingService
{
    CannibalizationAccountingResult Calculate(CannibalizationAccountingInput input);
}

public sealed class CannibalizationAccountingService : ICannibalizationAccountingService
{
    public CannibalizationAccountingResult Calculate(CannibalizationAccountingInput input)
    {
        RequireNonnegative(input.HostJurisdictionIncumbentCapture, nameof(input.HostJurisdictionIncumbentCapture));
        RequireNonnegative(input.ExternalCommercialIncumbentCapture, nameof(input.ExternalCommercialIncumbentCapture));
        RequireNonnegative(input.TribalOrOtherJurisdictionCapture, nameof(input.TribalOrOtherJurisdictionCapture));
        RequireNonnegative(input.OutsideOptionCapture, nameof(input.OutsideOptionCapture));
        RequireNonnegative(input.InducedResidentGgr, nameof(input.InducedResidentGgr));
        RequireNonnegative(input.TourismGgr, nameof(input.TourismGgr));
        RequireNonnegative(input.TrafficGgr, nameof(input.TrafficGgr));

        var crossJurisdiction = input.ExternalCommercialIncumbentCapture + input.TribalOrOtherJurisdictionCapture;
        var transfer = input.HostJurisdictionIncumbentCapture + crossJurisdiction + input.OutsideOptionCapture;
        var expansionAndImport = input.InducedResidentGgr + input.TourismGgr + input.TrafficGgr;
        return new CannibalizationAccountingResult(
            input.HostJurisdictionIncumbentCapture,
            crossJurisdiction,
            input.OutsideOptionCapture,
            input.InducedResidentGgr,
            input.TourismGgr,
            input.TrafficGgr,
            transfer,
            expansionAndImport,
            transfer + expansionAndImport);
    }

    private static void RequireNonnegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(name, "A finite, nonnegative amount is required.");
        }
    }
}

public sealed record DisplacementSectorInput(
    string SectorKey,
    double PriorWeight,
    double LocalInventoryModifier,
    double TaxableShare,
    double BusinessMargin,
    double SalesTaxRate,
    double BusinessIncomeTaxRate,
    double AnnualSalesPerJob);

public sealed record DisplacementInput(
    double LocalResidentGamingBase,
    double LocalCasinoCannibalization,
    double RepatriatedOrPreviouslyLeakedResidentGgr,
    double EligibleBaseShare,
    double DisplacementCoefficient,
    IReadOnlyCollection<DisplacementSectorInput> Sectors);

public sealed record SectorDisplacementResult(
    string SectorKey,
    double NormalizedWeight,
    double DisplacedSales,
    double DisplacedTaxableSales,
    double DisplacedBusinessIncome,
    double SalesTaxLoss,
    double BusinessIncomeTaxLoss,
    double DisplacedJobs);

public sealed record DisplacementResult(
    double LocalResidentGamingBase,
    double ExcludedLocalCasinoCannibalization,
    double ExcludedRepatriatedOrLeakedResidentGgr,
    double RemainingLocalResidentGamingBase,
    double DisplacementEligibleBase,
    double DisplacementCoefficient,
    double TotalDisplacedSales,
    IReadOnlyList<SectorDisplacementResult> Sectors);

public interface IDisplacementModelService
{
    DisplacementResult Calculate(DisplacementInput input);
}

public sealed class DisplacementModelService : IDisplacementModelService
{
    public DisplacementResult Calculate(DisplacementInput input)
    {
        RequireNonnegative(input.LocalResidentGamingBase, nameof(input.LocalResidentGamingBase));
        RequireNonnegative(input.LocalCasinoCannibalization, nameof(input.LocalCasinoCannibalization));
        RequireNonnegative(input.RepatriatedOrPreviouslyLeakedResidentGgr, nameof(input.RepatriatedOrPreviouslyLeakedResidentGgr));
        RequireShare(input.EligibleBaseShare, nameof(input.EligibleBaseShare));
        RequireShare(input.DisplacementCoefficient, nameof(input.DisplacementCoefficient));
        if (input.Sectors.Count == 0)
        {
            throw new ArgumentException("At least one displacement sector is required.", nameof(input));
        }
        if (input.Sectors.Select(sector => sector.SectorKey).Distinct(StringComparer.Ordinal).Count() != input.Sectors.Count)
        {
            throw new ArgumentException("Displacement sector keys must be unique.", nameof(input));
        }

        var excludedCannibalization = Math.Min(input.LocalResidentGamingBase, input.LocalCasinoCannibalization);
        var afterCannibalization = input.LocalResidentGamingBase - excludedCannibalization;
        var excludedLeakage = Math.Min(afterCannibalization, input.RepatriatedOrPreviouslyLeakedResidentGgr);
        var remaining = Math.Max(0, afterCannibalization - excludedLeakage);
        var eligible = remaining * input.EligibleBaseShare;
        var displaced = eligible * input.DisplacementCoefficient;

        var adjustedWeights = input.Sectors.Select(sector =>
        {
            RequireKey(sector.SectorKey, nameof(sector.SectorKey));
            RequireNonnegative(sector.PriorWeight, nameof(sector.PriorWeight));
            RequireNonnegative(sector.LocalInventoryModifier, nameof(sector.LocalInventoryModifier));
            RequireShare(sector.TaxableShare, nameof(sector.TaxableShare));
            RequireShare(sector.BusinessMargin, nameof(sector.BusinessMargin));
            RequireShare(sector.SalesTaxRate, nameof(sector.SalesTaxRate));
            RequireShare(sector.BusinessIncomeTaxRate, nameof(sector.BusinessIncomeTaxRate));
            if (!double.IsFinite(sector.AnnualSalesPerJob) || sector.AnnualSalesPerJob <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sector.AnnualSalesPerJob), "Annual sales per job must be positive and finite.");
            }
            return (Sector: sector, Weight: sector.PriorWeight * sector.LocalInventoryModifier);
        }).ToArray();
        var weightTotal = adjustedWeights.Sum(item => item.Weight);
        if (!double.IsFinite(weightTotal) || weightTotal <= 0)
        {
            throw new InvalidOperationException("Displacement sector weights must have a positive adjusted sum.");
        }

        var sectors = adjustedWeights.Select(item =>
        {
            var weight = item.Weight / weightTotal;
            var sectorSales = displaced * weight;
            var taxableSales = sectorSales * item.Sector.TaxableShare;
            var businessIncome = sectorSales * item.Sector.BusinessMargin;
            return new SectorDisplacementResult(
                item.Sector.SectorKey,
                weight,
                sectorSales,
                taxableSales,
                businessIncome,
                taxableSales * item.Sector.SalesTaxRate,
                businessIncome * item.Sector.BusinessIncomeTaxRate,
                sectorSales / item.Sector.AnnualSalesPerJob);
        }).ToArray();

        return new DisplacementResult(
            input.LocalResidentGamingBase,
            excludedCannibalization,
            excludedLeakage,
            remaining,
            eligible,
            input.DisplacementCoefficient,
            displaced,
            sectors);
    }

    private static void RequireKey(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty key is required.", name);
        }
    }

    private static void RequireShare(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name, "A share must be between zero and one.");
        }
    }

    private static void RequireNonnegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(name, "A finite, nonnegative value is required.");
        }
    }
}

public sealed record EmploymentImpactInput(
    double StabilizedGgr,
    double CapitalCost,
    double HostIncumbentCannibalization,
    double DirectJobsPerMillionGgr,
    double ConstructionJobYearsPerMillionCapitalCost,
    double IndirectAndInducedJobsPerDirectJob,
    double IncumbentJobsPerMillionLostGgr,
    double DirectAverageAnnualWage,
    double IndirectAverageAnnualWage,
    double IncumbentAverageAnnualWage,
    IReadOnlyCollection<SectorDisplacementResult> SectorDisplacement);

public sealed record EmploymentImpactResult(
    double DirectCasinoJobs,
    double ConstructionJobYears,
    double IndirectAndInducedJobs,
    double DisplacedSectorJobs,
    double IncumbentCasinoJobsLost,
    double NetPermanentJobs,
    double DirectLaborIncome,
    double IndirectLaborIncome,
    double IncumbentLaborIncomeLost);

public interface IEmploymentImpactService
{
    EmploymentImpactResult Calculate(EmploymentImpactInput input);
}

public sealed class EmploymentImpactService : IEmploymentImpactService
{
    public EmploymentImpactResult Calculate(EmploymentImpactInput input)
    {
        var values = new[]
        {
            input.StabilizedGgr,
            input.CapitalCost,
            input.HostIncumbentCannibalization,
            input.DirectJobsPerMillionGgr,
            input.ConstructionJobYearsPerMillionCapitalCost,
            input.IndirectAndInducedJobsPerDirectJob,
            input.IncumbentJobsPerMillionLostGgr,
            input.DirectAverageAnnualWage,
            input.IndirectAverageAnnualWage,
            input.IncumbentAverageAnnualWage
        };
        if (values.Any(value => !double.IsFinite(value) || value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Employment inputs must be finite and nonnegative.");
        }
        var direct = input.StabilizedGgr / 1_000_000d * input.DirectJobsPerMillionGgr;
        var construction = input.CapitalCost / 1_000_000d * input.ConstructionJobYearsPerMillionCapitalCost;
        var indirect = direct * input.IndirectAndInducedJobsPerDirectJob;
        var displaced = input.SectorDisplacement.Sum(sector => sector.DisplacedJobs);
        var incumbentLost = input.HostIncumbentCannibalization / 1_000_000d * input.IncumbentJobsPerMillionLostGgr;
        return new EmploymentImpactResult(
            direct,
            construction,
            indirect,
            displaced,
            incumbentLost,
            direct + indirect - displaced - incumbentLost,
            direct * input.DirectAverageAnnualWage,
            indirect * input.IndirectAverageAnnualWage,
            incumbentLost * input.IncumbentAverageAnnualWage);
    }
}

public sealed record FiscalImpactInput(
    double GamingTax,
    double LocalRevenueShare,
    double NonGamingSalesTax,
    double PayrollIncomeTax,
    double BusinessIncomeTax,
    double PropertyTax,
    double DisplacedSalesTaxLoss,
    double DisplacedBusinessIncomeTaxLoss,
    double HostIncumbentGamingTaxLoss,
    double OtherJurisdictionGamingTaxLoss);

public sealed record FiscalImpactResult(
    double GrossGamingTax,
    double HostLocalGrossPublicRevenue,
    double HostStateGrossPublicRevenue,
    double DisplacedLocalFiscalLoss,
    double HostIncumbentGamingTaxLoss,
    double OtherJurisdictionGamingTaxLoss,
    double NetHostLocalFiscalImpact,
    double NetHostStateFiscalImpact,
    double OtherJurisdictionFiscalImpact);

public interface IFiscalImpactService
{
    FiscalImpactResult Calculate(FiscalImpactInput input);
}

public sealed class FiscalImpactService : IFiscalImpactService
{
    public FiscalImpactResult Calculate(FiscalImpactInput input)
    {
        var values = new[]
        {
            input.GamingTax,
            input.LocalRevenueShare,
            input.NonGamingSalesTax,
            input.PayrollIncomeTax,
            input.BusinessIncomeTax,
            input.PropertyTax,
            input.DisplacedSalesTaxLoss,
            input.DisplacedBusinessIncomeTaxLoss,
            input.HostIncumbentGamingTaxLoss,
            input.OtherJurisdictionGamingTaxLoss
        };
        if (values.Any(value => !double.IsFinite(value) || value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Fiscal inputs must be finite and nonnegative.");
        }
        if (input.LocalRevenueShare > input.GamingTax)
        {
            throw new InvalidOperationException("Local revenue share cannot exceed total gaming tax.");
        }

        var displacedLoss = input.DisplacedSalesTaxLoss + input.DisplacedBusinessIncomeTaxLoss;
        var localGross = input.LocalRevenueShare + input.PropertyTax;
        var stateGross = input.GamingTax - input.LocalRevenueShare + input.NonGamingSalesTax +
                         input.PayrollIncomeTax + input.BusinessIncomeTax;
        return new FiscalImpactResult(
            input.GamingTax,
            localGross,
            stateGross,
            displacedLoss,
            input.HostIncumbentGamingTaxLoss,
            input.OtherJurisdictionGamingTaxLoss,
            localGross - displacedLoss,
            stateGross - input.HostIncumbentGamingTaxLoss,
            -input.OtherJurisdictionGamingTaxLoss);
    }
}

public sealed record SocialCostDomainInput(string DomainKey, double PerCaseCost, double Scale = 1);

public sealed record SocialCostInput(
    double ExposedEligiblePopulation,
    double Prevalence,
    double ExposureResponse,
    double LowCaseMultiplier,
    double HighCaseMultiplier,
    IReadOnlyCollection<SocialCostDomainInput> Domains);

public sealed record SocialCostDomainResult(
    string DomainKey,
    double IncrementalCases,
    double PerCaseCost,
    double AnnualCost,
    double LowAnnualCost,
    double HighAnnualCost);

public sealed record SocialCostResult(
    double ExposedEligiblePopulation,
    double IncrementalCases,
    double AnnualCost,
    double LowAnnualCost,
    double HighAnnualCost,
    IReadOnlyList<SocialCostDomainResult> Domains);

public interface ISocialCostService
{
    SocialCostResult Calculate(SocialCostInput input);
}

public sealed class SocialCostService : ISocialCostService
{
    public SocialCostResult Calculate(SocialCostInput input)
    {
        RequireNonnegative(input.ExposedEligiblePopulation, nameof(input.ExposedEligiblePopulation));
        RequireShare(input.Prevalence, nameof(input.Prevalence));
        RequireNonnegative(input.ExposureResponse, nameof(input.ExposureResponse));
        RequireNonnegative(input.LowCaseMultiplier, nameof(input.LowCaseMultiplier));
        RequireNonnegative(input.HighCaseMultiplier, nameof(input.HighCaseMultiplier));
        if (input.LowCaseMultiplier > input.HighCaseMultiplier)
        {
            throw new InvalidOperationException("The low social-cost multiplier cannot exceed the high multiplier.");
        }
        if (input.Domains.Select(domain => domain.DomainKey).Distinct(StringComparer.Ordinal).Count() != input.Domains.Count)
        {
            throw new ArgumentException("Social-cost domain keys must be unique.", nameof(input));
        }

        var cases = input.ExposedEligiblePopulation * input.Prevalence * input.ExposureResponse;
        var domains = input.Domains.Select(domain =>
        {
            if (string.IsNullOrWhiteSpace(domain.DomainKey))
            {
                throw new ArgumentException("A social-cost domain key is required.", nameof(input));
            }
            RequireNonnegative(domain.PerCaseCost, nameof(domain.PerCaseCost));
            RequireNonnegative(domain.Scale, nameof(domain.Scale));
            var annualCost = cases * domain.PerCaseCost * domain.Scale;
            return new SocialCostDomainResult(
                domain.DomainKey,
                cases,
                domain.PerCaseCost,
                annualCost,
                annualCost * input.LowCaseMultiplier,
                annualCost * input.HighCaseMultiplier);
        }).ToArray();
        return new SocialCostResult(
            input.ExposedEligiblePopulation,
            cases,
            domains.Sum(domain => domain.AnnualCost),
            domains.Sum(domain => domain.LowAnnualCost),
            domains.Sum(domain => domain.HighAnnualCost),
            domains);
    }

    private static void RequireShare(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name, "A share must be between zero and one.");
        }
    }

    private static void RequireNonnegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(name, "A finite, nonnegative value is required.");
        }
    }
}

public sealed record NetImpactInput(
    double GrossPropertyGgr,
    double TransferEffectGgr,
    double CrossJurisdictionImportedGgr,
    double OutsideOrUnmodeledLeakageCapture,
    double InducedResidentGgr,
    double TourismAndTrafficImportGgr,
    double LocalDiscretionaryDisplacement,
    double DirectAndIndirectLaborIncome,
    double NetHostLocalFiscalImpact,
    double NetHostStateFiscalImpact,
    double GrossSocialCost);

public sealed record NetImpactResult(
    double GrossPropertyGgr,
    double TransferEffectGgr,
    double CrossJurisdictionImportedGgr,
    double OutsideOrUnmodeledLeakageCapture,
    double InducedResidentGgr,
    double TourismAndTrafficImportGgr,
    double LocalDiscretionaryDisplacement,
    double DirectAndIndirectLaborIncome,
    double NetHostLocalFiscalImpact,
    double NetHostStateFiscalImpact,
    double GrossSocialCost,
    double NetNewLocalGamingActivity,
    double NetHostLocalImpact,
    double NetHostStateImpact);

public interface INetImpactService
{
    NetImpactResult Calculate(NetImpactInput input);
}

public sealed class NetImpactService : INetImpactService
{
    public NetImpactResult Calculate(NetImpactInput input)
    {
        var nonnegative = new[]
        {
            input.GrossPropertyGgr,
            input.TransferEffectGgr,
            input.CrossJurisdictionImportedGgr,
            input.OutsideOrUnmodeledLeakageCapture,
            input.InducedResidentGgr,
            input.TourismAndTrafficImportGgr,
            input.LocalDiscretionaryDisplacement,
            input.DirectAndIndirectLaborIncome,
            input.GrossSocialCost
        };
        if (nonnegative.Any(value => !double.IsFinite(value) || value < 0) ||
            !double.IsFinite(input.NetHostLocalFiscalImpact) ||
            !double.IsFinite(input.NetHostStateFiscalImpact))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Net-impact inputs must be finite and monetary costs/flows must be nonnegative.");
        }

        var netNewLocalGamingActivity = input.CrossJurisdictionImportedGgr + input.InducedResidentGgr +
                                        input.TourismAndTrafficImportGgr - input.LocalDiscretionaryDisplacement;
        return new NetImpactResult(
            input.GrossPropertyGgr,
            input.TransferEffectGgr,
            input.CrossJurisdictionImportedGgr,
            input.OutsideOrUnmodeledLeakageCapture,
            input.InducedResidentGgr,
            input.TourismAndTrafficImportGgr,
            input.LocalDiscretionaryDisplacement,
            input.DirectAndIndirectLaborIncome,
            input.NetHostLocalFiscalImpact,
            input.NetHostStateFiscalImpact,
            input.GrossSocialCost,
            netNewLocalGamingActivity,
            netNewLocalGamingActivity + input.NetHostLocalFiscalImpact - input.GrossSocialCost,
            netNewLocalGamingActivity + input.NetHostStateFiscalImpact - input.GrossSocialCost);
    }
}
