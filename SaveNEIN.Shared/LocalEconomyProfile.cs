namespace SaveNEIN.Shared;

public sealed class LocalEconomyProfile
{
    public string CountyFips { get; set; } = "";
    public int IndustryYear { get; set; } = 2025;
    public int AcsYear { get; set; } = 2024;
    public DateTimeOffset RetrievedAtUtc { get; set; }
    public List<IndustryProfile> Industries { get; set; } = [];
    public WorkforceProfile? Workforce { get; set; }
    public MonthlyLaborMarket? CurrentLaborMarket { get; set; }
    public List<OccupationWage> Occupations { get; set; } = [];
    public string WageAreaName { get; set; } = "";
    public string WageSourceUrl { get; set; } = "";
    public List<string> SourceUrls { get; set; } = [];
    public List<string> Notices { get; set; } = [];
    public bool PackagedIndustrySnapshot { get; set; }
}

public sealed class IndustryProfile
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public double? Jobs { get; set; }
    public double? Establishments { get; set; }
    public double? AnnualPay { get; set; }
    public double? Payroll { get; set; }
    public double? Share { get; set; }
    public double? ChangeFiveYear { get; set; }
    public double? ChangeTenYear { get; set; }
    public double? LocationQuotient { get; set; }
    public bool Suppressed { get; set; }
}

public sealed class SurveyEstimate
{
    public double? Value { get; set; }
    public double? MarginOfError { get; set; }
}

public sealed class MonthlyLaborMarket
{
    public string Period { get; set; } = "";
    public double LaborForce { get; set; }
    public double Employed { get; set; }
    public double Unemployed { get; set; }
    public double UnemploymentRate { get; set; }
    public bool Preliminary { get; set; }
}

public sealed class WorkforceProfile
{
    public SurveyEstimate Population16Plus { get; set; } = new();
    public SurveyEstimate CivilianLaborForce { get; set; } = new();
    public SurveyEstimate Employed { get; set; } = new();
    public SurveyEstimate Unemployed { get; set; } = new();
    public SurveyEstimate NotInLaborForce { get; set; } = new();
    public SurveyEstimate ResidentWorkers { get; set; } = new();
    public SurveyEstimate WorkInCounty { get; set; } = new();
    public SurveyEstimate WorkElsewhereInState { get; set; } = new();
    public SurveyEstimate WorkOutsideState { get; set; } = new();
}

public sealed class OccupationWage
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public double? Employment { get; set; }
    public double? MedianHourly { get; set; }
    public double? MeanAnnual { get; set; }
}

public sealed class HiringScenario
{
    public double Positions { get; set; } = 1000;
    public double FtePerPosition { get; set; } = 1;
    public double PreviouslyUnemployedPercent { get; set; } = 25;
    public double ExistingEmployersPercent { get; set; } = 25;
    public double InCommutersPercent { get; set; } = 25;
    public double NewResidentsPercent { get; set; } = 25;
    public string SupportingInformation { get; set; } = "";
}

public sealed class LocalEconomyReport
{
    public string StatusNote { get; set; } = "";
    public LocalEconomyProfile? Profile { get; set; }
    public HiringScenario Hiring { get; set; } = new();
}
