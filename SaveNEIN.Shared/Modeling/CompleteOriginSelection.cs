// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

namespace SaveNEIN.Shared.Modeling;

public sealed record OriginSelectionPage(int Total, IReadOnlyList<string> StableOriginIds);

/// <summary>Load the complete declared study selection without silently truncating a paginated response.</summary>
public static class CompleteOriginSelection
{
    public static async Task<IReadOnlyList<string>> LoadAsync(
        Func<int, CancellationToken, Task<OriginSelectionPage>> fetchPage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetchPage);
        var ids = new List<string>();
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        int? total = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await fetchPage(ids.Count, cancellationToken);
            if (page.Total < 0 || (total.HasValue && page.Total != total.Value))
                throw new InvalidDataException("The origin selection changed while it was loading.");
            total = page.Total;
            if (page.StableOriginIds is null || page.StableOriginIds.Count > total.Value - ids.Count ||
                (page.StableOriginIds.Count == 0 && ids.Count < total.Value))
                throw new InvalidDataException("The origin response is incomplete or exceeds its declared count.");
            foreach (var id in page.StableOriginIds)
            {
                if (string.IsNullOrWhiteSpace(id) || !distinct.Add(id))
                    throw new InvalidDataException("The origin selection contains empty or duplicate identities.");
                ids.Add(id);
            }
        } while (ids.Count < total.Value);
        return ids;
    }
}
