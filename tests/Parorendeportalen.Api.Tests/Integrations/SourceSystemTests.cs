using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Tests.Integrations;

public class SourceSystemTests
{
    // Catches a new SourceSystem mapped to Portal, which sync may overwrite.
    [Fact]
    public void ToOrigin_MapsEverySourceSystem_ToADefinedOriginThatIsNotPortal()
    {
        var sourceSystems = Enum.GetValues<SourceSystem>();

        Assert.NotEmpty(sourceSystems);
        foreach (var sourceSystem in sourceSystems)
        {
            var origin = sourceSystem.ToOrigin();

            Assert.True(Enum.IsDefined(origin), $"{sourceSystem} maps to undefined Origin {(int)origin}.");
            Assert.NotEqual(Origin.Portal, origin);
        }
    }

    [Fact]
    public void ToOrigin_Throws_ForTheUnnamedZeroValue()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => default(SourceSystem).ToOrigin());

        Assert.Equal("sourceSystem", exception.ParamName);
    }

    [Fact]
    public void ToOrigin_MapsSynthetic_ToOriginSynthetic()
    {
        Assert.Equal(Origin.Synthetic, SourceSystem.Synthetic.ToOrigin());
    }
}
