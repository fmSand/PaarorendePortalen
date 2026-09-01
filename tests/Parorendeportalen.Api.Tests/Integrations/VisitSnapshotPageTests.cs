using Parorendeportalen.Api.Integrations;

namespace Parorendeportalen.Api.Tests.Integrations;

public class VisitSnapshotPageTests
{
    [Fact]
    public void APageWithoutAToken_EndsTheRun()
    {
        var page = new VisitSnapshotPage([], ContinuationToken: null);

        Assert.False(page.HasMore);
    }

    [Fact]
    public void AnEmptyPageWithAToken_DoesNotEndTheRun()
    {
        var page = new VisitSnapshotPage([], "page-2");

        Assert.Empty(page.Snapshots);
        Assert.True(page.HasMore);
    }
}
