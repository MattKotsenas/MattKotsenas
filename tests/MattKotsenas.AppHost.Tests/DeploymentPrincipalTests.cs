using MattKotsenas.AppHost;

namespace MattKotsenas.AppHost.Tests;

public sealed class DeploymentPrincipalTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-guid")]
    [InlineData("{55555555-5555-5555-5555-555555555555}")] // Braced GUIDs are not canonical object IDs.
    public void ParseObjectIdRejectsInvalidValues(string? value)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => DeploymentPrincipal.ParseObjectId(value));

        Assert.Contains(
            "must contain the deployment principal's object ID",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ParseObjectIdReturnsCanonicalGuid()
    {
        var result = DeploymentPrincipal.ParseObjectId(
            "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");

        Assert.Equal(
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            result);
    }
}
