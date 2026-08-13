using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Configuration;
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

            var inMemorySettings = new Dictionary<string, string?>
            {
                { "PopulationHeatmap:BufferMiles", "100" }
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var taxOptions = Options.Create(new TaxAllocationOptions());
            var seeder = new TigerSeeder(
                new TigerIngestionService(NullLogger<TigerIngestionService>.Instance, config, new System.Net.Http.HttpClient()),
                NullLogger<TigerSeeder>.Instance,
                config,
                taxOptions);

            var controller = new CensusController(db, seeder, memoryCache, taxOptions, config);

            var result = await controller.GetPopulationHeatmap(stateFips);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("State FIPS must be 2 digits.", badRequest.Value);
        }
    }
}
