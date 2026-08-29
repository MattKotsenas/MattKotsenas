namespace MattKotsenas.AppHost;

internal static class DeploymentPrincipal
{
    public static string ParseObjectId(string? value)
    {
        if (!Guid.TryParseExact(value, "D", out var objectId))
        {
            throw new InvalidOperationException(
                "DeploymentPrincipalId must contain the deployment principal's object ID.");
        }

        return objectId.ToString("D");
    }
}
