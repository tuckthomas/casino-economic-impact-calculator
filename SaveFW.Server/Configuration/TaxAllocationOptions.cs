namespace SaveFW.Server.Configuration;

public sealed class TaxAllocationOptions
{
    public string DefaultScenarioId { get; set; } = string.Empty;
    public List<TaxAllocationScenarioDefinition> Scenarios { get; set; } = new();

    public HashSet<string> GetMunicipalEligibleCountyFips()
    {
        return Scenarios
            .Where(s => s.Municipality?.Enabled == true)
            .SelectMany(s => s.Municipality?.EligibleCountyFips ?? Enumerable.Empty<string>())
            .Select(value => string.Concat((value ?? string.Empty).Where(char.IsDigit)))
            .Where(value => value.Length == 5)
            .ToHashSet(StringComparer.Ordinal);
    }
}

public sealed class TaxAllocationScenarioDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string? BillName { get; set; }
    public string? PublicName { get; set; }
    public TaxAllocationRecipients? Recipients { get; set; }
    public TaxAllocationMunicipalityRule? Municipality { get; set; }
    public TaxAllocationRules? Rules { get; set; }
}

public sealed class TaxAllocationRecipients
{
    public string State { get; set; } = string.Empty;
    public string County { get; set; } = string.Empty;
    public string Municipality { get; set; } = string.Empty;
    public string Regional { get; set; } = string.Empty;
}

public sealed class TaxAllocationMunicipalityRule
{
    public bool Enabled { get; set; }
    public bool RequiresContainment { get; set; }
    public bool FallbackToCounty { get; set; }
    public List<string> EligibleCountyFips { get; set; } = new();
}

public sealed class TaxAllocationRules
{
    public TaxAllocationComponentRules? Regular { get; set; }
    public TaxAllocationComponentRules? Supplemental { get; set; }
}

public sealed class TaxAllocationComponentRules
{
    public TaxAllocationBranchRules? Municipal { get; set; }
    public TaxAllocationBranchRules? Fallback { get; set; }
}

public sealed class TaxAllocationBranchRules
{
    public double State { get; set; }
    public double County { get; set; }
    public double Municipality { get; set; }
    public double Regional { get; set; }
}
