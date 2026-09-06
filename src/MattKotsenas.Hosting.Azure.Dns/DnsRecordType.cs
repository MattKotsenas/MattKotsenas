namespace MattKotsenas.Hosting.Azure.Dns;

/// <summary>
/// Identifies a DNS resource-record type.
/// </summary>
public readonly record struct DnsRecordType
{
    private readonly string? _value;

    private DnsRecordType(string value)
    {
        _value = value;
    }

    /// <summary>
    /// Gets the normalized record-type mnemonic.
    /// </summary>
    public string Value => _value ??
        throw new InvalidOperationException(
            "The DNS record type is uninitialized.");

    /// <summary>
    /// Gets the IPv4 address record type.
    /// </summary>
    public static DnsRecordType A { get; } = new("A");

    /// <summary>
    /// Gets the canonical name record type.
    /// </summary>
    public static DnsRecordType Cname { get; } = new("CNAME");

    /// <summary>
    /// Gets the text record type.
    /// </summary>
    public static DnsRecordType Txt { get; } = new("TXT");

    /// <summary>
    /// Creates a record type from an IANA mnemonic.
    /// </summary>
    /// <param name="value">The record-type mnemonic.</param>
    /// <returns>The normalized DNS record type.</returns>
    public static DnsRecordType From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(character =>
            !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException(
                $"'{value}' is not a DNS record type.",
                nameof(value));
        }

        return new(value.ToUpperInvariant());
    }

    /// <inheritdoc />
    public override string ToString() =>
        _value ?? string.Empty;
}
