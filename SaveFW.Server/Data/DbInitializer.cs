using Microsoft.EntityFrameworkCore;
using SaveFW.Shared;
using System.Data.Common;
using System.Text.Json;

namespace SaveFW.Server.Data;

public static class DbInitializer
{
    public static async Task Seed(AppDbContext db)
    {
        // 1. Ensure database is created
        await db.Database.EnsureCreatedAsync();

        // 1b. Initialize Address Points infrastructure (views, functions)
        await InitializeAddressPointsInfrastructure(db);

        // 2. Seed Impact Facts
        if (!await db.ImpactFacts.AnyAsync())
        {
            var csvPath = Path.Combine(Directory.GetCurrentDirectory(), "../../static_html_to_convert/sources.csv");
            if (File.Exists(csvPath))
            {
                var lines = await File.ReadAllLinesAsync(csvPath);
                var impacts = new List<ImpactFact>();

                // Simple CSV parsing (Category, Description, SourceUrl)
                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Handling commas inside quotes is tricky with simple split, 
                    // but for this specific file we know the structure.
                    // Let's use a slightly more robust split
                    var parts = ParseCsvLine(line);
                    if (parts.Count >= 2)
                    {
                        impacts.Add(new ImpactFact
                        {
                            Category = parts[0].Trim(),
                            Description = parts[1].Trim().Trim('"'),
                            SourceUrl = parts.Count > 2 ? parts[2].Trim().Trim('"') : null
                        });
                    }
                }
                await db.ImpactFacts.AddRangeAsync(impacts);
            }
        }

