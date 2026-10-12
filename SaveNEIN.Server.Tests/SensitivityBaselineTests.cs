using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Controllers;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Tests;

public sealed class SensitivityBaselineTests
{
    [Fact]
    public async Task RunDetailsReturnSavedParameterValuesRatherThanCurrentDefaults()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var runId = Guid.NewGuid();
        db.ModelRuns.Add(new ModelRun { Id = runId, Status = ModelRunStatuses.Finalized });
        db.ModelParameterDefinitions.Add(new ModelParameterDefinition { Id = 1, Key = "gravity.beta", SystemDefaultValue = 9 });
        db.ModelRunParameterValues.Add(new ModelRunParameterValue { ModelRunId = runId, ParameterDefinitionId = 1, FinalValue = 1.75, SourceLayer = "user-override" });
        await db.SaveChangesAsync();
        var response = Assert.IsType<OkObjectResult>(await new GravityModelRunsController(db, null!, null!).Get(runId, default));
        var parameters = JsonSerializer.SerializeToElement(response.Value).GetProperty("Parameters");
        Assert.Equal("gravity.beta", parameters[0].GetProperty("Key").GetString());
        Assert.Equal(1.75, parameters[0].GetProperty("Value").GetDouble());
    }

    [Fact]
    public void SavedBaselineRejectsDriftMissingValuesAndNonFiniteValues()
    {
        var saved = new Dictionary<string, double> { ["gravity.beta"] = 1.75, ["social_cost.prevalence"] = 0.01 };
        SensitivityAnalysisService.RequireExpectedBaseline(new Dictionary<string, double>(saved), saved);
        Assert.Throws<InvalidOperationException>(() => SensitivityAnalysisService.RequireExpectedBaseline(
            new Dictionary<string, double>(saved) { ["gravity.beta"] = 2 }, saved));
        Assert.Throws<InvalidOperationException>(() => SensitivityAnalysisService.RequireExpectedBaseline(new Dictionary<string, double>(), saved));
        Assert.Throws<InvalidOperationException>(() => SensitivityAnalysisService.RequireExpectedBaseline(
            new Dictionary<string, double>(saved) { ["gravity.beta"] = double.NaN }, saved));
    }
}
