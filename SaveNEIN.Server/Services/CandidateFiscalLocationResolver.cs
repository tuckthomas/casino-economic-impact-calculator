// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Data;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;

namespace SaveNEIN.Server.Services;

public sealed record CandidateFiscalLocation(
    string StateFips,
    string CountyFips,
    string CountyName,
    string? MunicipalityGeoid,
    string? MunicipalityName);

public interface ICandidateFiscalLocationResolver
{
    Task<CandidateFiscalLocation> ResolveAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default);
}

public sealed class CandidateFiscalLocationResolver(AppDbContext db) : ICandidateFiscalLocationResolver
{
    public async Task<CandidateFiscalLocation> ResolveAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Candidate coordinates must be valid WGS84 coordinates.");
        }
        if (!string.Equals(db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            throw new UnsupportedJurisdictionException("Candidate fiscal geography requires the PostgreSQL TIGER boundary store.");
        }

        var connection = db.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                WITH candidate AS (
                    SELECT ST_SetSRID(ST_Point(@longitude, @latitude), 4326) AS geom
                )
                SELECT county.state_fp,
                       county.geoid,
                       county.name,
                       place.geoid,
                       place.name
                FROM candidate
                JOIN LATERAL (
                    SELECT state_fp, geoid, name, geom
                    FROM tiger_counties
                    WHERE ST_Covers(geom, candidate.geom)
                    ORDER BY geoid
                    LIMIT 1
                ) county ON TRUE
                LEFT JOIN LATERAL (
                    SELECT geoid, name
                    FROM tiger_places
                    WHERE state_fp = county.state_fp
                      AND funcstat = 'A'
                      AND ST_Covers(geom, candidate.geom)
                    ORDER BY COALESCE(aland, 0) ASC, geoid
                    LIMIT 1
                ) place ON TRUE;
                """;
            AddParameter(command, "latitude", latitude);
            AddParameter(command, "longitude", longitude);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new UnsupportedJurisdictionException(
                    $"No TIGER county contains candidate {latitude:R},{longitude:R}; fiscal allocation cannot be resolved.");
            }
            return new CandidateFiscalLocation(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