        // 3. Seed Legislators
        if (!await db.Legislators.AnyAsync())
        {
            var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "../../static_html_to_convert/data/legislators.json");
            if (File.Exists(jsonPath))
            {
                var json = await File.ReadAllTextAsync(jsonPath);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var legislators = new List<Legislator>();

                // City Council
                if (root.TryGetProperty("city_council", out var cityCouncil))
                {
                    foreach (var prop in cityCouncil.EnumerateObject())
                    {
                        if (prop.Name == "at_large")
                        {
                            foreach (var person in prop.Value.EnumerateArray())
                            {
                                legislators.Add(new Legislator
                                {
                                    Name = person.GetProperty("name").GetString() ?? "",
                                    Email = person.GetProperty("email").GetString() ?? "",
                                    Type = "City Council",
                                    District = "At Large"
                                });
                            }
                        }
                        else
                        {
                            legislators.Add(new Legislator
                            {
                                Name = prop.Value.GetProperty("name").GetString() ?? "",
                                Email = prop.Value.GetProperty("email").GetString() ?? "",
                                Type = "City Council",
                                District = prop.Name
                            });
                        }
                    }
                }

                // State House
                if (root.TryGetProperty("state_house", out var stateHouse))
                {
                    foreach (var prop in stateHouse.EnumerateObject())
                    {
                        legislators.Add(new Legislator
                        {
                            Name = prop.Value.GetProperty("name").GetString() ?? "",
                            Email = prop.Value.GetProperty("email").GetString() ?? "",
                            Party = prop.Value.TryGetProperty("party", out var p) ? p.GetString() : null,
                            Type = "State House",
                            District = prop.Name
                        });
                    }
                }

                await db.Legislators.AddRangeAsync(legislators);
            }
        }

        // 4. Seed Casino Competitors
        await CasinoCompetitorSeeder.SeedAsync(db);

        await db.SaveChangesAsync();
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result;
    }

    /// <summary>
    /// Initialize address points infrastructure (views, functions) for geocoding.
    /// Uses CREATE OR REPLACE for idempotency - safe to run on every startup.
    /// </summary>
    private static async Task InitializeAddressPointsInfrastructure(AppDbContext db)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync();
            }

            var columns = await GetTableColumnsAsync(conn, "address_points");
            if (columns.Count == 0)
            {
                return;
            }

            var stateColumn = RequireColumn(columns, "state");
            var zipColumn = RequireColumn(columns, "zip");
            var streetNameNormColumn = RequireColumn(columns, "street_name_norm");
            var houseNumberColumn = RequireColumn(columns, "house_number");
            var unitColumn = RequireColumn(columns, "unit");
            var sourceRankColumn = RequireColumn(columns, "source_rank");
            var sourceUpdatedAtColumn = RequireColumn(columns, "source_updated_at");
            var ingestedAtColumn = RequireColumn(columns, "ingested_at");
            var isActiveColumn = RequireColumn(columns, "is_active");

            // Create street name normalization function
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE OR REPLACE FUNCTION normalize_street_name(raw_name TEXT)
                RETURNS TEXT AS $$
                DECLARE result TEXT;
                BEGIN
                    result := UPPER(TRIM(raw_name));
                    result := REGEXP_REPLACE(result, '\bNORTH\b', 'N', 'g');
                    result := REGEXP_REPLACE(result, '\bSOUTH\b', 'S', 'g');
                    result := REGEXP_REPLACE(result, '\bEAST\b', 'E', 'g');
                    result := REGEXP_REPLACE(result, '\bWEST\b', 'W', 'g');
                    result := REGEXP_REPLACE(result, '\bNORTHEAST\b', 'NE', 'g');
                    result := REGEXP_REPLACE(result, '\bNORTHWEST\b', 'NW', 'g');
                    result := REGEXP_REPLACE(result, '\bSOUTHEAST\b', 'SE', 'g');
                    result := REGEXP_REPLACE(result, '\bSOUTHWEST\b', 'SW', 'g');
                    result := REGEXP_REPLACE(result, '\bST\b$', 'STREET', 'g');
                    result := REGEXP_REPLACE(result, '\bAVE\b$', 'AVENUE', 'g');
                    result := REGEXP_REPLACE(result, '\bBLVD\b$', 'BOULEVARD', 'g');
                    result := REGEXP_REPLACE(result, '\bDR\b$', 'DRIVE', 'g');
                    result := REGEXP_REPLACE(result, '\bLN\b$', 'LANE', 'g');
                    result := REGEXP_REPLACE(result, '\bRD\b$', 'ROAD', 'g');
                    result := REGEXP_REPLACE(result, '\bCT\b$', 'COURT', 'g');
                    result := REGEXP_REPLACE(result, '\bPL\b$', 'PLACE', 'g');
                    result := REGEXP_REPLACE(result, '\s+', ' ', 'g');
                    RETURN result;
                END;
                $$ LANGUAGE plpgsql IMMUTABLE;
            ");

            // Create preferred view for deduplication
            await using var createViewCommand = conn.CreateCommand();
            createViewCommand.CommandText = $@"
                CREATE OR REPLACE VIEW address_points_preferred AS
                SELECT DISTINCT ON (
                    {QuoteIdentifier(stateColumn)},
                    COALESCE({QuoteIdentifier(zipColumn)}, ''),
                    {QuoteIdentifier(streetNameNormColumn)},
                    {QuoteIdentifier(houseNumberColumn)},
                    COALESCE({QuoteIdentifier(unitColumn)}, '')
                ) *
                FROM address_points
                WHERE {QuoteIdentifier(isActiveColumn)} = TRUE
                ORDER BY
                    {QuoteIdentifier(stateColumn)},
                    COALESCE({QuoteIdentifier(zipColumn)}, ''),
                    {QuoteIdentifier(streetNameNormColumn)},
                    {QuoteIdentifier(houseNumberColumn)},
                    COALESCE({QuoteIdentifier(unitColumn)}, ''),
                    {QuoteIdentifier(sourceRankColumn)} ASC,
                    {QuoteIdentifier(sourceUpdatedAtColumn)} DESC NULLS LAST,
                    {QuoteIdentifier(ingestedAtColumn)} DESC;
            ";
            await createViewCommand.ExecuteNonQueryAsync();

            Console.WriteLine("Address points infrastructure initialized successfully.");
        }
        catch (Exception ex)
        {
            // Log but don't fail startup - this is optional infrastructure
            Console.WriteLine($"Warning: Could not initialize address points infrastructure: {ex.Message}");
        }
    }

    private static async Task<HashSet<string>> GetTableColumnsAsync(DbConnection conn, string tableName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @tableName;
        ";

        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "@tableName";
        parameter.Value = tableName;
        cmd.Parameters.Add(parameter);

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static string RequireColumn(HashSet<string> columns, string expectedName)
    {
        var normalizedExpectedName = NormalizeIdentifier(expectedName);
        var actualName = columns.FirstOrDefault(column => NormalizeIdentifier(column) == normalizedExpectedName);
        if (actualName is null)
        {
            throw new InvalidOperationException($"address_points is missing expected column '{expectedName}'.");
        }

        return actualName;
    }

    private static string NormalizeIdentifier(string identifier)
        => new(identifier.Where(ch => ch != '_').Select(char.ToLowerInvariant).ToArray());

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
