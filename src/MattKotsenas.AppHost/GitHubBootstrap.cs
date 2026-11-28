using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;

namespace MattKotsenas.AppHost;

internal static class GitHubBootstrap
{
    private const string Repository = "MattKotsenas/MattKotsenas";

    internal static async Task VerifyRepositoryAsync(CancellationToken cancellationToken)
    {
        var result = await Cli.Wrap("gh")
            .WithArguments(["api", $"repos/{Repository}"])
            .ExecuteBufferedAsync(cancellationToken);
        using var metadata = JsonDocument.Parse(result.StandardOutput);
        VerifyRepository(metadata.RootElement);
    }

    internal static void VerifyRepository(JsonElement repository)
    {
        var owner = repository.GetProperty("owner");
        if (repository.GetProperty("id").GetInt64() != 8395662 ||
            owner.GetProperty("id").GetInt64() != 51421 ||
            owner.GetProperty("login").GetString() != "MattKotsenas" ||
            repository.GetProperty("name").GetString() != "MattKotsenas" ||
            repository.GetProperty("full_name").GetString() != Repository)
        {
            throw new InvalidOperationException("GitHub repository metadata differs from the known immutable owner/repository IDs and subject prefix.");
        }
    }

    internal static async Task ConfigureAsync(
        string? clientId,
        string? tenantId,
        string? subscriptionId,
        string? expectedSubscriptionId,
        string? expectedTenantId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(clientId, "D", out _) ||
            !Guid.TryParseExact(tenantId, "D", out _) ||
            !Guid.TryParseExact(subscriptionId, "D", out _) ||
            !string.Equals(subscriptionId, expectedSubscriptionId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(tenantId, expectedTenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Native bootstrap outputs must contain valid IDs matching the verified Azure target.");
        }

        await VerifyRepositoryAsync(cancellationToken);
        await Cli.Wrap("gh")
            .WithArguments(["api", "--method", "PUT", $"repos/{Repository}/actions/oidc/customization/sub", "-F", "use_default=true", "-F", "use_immutable_subject=true", "--silent"])
            .ExecuteBufferedAsync(cancellationToken);
        var readback = await Cli.Wrap("gh")
            .WithArguments(["api", $"repos/{Repository}/actions/oidc/customization/sub"])
            .ExecuteBufferedAsync(cancellationToken);
        using var configuration = JsonDocument.Parse(readback.StandardOutput);
        VerifyOidcConfiguration(configuration.RootElement);

        foreach (var (name, value) in new[]
        {
            ("AZURE_CLIENT_ID", clientId),
            ("AZURE_TENANT_ID", tenantId),
            ("AZURE_SUBSCRIPTION_ID", subscriptionId),
        })
        {
            await Cli.Wrap("gh")
                .WithArguments(["variable", "set", name, "--repo", Repository, "--body", value])
                .ExecuteBufferedAsync(cancellationToken);
        }
    }

    internal static void VerifyOidcConfiguration(JsonElement configuration)
    {
        if (!configuration.GetProperty("use_default").GetBoolean() ||
            !configuration.GetProperty("use_immutable_subject").GetBoolean() ||
            configuration.GetProperty("sub_claim_prefix").GetString() != BootstrapPreflight.SubjectPrefix)
        {
            throw new InvalidOperationException("GitHub immutable OIDC readback differs from the expected default subject; variable writes stopped.");
        }
    }
}
