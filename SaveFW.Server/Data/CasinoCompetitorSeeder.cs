using Microsoft.EntityFrameworkCore;
using SaveFW.Server.Data.Entities;
using NetTopologySuite.Geometries;

namespace SaveFW.Server.Data;

public static class CasinoCompetitorSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.CasinoCompetitors.AnyAsync())
            return;

        var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

        var competitors = new List<CasinoCompetitor>
        {
            // Northeast Indiana Relevant (In-State)
            new CasinoCompetitor { Name = "Four Winds Casino South Bend", State = "IN", City = "South Bend", Latitude = 41.6508, Longitude = -86.2941, VenueType = "full_service_casino", OperatorName = "Pokagon Band of Potawatomi", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasHotel = true, HasRestaurants = true, HasEntertainment = true },
            new CasinoCompetitor { Name = "Harrah's Hoosier Park", State = "IN", City = "Anderson", Latitude = 40.0827, Longitude = -85.6562, VenueType = "racino", OperatorName = "Caesars Entertainment", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasRacetrack = true, HasRestaurants = true, HasEntertainment = true },
            new CasinoCompetitor { Name = "Hard Rock Casino Northern Indiana", State = "IN", City = "Gary", Latitude = 41.5772, Longitude = -87.3976, VenueType = "full_service_casino", OperatorName = "Hard Rock", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasRestaurants = true, HasEntertainment = true },
            new CasinoCompetitor { Name = "Ameristar Casino East Chicago", State = "IN", City = "East Chicago", Latitude = 41.6552, Longitude = -87.4475, VenueType = "full_service_casino", OperatorName = "PENN Entertainment", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasHotel = true, HasRestaurants = true },
            new CasinoCompetitor { Name = "Bally's Evansville", State = "IN", City = "Evansville", Latitude = 37.9719, Longitude = -87.5786, VenueType = "full_service_casino", OperatorName = "Bally's", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasHotel = true, HasRestaurants = true },
            new CasinoCompetitor { Name = "Belterra Casino Resort", State = "IN", City = "Florence", Latitude = 38.7844, Longitude = -84.9458, VenueType = "full_service_casino", OperatorName = "Boyd Gaming", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasHotel = true, HasRestaurants = true, HasResortAmenities = true },
            new CasinoCompetitor { Name = "Blue Chip Casino Hotel Spa", State = "IN", City = "Michigan City", Latitude = 41.7247, Longitude = -86.9069, VenueType = "full_service_casino", OperatorName = "Boyd Gaming", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasHotel = true, HasRestaurants = true, HasEntertainment = true, HasResortAmenities = true },
            new CasinoCompetitor { Name = "Caesars Southern Indiana", State = "IN", City = "Elizabeth", Latitude = 38.1565, Longitude = -85.9926, VenueType = "full_service_casino", OperatorName = "Caesars Entertainment", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasHotel = true, HasRestaurants = true, HasEntertainment = true },
            new CasinoCompetitor { Name = "French Lick Resort Casino", State = "IN", City = "French Lick", Latitude = 38.5528, Longitude = -86.6225, VenueType = "full_service_casino", OperatorName = "Cook Group", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasHotel = true, HasRestaurants = true, HasResortAmenities = true },
            new CasinoCompetitor { Name = "Hollywood Casino Lawrenceburg", State = "IN", City = "Lawrenceburg", Latitude = 39.0989, Longitude = -84.8519, VenueType = "full_service_casino", OperatorName = "PENN Entertainment", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasHotel = true, HasRestaurants = true, HasEntertainment = true },
            new CasinoCompetitor { Name = "Horseshoe Hammond", State = "IN", City = "Hammond", Latitude = 41.6947, Longitude = -87.5147, VenueType = "full_service_casino", OperatorName = "Caesars Entertainment", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasRestaurants = true, HasEntertainment = true },
            new CasinoCompetitor { Name = "Horseshoe Indianapolis", State = "IN", City = "Shelbyville", Latitude = 39.5668, Longitude = -85.8239, VenueType = "racino", OperatorName = "Caesars Entertainment", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasRacetrack = true, HasRestaurants = true, HasEntertainment = true },
            new CasinoCompetitor { Name = "Rising Star Casino Resort", State = "IN", City = "Rising Sun", Latitude = 38.9419, Longitude = -84.8472, VenueType = "full_service_casino", OperatorName = "Full House Resorts", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasHotel = true, HasRestaurants = true, HasResortAmenities = true },
            
            // Michigan Competitors
            new CasinoCompetitor { Name = "Four Winds New Buffalo", State = "MI", City = "New Buffalo", Latitude = 41.7828, Longitude = -86.7262, VenueType = "full_service_casino", OperatorName = "Pokagon Band of Potawatomi", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasHotel = true, HasRestaurants = true, HasEntertainment = true },
            new CasinoCompetitor { Name = "FireKeepers Casino Hotel", State = "MI", City = "Battle Creek", Latitude = 42.2891, Longitude = -85.1274, VenueType = "full_service_casino", OperatorName = "Nottawaseppi Huron Band", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasHotel = true, HasRestaurants = true, HasEntertainment = true },
            new CasinoCompetitor { Name = "Gun Lake Casino", State = "MI", City = "Wayland", Latitude = 42.6179, Longitude = -85.6425, VenueType = "full_service_casino", OperatorName = "Match-E-Be-Nash-She-Wish Band", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasRestaurants = true, HasEntertainment = true },
            
            // Ohio Competitors
            new CasinoCompetitor { Name = "Hollywood Casino Toledo", State = "OH", City = "Toledo", Latitude = 41.6163, Longitude = -83.5358, VenueType = "full_service_casino", OperatorName = "PENN Entertainment", HasSlots = true, HasTableGames = true, HasSportsbook = true, HasRestaurants = true, HasEntertainment = true }
        };

        foreach (var competitor in competitors)
        {
            competitor.Geom = geometryFactory.CreatePoint(new Coordinate(competitor.Longitude, competitor.Latitude));
        }

        await db.CasinoCompetitors.AddRangeAsync(competitors);
        await db.SaveChangesAsync();
    }
}