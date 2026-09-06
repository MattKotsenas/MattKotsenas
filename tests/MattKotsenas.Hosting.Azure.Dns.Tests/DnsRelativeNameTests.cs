namespace MattKotsenas.Hosting.Azure.Dns.Tests;

public sealed class DnsRelativeNameTests
{
    [Theory]
    [InlineData("@", "@")]
    [InlineData("_dnsauth.www", "_dnsauth.www")]
    [InlineData("WWW", "www")]
    [InlineData("*", "*")]
    [InlineData("*.www", "*.www")]
    public void ValidNamesAreNormalized(
        string value,
        string expected)
    {
        Assert.Equal(
            expected,
            DnsRelativeName.From(value).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".www")]
    [InlineData("www.")]
    [InlineData("not valid")]
    [InlineData("www*")]
    [InlineData("www.*")]
    public void InvalidNamesAreRejected(string value)
    {
        Assert.Throws<ArgumentException>(
            () => DnsRelativeName.From(value));
    }

    [Fact]
    public void NullIsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => DnsRelativeName.From(null!));
    }

    [Fact]
    public void OversizedNamesAreRejected()
    {
        Assert.Throws<ArgumentException>(
            () => DnsRelativeName.From(new string('a', 64)));
        Assert.Throws<ArgumentException>(
            () => DnsRelativeName.From(
                string.Join(
                    '.',
                    Enumerable.Repeat(
                        new string('a', 63),
                        5))));
    }
}
