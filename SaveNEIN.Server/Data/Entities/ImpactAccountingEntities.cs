using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaveNEIN.Server.Data.Entities;

[Table("model_run_geographic_accounting")]
public sealed class ModelRunGeographicAccounting
{
    [Key]
    public long Id { get; set; }
    public Guid ModelRunId { get; set; }
    [Required, MaxLength(40)] public string ScopeKind { get; set; } = string.Empty;
    [Required, MaxLength(160)] public string ScopeCode { get; set; } = string.Empty;
    public int LocalOriginCount { get; set; }
    public decimal HostJurisdictionCannibalization { get; set; }
    public decimal CrossJurisdictionCapture { get; set; }
    public decimal OutsideOrUnmodeledLeakageCapture { get; set; }
    public decimal InducedResidentGgr { get; set; }
    public decimal TourismGgr { get; set; }
    public decimal TrafficGgr { get; set; }
    public decimal TransferEffectGgr { get; set; }
    public decimal MarketExpansionAndImportGgr { get; set; }
    public decimal StabilizedGgr { get; set; }
    public decimal LocalResidentGamingBase { get; set; }
    public decimal ExcludedLocalCasinoCannibalization { get; set; }
    public decimal ExcludedRepatriatedOrLeakedResidentGgr { get; set; }
    public decimal RemainingLocalResidentGamingBase { get; set; }
    [Required] public string LocalOriginIdsJson { get; set; } = "[]";
}

[Table("model_run_sector_displacement")]
public sealed class ModelRunSectorDisplacement
{
    [Key]
    public long Id { get; set; }
    public Guid ModelRunId { get; set; }
    [Required, MaxLength(40)] public string ScopeKind { get; set; } = string.Empty;
    [Required, MaxLength(160)] public string ScopeCode { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string SectorKey { get; set; } = string.Empty;
    public double NormalizedWeight { get; set; }
    public decimal DisplacementEligibleBase { get; set; }
    public double DisplacementCoefficient { get; set; }
    public decimal DisplacedSales { get; set; }
    public decimal DisplacedTaxableSales { get; set; }
    public decimal DisplacedBusinessIncome { get; set; }
    public decimal SalesTaxLoss { get; set; }
    public decimal BusinessIncomeTaxLoss { get; set; }
    public double DisplacedJobs { get; set; }
}

[Table("model_run_employment_impacts")]
public sealed class ModelRunEmploymentImpact
{
    [Key]
    public long Id { get; set; }
    public Guid ModelRunId { get; set; }
    [Required, MaxLength(40)] public string ScopeKind { get; set; } = string.Empty;
    [Required, MaxLength(160)] public string ScopeCode { get; set; } = string.Empty;
    public double DirectCasinoJobs { get; set; }
    public double ConstructionJobYears { get; set; }
    public double IndirectAndInducedJobs { get; set; }
    public double DisplacedSectorJobs { get; set; }
    public double IncumbentCasinoJobsLost { get; set; }
    public double NetPermanentJobs { get; set; }
    public decimal DirectLaborIncome { get; set; }
    public decimal IndirectLaborIncome { get; set; }
    public decimal IncumbentLaborIncomeLost { get; set; }
}

[Table("model_run_fiscal_impacts")]
public sealed class ModelRunFiscalImpact
{
    [Key]
    public long Id { get; set; }
    public Guid ModelRunId { get; set; }
    [Required, MaxLength(40)] public string ScopeKind { get; set; } = string.Empty;
    [Required, MaxLength(160)] public string ScopeCode { get; set; } = string.Empty;
    public decimal GrossGamingTax { get; set; }
    public decimal HostLocalGrossPublicRevenue { get; set; }
    public decimal HostStateGrossPublicRevenue { get; set; }
    public decimal DisplacedLocalFiscalLoss { get; set; }
    public decimal HostIncumbentGamingTaxLoss { get; set; }
    public decimal OtherJurisdictionGamingTaxLoss { get; set; }
    public decimal NetHostLocalFiscalImpact { get; set; }
    public decimal NetHostStateFiscalImpact { get; set; }
    public decimal OtherJurisdictionFiscalImpact { get; set; }
    [Required] public string RuleProvenanceJson { get; set; } = "{}";
}

[Table("model_run_social_costs")]
public sealed class ModelRunSocialCost
{
    [Key]
    public long Id { get; set; }
    public Guid ModelRunId { get; set; }
    [Required, MaxLength(40)] public string ScopeKind { get; set; } = string.Empty;
    [Required, MaxLength(160)] public string ScopeCode { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string DomainKey { get; set; } = string.Empty;
    public double ExposedEligiblePopulation { get; set; }
    public double IncrementalCases { get; set; }
    public decimal PerCaseCost { get; set; }
    public decimal AnnualCost { get; set; }
    public decimal LowAnnualCost { get; set; }
    public decimal HighAnnualCost { get; set; }
    public bool Included { get; set; } = true;
    public string? ProvenanceNotes { get; set; }
}

[Table("model_run_net_impacts")]
public sealed class ModelRunNetImpact
{
    [Key]
    public long Id { get; set; }
    public Guid ModelRunId { get; set; }
    [Required, MaxLength(40)] public string ScopeKind { get; set; } = string.Empty;
    [Required, MaxLength(160)] public string ScopeCode { get; set; } = string.Empty;
    public decimal GrossPropertyGgr { get; set; }
    public decimal TransferEffectGgr { get; set; }
    public decimal CrossJurisdictionImportedGgr { get; set; }
    public decimal OutsideOrUnmodeledLeakageCapture { get; set; }
    public decimal InducedResidentGgr { get; set; }
    public decimal TourismAndTrafficImportGgr { get; set; }
    public decimal LocalDiscretionaryDisplacement { get; set; }
    public decimal DirectAndIndirectLaborIncome { get; set; }
    public decimal NetHostLocalFiscalImpact { get; set; }
    public decimal NetHostStateFiscalImpact { get; set; }
    public decimal GrossSocialCost { get; set; }
    public decimal NetNewLocalGamingActivity { get; set; }
    public decimal NetHostLocalImpact { get; set; }
    public decimal NetHostStateImpact { get; set; }
    [Required, MaxLength(80)] public string AccountingMethodKey { get; set; } = "explicit-cash-flow-bridge-v1";
}

public static class ImpactScopeKinds
{
    public const string HostMunicipality = "host-municipality";
    public const string HostCounty = "host-county";
    public const string CustomRegion = "custom-region";
    public const string MetropolitanArea = "metropolitan-area";
    public const string HostState = "host-state";
}

public static class DisplacementSectorKeys
{
    public const string RestaurantHospitality = "restaurant-hospitality";
    public const string Retail = "retail";
    public const string ArtsEntertainmentRecreation = "arts-entertainment-recreation";
}
