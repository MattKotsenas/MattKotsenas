namespace MattKotsenas.Hosting.Azure.Dns.Tests;

public sealed class DnsRecordTypeTests
{
    [Theory]
    [InlineData("A", "A")]
    [InlineData("cname", "CNAME")]
    [InlineData("AAAA", "AAAA")]
    [InlineData("TYPE65", "TYPE65")]
    public void ValuesAreNormalized(
        string value,
        string expected)
    {
        Assert.Equal(
            expected,
            DnsRecordType.From(value).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not a type")]
    [InlineData("A!")]
    public void InvalidValuesAreRejected(string value)
    {
        Assert.Throws<ArgumentException>(
            () => DnsRecordType.From(value));
    }

    [Fact]
    public void DefaultValueIsInvalid()
    {
        Assert.Throws<InvalidOperationException>(
            () => default(DnsRecordType).Value);
    }

    [Fact]
    public void KnownTypesEqualNormalizedValues()
    {
        Assert.Equal(
            DnsRecordType.Cname,
            DnsRecordType.From("cname"));
    }
}
