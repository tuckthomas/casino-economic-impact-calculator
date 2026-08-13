using System;
using SaveNEIN.Server.Services;
using Xunit;

namespace SaveNEIN.Server.Tests
{
    public class TigerIngestionServiceTests
    {
        [Theory]
        [InlineData("18", "Indiana", "in")]
        [InlineData("17", "Illinois", "il")]
        [InlineData("21", "Kentucky", "ky")]
        [InlineData("26", "Michigan", "mi")]
        [InlineData("39", "Ohio", "oh")]
        [InlineData("55", "Wisconsin", "wi")]
        [InlineData("11", "District_of_Columbia", "dc")]
        [InlineData("72", "Puerto_Rico", "pr")]
        [InlineData("1", "Alabama", "al")]
        public void GetPl94171StateFiles_ValidFips_ReturnsExpectedFolderAndAbbreviation(
            string fips,
            string expectedFolder,
            string expectedAbbr)
        {
            var result = TigerIngestionService.GetPl94171StateFiles(fips);

            Assert.Equal(expectedFolder, result.Folder);
            Assert.Equal(expectedAbbr, result.Abbreviation);
        }

        [Theory]
        [InlineData("00")]
        [InlineData("99")]
        [InlineData("invalid")]
        public void GetPl94171StateFiles_InvalidFips_ThrowsNotSupportedException(string fips)
        {
            Assert.Throws<NotSupportedException>(() => TigerIngestionService.GetPl94171StateFiles(fips));
        }
    }
}
