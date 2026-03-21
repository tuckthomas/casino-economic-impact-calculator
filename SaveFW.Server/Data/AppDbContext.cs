using Microsoft.EntityFrameworkCore;
using SaveFW.Shared;

namespace SaveFW.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<ImpactFact> ImpactFacts => Set<ImpactFact>();
    public DbSet<Legislator> Legislators => Set<Legislator>();
    
    public DbSet<SaveFW.Server.Data.Entities.County> Counties => Set<SaveFW.Server.Data.Entities.County>();
    public DbSet<SaveFW.Server.Data.Entities.BlockGroup> BlockGroups => Set<SaveFW.Server.Data.Entities.BlockGroup>();
    public DbSet<SaveFW.Server.Data.Entities.IsochroneCache> IsochroneCache => Set<SaveFW.Server.Data.Entities.IsochroneCache>();
    public DbSet<SaveFW.Server.Data.Entities.SiteScore> SiteScores => Set<SaveFW.Server.Data.Entities.SiteScore>();
    
    // Phase 9: Address Point Infrastructure
    public DbSet<SaveFW.Server.Data.Entities.AddressPoint> AddressPoints => Set<SaveFW.Server.Data.Entities.AddressPoint>();
    public DbSet<SaveFW.Server.Data.Entities.TigerAddressRange> TigerAddressRanges => Set<SaveFW.Server.Data.Entities.TigerAddressRange>();
    public DbSet<SaveFW.Server.Data.Entities.CasinoCompetitor> CasinoCompetitors => Set<SaveFW.Server.Data.Entities.CasinoCompetitor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // PostGIS GIST Indexes
        modelBuilder.Entity<SaveFW.Server.Data.Entities.County>()
            .HasIndex(c => c.Geom)
            .HasMethod("gist");

        modelBuilder.Entity<SaveFW.Server.Data.Entities.BlockGroup>()
            .HasIndex(b => b.Geom)
            .HasMethod("gist");
        modelBuilder.Entity<SaveFW.Server.Data.Entities.BlockGroup>()
            .HasIndex(b => b.CountyFips);

        modelBuilder.Entity<SaveFW.Server.Data.Entities.IsochroneCache>()
            .HasIndex(i => i.Geom)
            .HasMethod("gist");
            
        // Unique constraint approximation (application should round before query)
        modelBuilder.Entity<SaveFW.Server.Data.Entities.IsochroneCache>()
            .HasIndex(i => new { i.Lat, i.Lon, i.Minutes, i.SourceHash });
            
        // --- Phase 9: Address Point Indexes ---
        
        // AddressPoint: Unique index for upsert on (source, source_id)
        modelBuilder.Entity<SaveFW.Server.Data.Entities.AddressPoint>()
            .HasIndex(a => new { a.Source, a.SourceId })
            .IsUnique();
            
        // AddressPoint: GIST index on geometry
        modelBuilder.Entity<SaveFW.Server.Data.Entities.AddressPoint>()
            .HasIndex(a => a.Geom)
            .HasMethod("gist");
            
        // AddressPoint: Lookup index for geocoding queries
        modelBuilder.Entity<SaveFW.Server.Data.Entities.AddressPoint>()
            .HasIndex(a => new { a.State, a.Zip, a.StreetNameNorm, a.HouseNumber });
            
        // TigerAddressRange: GIST index on geometry
        modelBuilder.Entity<SaveFW.Server.Data.Entities.TigerAddressRange>()
            .HasIndex(t => t.Geom)
            .HasMethod("gist");
            
        // TigerAddressRange: Lookup index for interpolation
        modelBuilder.Entity<SaveFW.Server.Data.Entities.TigerAddressRange>()
            .HasIndex(t => new { t.State, t.NameNorm });
            
        // CasinoCompetitor: GIST index on geometry
        modelBuilder.Entity<SaveFW.Server.Data.Entities.CasinoCompetitor>()
            .HasIndex(c => c.Geom)
            .HasMethod("gist");
    }
}

