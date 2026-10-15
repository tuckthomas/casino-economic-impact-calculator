// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SaveNEIN.Server.Services.Providers;

/// <summary>Known omitted border facilities, not a claim of complete Wisconsin or precise historical capacity.</summary>
public sealed class WisconsinBorderFacilityProvider(HttpClient http)
{
    public const string RegulatorUrl = "https://doa.wi.gov/Gaming/Gaming%202024-2025%20Annual%20Report%20FINAL.pdf";
    public async Task<ProviderDataset<CasinoCompetitorImportRow>> CaptureAsync(byte[] regulatorBytes, CancellationToken cancellationToken = default)
    {
        var text = string.Join('\n', MissouriGamingCommissionProvider.ExtractPages(regulatorBytes));
        if (!text.Contains("2024-2025", StringComparison.Ordinal) || !text.Contains("Potawatomi Hotel", StringComparison.Ordinal) ||
            !text.Contains("Gaming Wisconsin Dells", StringComparison.Ordinal))
            throw new InvalidDataException("Wisconsin's captured 2025 regulator inventory has different facility identities.");
        var rows = new List<CasinoCompetitorImportRow>();
        var records = new List<string> { Hash(regulatorBytes) };
        foreach (var (key, name, tribe, address, source, requiredAddressToken) in Catalog)
        {
            var htmlBytes = await http.GetByteArrayAsync(source, cancellationToken);
            var html = Encoding.UTF8.GetString(htmlBytes);
            if (!html.Contains(requiredAddressToken, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The operator page no longer confirms the captured address for {name}.");
            if (key == "ho-chunk-madison" && (!html.Contains("2025", StringComparison.Ordinal) || !html.Contains("JACKPOT", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The Madison operator page does not contain its historical operating evidence.");
            var geoBytes = await http.GetByteArrayAsync($"https://geocode.arcgis.com/arcgis/rest/services/World/GeocodeServer/findAddressCandidates?SingleLine={Uri.EscapeDataString(address)}&f=json&countryCode=USA&maxLocations=1", cancellationToken);
            using var geo = JsonDocument.Parse(geoBytes);
            var candidates = geo.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() == 0 || candidates[0].GetProperty("score").GetDouble() < 90)
                throw new InvalidDataException($"Wisconsin operator address cannot be reliably geocoded: {address}.");
            var match = candidates[0];
            records.Add($"{key}|operator={Hash(htmlBytes)}|geocode={Hash(geoBytes)}");
            rows.Add(new($"USA-WI-BORDER-{key}", name, "WI", "USA", "tribal-casino", "tribal-casino", "operating-2025-source-listed",
                null, key == "ho-chunk-madison" ? "Operator historical page; Class II not included in state Class III report" : "Wisconsin Division of Gaming",
                null, tribe, null, null, null, null, match.GetProperty("location").GetProperty("y").GetDouble(), match.GetProperty("location").GetProperty("x").GetDouble(),
                true, tribe, source, DateTime.UtcNow, true, null, null, null, false, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
                $"Operator-published address {address}; geocode score {match.GetProperty("score").GetDouble():F1}. Wisconsin FY2025 inventory {RegulatorUrl}; Madison operation is separately evidenced by its operator's year-specific jackpot history. Jackpot payouts are not GGR and were not imported as revenue. Historical unit counts and amenities remain null; structural reference substitution is an explicit unresolved prior, not a measured capacity. Current addresses/geocodes require temporal review before release."));
        }
        var hash = Hash(Encoding.UTF8.GetBytes(string.Join('\n', records)));
        return new(new RegisterDataSourceRequest("Wisconsin known border competitor supplement", "Wisconsin Division of Gaming and tribal operators", RegulatorUrl,
                "regulator-and-operator-address-geocode", "US-WI southeastern border subset", "2025 operating evidence; 2026 address capture", DateTime.UtcNow,
                hash, true, "Source public-use and geocoder terms apply.", $"Source response hashes:\n{string.Join('\n', records)}"),
            DatasetSnapshotKinds.Competitors, "2025", new(2025, 1, 1), new(2025, 12, 31), hash, "wisconsin-border-identity-v1", rows,
            ["Three known omitted border facilities added; complete Wisconsin inventory, Oneida boundary sites and other neighboring states still require audit.",
             "Historical physical counts and individual revenue are unreported in this capture. No current marketing counts or jackpot payouts were substituted for 2025 capacity/GGR.",
             "Unknown capacity uses existing model reference priors during diagnostics; this does not meet independent complete-input release acceptance."]);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static readonly (string Key, string Name, string Tribe, string Address, string Source, string AddressToken)[] Catalog =
    [
        ("potawatomi-milwaukee", "Potawatomi Casino Hotel Milwaukee", "Forest County Potawatomi", "1721 West Canal Street, Milwaukee, WI 53233", "https://www.potawatomi.com/contact", "1721"),
        ("ho-chunk-madison", "Ho-Chunk Gaming Madison", "Ho-Chunk Nation", "4002 Evan Acres Road, Madison, WI 53718", "https://www.ho-chunkgaming.com/madison/", "4002"),
        ("ho-chunk-wisconsin-dells", "Ho-Chunk Gaming Wisconsin Dells", "Ho-Chunk Nation", "S3214 County Road BD, Baraboo, WI 53913", "https://www.ho-chunkgaming.com/wisconsindells/", "S3214")
    ];
}
