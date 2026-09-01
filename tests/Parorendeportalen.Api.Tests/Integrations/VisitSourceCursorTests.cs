using Parorendeportalen.Api.Integrations;

namespace Parorendeportalen.Api.Tests.Integrations;

public class VisitSourceCursorTests
{
    [Fact]
    public void Initial_HasNeitherAWatermarkNorAToken()
    {
        Assert.Null(VisitSourceCursor.Initial.ChangedSince);
        Assert.Null(VisitSourceCursor.Initial.ContinuationToken);
    }

    [Fact]
    public void Since_StartsAFreshRunAtTheWatermark()
    {
        var watermark = new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);

        var cursor = VisitSourceCursor.Since(watermark);

        Assert.Equal(watermark, cursor.ChangedSince);
        Assert.Null(cursor.ContinuationToken);
    }

    // Dropping it on page two widens the next fetch back to everything.
    [Fact]
    public void Next_CarriesTheWatermarkForward_AndSetsTheToken()
    {
        var watermark = new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);

        var second = VisitSourceCursor.Since(watermark).Next("page-2");

        Assert.Equal(watermark, second.ChangedSince);
        Assert.Equal("page-2", second.ContinuationToken);
    }

    [Fact]
    public void Next_ReplacesTheTokenRatherThanAccumulating()
    {
        var third = VisitSourceCursor.Initial.Next("page-2").Next("page-3");

        Assert.Equal("page-3", third.ContinuationToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Next_Rejects_ABlankToken(string token)
    {
        Assert.Throws<ArgumentException>(() => VisitSourceCursor.Initial.Next(token));
    }
}
