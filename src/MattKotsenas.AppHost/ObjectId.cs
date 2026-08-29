using Vogen;

namespace MattKotsenas.AppHost;

[ValueObject<Guid>]
internal readonly partial struct ObjectId
{
    public static ObjectId FromString(string? value)
    {
        if (!Guid.TryParseExact(value, "D", out var objectId))
        {
            throw new InvalidOperationException(
                "Object ID must be a canonical GUID.");
        }

        return From(objectId);
    }
}
