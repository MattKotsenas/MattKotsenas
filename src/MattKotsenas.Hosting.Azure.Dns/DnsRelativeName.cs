namespace MattKotsenas.Hosting.Azure.Dns;

/// <summary>
/// Represents a validated DNS name relative to a zone.
/// </summary>
public sealed record DnsRelativeName
{
    private DnsRelativeName(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the normalized relative name.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets whether this name represents the zone apex.
    /// </summary>
    public bool IsApex => Value == "@";

    /// <summary>
    /// Gets the zone apex name.
    /// </summary>
    public static DnsRelativeName Apex { get; } = new("@");

    /// <summary>
    /// Creates a validated relative DNS name.
    /// </summary>
    /// <param name="value">The relative DNS name.</param>
    /// <returns>The validated relative DNS name.</returns>
    public static DnsRelativeName From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value == "@")
        {
            return Apex;
        }

        var labels = value.Split('.');
        if (value.Length > 253 ||
            labels.Where((label, index) =>
                    IsInvalidLabel(label, index))
                .Any())
        {
            throw new ArgumentException(
                $"'{value}' is not a relative DNS name.",
                nameof(value));
        }

        return new(value.ToLowerInvariant());
    }

    /// <summary>
    /// Creates the fully qualified hostname within a zone.
    /// </summary>
    /// <param name="zoneName">The fully qualified zone name.</param>
    /// <returns>The fully qualified hostname.</returns>
    public string ToHostname(string zoneName) =>
        IsApex ? zoneName : $"{Value}.{zoneName}";

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool IsInvalidLabel(
        string label,
        int index) =>
        label.Length is 0 or > 63 ||
        label == "*" && index is not 0 ||
        label != "*" &&
        label.Any(character =>
            !char.IsAsciiLetterOrDigit(character) &&
            character is not '-' and not '_');
}
