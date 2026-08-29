using MattKotsenas.AppHost;

namespace MattKotsenas.AppHost.Tests;

public sealed class ObjectIdTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-guid")]
    [InlineData("{55555555-5555-5555-5555-555555555555}")] // Braced GUIDs are not canonical object IDs.
    public void FromStringRejectsInvalidValues(string? value)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ObjectId.FromString(value));

        Assert.Contains(
            "must be a canonical GUID",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ToStringReturnsCanonicalGuid()
    {
        var objectId = ObjectId.FromString(
            "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");

        Assert.Equal(
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            objectId.ToString());
    }
}
