using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaveFW.Server.Data.Entities;

[Table("counties")]
public class County
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StateFips { get; set; } = string.Empty;
    public string CountyFips { get; set; } = string.Empty;
    
    [Column(TypeName = "geometry(MultiPolygon, 4326)")]
    public MultiPolygon Geom { get; set; } = null!;
}

[Table("block_groups")]
public class BlockGroup
{
    [Key]
    public string GeoId { get; set; } = string.Empty;
    public string CountyFips { get; set; } = string.Empty;
    public int Population { get; set; }
    public int? MedianIncome { get; set; }

    [Column(TypeName = "geometry(MultiPolygon, 4326)")]
    public MultiPolygon Geom { get; set; } = null!;
}

[Table("isochrone_cache")]
public class IsochroneCache
{
    [Key]
    public long Id { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public int Minutes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? SourceHash { get; set; }

    [Column(TypeName = "geometry(MultiPolygon, 4326)")]
    public MultiPolygon Geom { get; set; } = null!;
}

[Table("site_scores")]
public class SiteScore
{
    [Key]
    public long Id { get; set; }
    public int CountyId { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public int Minutes { get; set; }
    public double PopEst { get; set; }
    public double? IncomeEst { get; set; }
    public double Score { get; set; }
    public DateTime ComputedAt { get; set; }
    public string? SourceHash { get; set; }
}

/// <summary>
/// Address points from NAD (National Address Database) and OpenAddresses.
/// Supports deduplication via source identity fields and incremental updates.
/// </summary>
[Table("address_points")]
public class AddressPoint
{
    [Key]
    public long Id { get; set; }
    
    /// <summary>Source: 'NAD' or 'OpenAddresses'</summary>
    [Required]
    [MaxLength(20)]
    public string Source { get; set; } = string.Empty;
    
    /// <summary>Original ID from source dataset for upsert/deduplication</summary>
    [Required]
    [MaxLength(100)]
    public string SourceId { get; set; } = string.Empty;
    
    /// <summary>When the source record was last updated (if available)</summary>
    public DateTime? SourceUpdatedAt { get; set; }
    
    /// <summary>When this record was first ingested</summary>
    public DateTime IngestedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>When this record was last seen in an ingestion run</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>False if the record was not seen in recent ingestion (tombstone)</summary>
    public bool IsActive { get; set; } = true;
    
    // --- Address Components ---
    
    [Required]
    [MaxLength(20)]
    public string HouseNumber { get; set; } = string.Empty;
    
    /// <summary>Raw street name as provided by source</summary>
    [Required]
    [MaxLength(200)]
    public string StreetNameRaw { get; set; } = string.Empty;
    
    /// <summary>Normalized street name for matching (uppercase, standardized abbreviations)</summary>
    [Required]
    [MaxLength(200)]
    public string StreetNameNorm { get; set; } = string.Empty;
    
    [MaxLength(10)]
    public string? StreetPredir { get; set; }
    
    [MaxLength(20)]
    public string? StreetType { get; set; }
    
    [MaxLength(10)]
    public string? StreetPostdir { get; set; }
    
    [MaxLength(50)]
    public string? Unit { get; set; }
    
    [MaxLength(100)]
    public string? City { get; set; }
    
    [Required]
    [MaxLength(2)]
    public string State { get; set; } = string.Empty;
    
    [MaxLength(10)]
    public string? Zip { get; set; }
    
    /// <summary>Point geometry in WGS84</summary>
    [Column(TypeName = "geometry(Point, 4326)")]
    public Point Geom { get; set; } = null!;
    
    /// <summary>Raw source data preserved as JSON for traceability</summary>
    [Column(TypeName = "jsonb")]
    public string? Raw { get; set; }
    
    /// <summary>Source precedence rank: NAD=1, OpenAddresses=2 (lower wins)</summary>
    public short SourceRank { get; set; } = 99;
    
    /// <summary>Optional: USPS DPV key for future validation integration</summary>
    [MaxLength(50)]
    public string? UspsDpvKey { get; set; }
}

/// <summary>
/// TIGER address ranges for interpolation fallback only.
/// Not mixed with address_points - used when no point match exists.
/// </summary>
[Table("tiger_address_ranges")]
public class TigerAddressRange
{
    [Key]
    public long Id { get; set; }
    
    [Required]
    [MaxLength(2)]
    public string State { get; set; } = string.Empty;
    
    [MaxLength(3)]
    public string? CountyFp { get; set; }
    
    // Left side address range
    [MaxLength(12)]
    public string? LFromHn { get; set; }
    
    [MaxLength(12)]
    public string? LToHn { get; set; }
    
    // Right side address range
    [MaxLength(12)]
    public string? RFromHn { get; set; }
    
    [MaxLength(12)]
    public string? RToHn { get; set; }
    
    /// <summary>Full street name from TIGER</summary>
    [MaxLength(200)]
    public string? FullName { get; set; }
    
    /// <summary>Normalized name for matching</summary>
    [MaxLength(200)]
    public string? NameNorm { get; set; }
    
    /// <summary>Line geometry representing the street segment</summary>
    [Column(TypeName = "geometry(LineString, 4326)")]
    public LineString Geom { get; set; } = null!;
}

/// <summary>
/// Reference dataset of existing casinos and casino-like gambling venues.
/// Used for competition-aware location scoring and revenue heuristics.
/// </summary>
[Table("casino_competitors")]
public class CasinoCompetitor
{
    [Key]
    [Column("id")]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(200)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(2)]
    [Column("state")]
    public string State { get; set; } = string.Empty;
    
    [MaxLength(100)]
    [Column("county")]
    public string? County { get; set; }
    
    [MaxLength(100)]
    [Column("city")]
    public string? City { get; set; }
    
    [Column("latitude")]
    public double Latitude { get; set; }
    
    [Column("longitude")]
    public double Longitude { get; set; }
    
    [Column("is_active")]
    public bool IsActive { get; set; } = true;
    
    [Column("notes")]
    public string? Notes { get; set; }
    
    // Venue classification fields
    [Required]
    [MaxLength(50)]
    [Column("venue_type")]
    public string VenueType { get; set; } = string.Empty;
    
    [MaxLength(200)]
    [Column("operator_name")]
    public string? OperatorName { get; set; }
    
    [Column("market_notes")]
    public string? MarketNotes { get; set; }
    
    [MaxLength(500)]
    [Column("source_url")]
    public string? SourceUrl { get; set; }
    
    [Column("last_verified_at")]
    public DateTime? LastVerifiedAt { get; set; }
    
    // Competition/feature fields
    [Column("has_slots")]
    public bool HasSlots { get; set; }
    [Column("has_table_games")]
    public bool HasTableGames { get; set; }
    [Column("has_poker")]
    public bool HasPoker { get; set; }
    [Column("has_sportsbook")]
    public bool HasSportsbook { get; set; }
    [Column("has_racetrack")]
    public bool HasRacetrack { get; set; }
    [Column("has_hotel")]
    public bool HasHotel { get; set; }
    [Column("has_restaurants")]
    public bool HasRestaurants { get; set; }
    [Column("has_entertainment")]
    public bool HasEntertainment { get; set; }
    [Column("has_loyalty_program")]
    public bool HasLoyaltyProgram { get; set; }
    [Column("has_resort_amenities")]
    public bool HasResortAmenities { get; set; }
    
    [Column("estimated_competition_weight")]
    public double? EstimatedCompetitionWeight { get; set; }

    [Column("geom", TypeName = "geometry(Point, 4326)")]
    public Point Geom { get; set; } = null!;
}
