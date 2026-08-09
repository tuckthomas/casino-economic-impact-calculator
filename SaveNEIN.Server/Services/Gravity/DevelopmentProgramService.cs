using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Gravity;

public sealed record DevelopmentProgramDefinition(
    string StableProgramId,
    string Version,
    string Name,
    int SlotOrVltPositions,
    int TableGameCount,
    int PokerTableCount,
    bool HasSportsbook,
    int HotelRoomCount,
    int GamingFloorSquareFeet,
    int FoodBeverageVenueCount,
    int EventCapacity,
    int ResortAmenityCount,
    decimal? CapitalCost,
    int? CapitalCostDollarYear,
    DateOnly? PlannedOpeningDate,
    int StabilizedYearNumber,
    string? Notes);

public interface IDevelopmentProgramService
{
    Task<DevelopmentProgram> CreateAsync(
        DevelopmentProgramDefinition definition,
        CancellationToken cancellationToken = default);

    Task<DevelopmentProgram> CreateVersionAsync(
        Guid sourceProgramId,
        DevelopmentProgramDefinition definition,
        CancellationToken cancellationToken = default);
}

public sealed class DevelopmentProgramService(AppDbContext db) : IDevelopmentProgramService
{
    public async Task<DevelopmentProgram> CreateAsync(
        DevelopmentProgramDefinition definition,
        CancellationToken cancellationToken = default)
    {
        Validate(definition);
        if (await db.DevelopmentPrograms.AnyAsync(
                program => program.StableProgramId == definition.StableProgramId.Trim() &&
                           program.Version == definition.Version.Trim(),
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Development program '{definition.StableProgramId}' already has version '{definition.Version}'.");
        }

        var program = Map(definition);
        db.DevelopmentPrograms.Add(program);
        await db.SaveChangesAsync(cancellationToken);
        return program;
    }

    public async Task<DevelopmentProgram> CreateVersionAsync(
        Guid sourceProgramId,
        DevelopmentProgramDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var source = await db.DevelopmentPrograms
            .AsNoTracking()
            .SingleOrDefaultAsync(program => program.Id == sourceProgramId, cancellationToken)
            ?? throw new KeyNotFoundException($"Development program '{sourceProgramId}' was not found.");
        if (!string.Equals(source.StableProgramId, definition.StableProgramId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A new development-program version must preserve the stable program ID.");
        }
        return await CreateAsync(definition, cancellationToken);
    }

    public static void Validate(DevelopmentProgramDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.StableProgramId) ||
            string.IsNullOrWhiteSpace(definition.Version) ||
            string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new ArgumentException("Stable ID, version, and name are required.", nameof(definition));
        }
        var counts = new[]
        {
            definition.SlotOrVltPositions,
            definition.TableGameCount,
            definition.PokerTableCount,
            definition.HotelRoomCount,
            definition.GamingFloorSquareFeet,
            definition.FoodBeverageVenueCount,
            definition.EventCapacity,
            definition.ResortAmenityCount
        };
        if (counts.Any(value => value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(definition), "Development-program counts cannot be negative.");
        }
        if (definition.CapitalCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(definition), "Capital cost cannot be negative.");
        }
        if (definition.StabilizedYearNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(definition), "Stabilized year must be at least one.");
        }
        if (definition.SlotOrVltPositions == 0 && definition.TableGameCount == 0)
        {
            throw new ArgumentException(
                "A casino development program must include gaming positions or table games.",
                nameof(definition));
        }
    }

    private static DevelopmentProgram Map(DevelopmentProgramDefinition definition) => new()
    {
        StableProgramId = definition.StableProgramId.Trim(),
        Version = definition.Version.Trim(),
        Name = definition.Name.Trim(),
        SlotOrVltPositions = definition.SlotOrVltPositions,
        TableGameCount = definition.TableGameCount,
        PokerTableCount = definition.PokerTableCount,
        HasSportsbook = definition.HasSportsbook,
        HotelRoomCount = definition.HotelRoomCount,
        GamingFloorSquareFeet = definition.GamingFloorSquareFeet,
        FoodBeverageVenueCount = definition.FoodBeverageVenueCount,
        EventCapacity = definition.EventCapacity,
        ResortAmenityCount = definition.ResortAmenityCount,
        CapitalCost = definition.CapitalCost,
        CapitalCostDollarYear = definition.CapitalCostDollarYear,
        PlannedOpeningDate = definition.PlannedOpeningDate,
        StabilizedYearNumber = definition.StabilizedYearNumber,
        Notes = definition.Notes,
        IsImmutable = false
    };
}
