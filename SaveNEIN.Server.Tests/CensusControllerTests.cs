using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SaveNEIN.Server.Controllers;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Services;
using Xunit;

namespace SaveNEIN.Server.Tests
{
    public class CensusControllerTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("123")]
        [InlineData("XX")]
        public async Task GetPopulationHeatmap_InvalidFips_ReturnsBadRequest(string stateFips)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: $"CensusDb_{System.Guid.NewGuid()}")
                .Options;
            using var db = new AppDbContext(options);

            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var seeder = new TigerSeeder(
                new TigerIngestionService(NullLogger<TigerIngestionService>.Instance, config, new System.Net.Http.HttpClient()),
                NullLogger<TigerSeeder>.Instance,
                config,
                db);

            var controller = new CensusController(db, seeder, memoryCache);

            var result = await controller.GetPopulationHeatmap(stateFips);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("State FIPS must be 2 digits.", badRequest.Value);
        }
    }
}
