// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Text.Json;

namespace SaveNEIN.Server.Services.Validation;

public sealed record BenchmarkOutputMetric(string Path, decimal Value, string Currency);

public interface IBenchmarkOutputReader
{
    BenchmarkOutputMetric ReadMonetaryMetric(string reportedOutputsJson, string metricPath);
}

public sealed class BenchmarkOutputReader : IBenchmarkOutputReader
{
    public BenchmarkOutputMetric ReadMonetaryMetric(string reportedOutputsJson, string metricPath)
    {
        var normalizedPath = metricPath?.Trim() ?? string.Empty;
        var segments = normalizedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0))
        {
            throw new ArgumentException("A dot-delimited benchmark metric path is required.", nameof(metricPath));
        }

        using var document = JsonDocument.Parse(reportedOutputsJson);
        var current = document.RootElement;
        foreach (var segment in segments)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                throw new KeyNotFoundException($"Benchmark output metric '{normalizedPath}' was not found.");
            }
        }
        if (current.ValueKind != JsonValueKind.Number || !current.TryGetDecimal(out var value))
        {
            throw new InvalidOperationException($"Benchmark output metric '{normalizedPath}' is not a decimal number.");
        }

        var currency = document.RootElement.TryGetProperty("currency", out var currencyElement) &&
                       currencyElement.ValueKind == JsonValueKind.String
            ? currencyElement.GetString()?.Trim().ToUpperInvariant()
            : null;
        if (currency is null or "")
        {
            throw new InvalidOperationException("Benchmark monetary outputs must declare a currency.");
        }

        return new BenchmarkOutputMetric(normalizedPath, value, currency);
    }
}
