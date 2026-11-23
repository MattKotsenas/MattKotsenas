using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Configuration;

namespace MattKotsenas.Bootstrap.AppHost;

internal static class BootstrapPreflight
{
    internal const string ResourceGroup = "blog";
    internal const string IdentityName = "github-MattKotsenas-blog";
    internal const string CredentialName = "github-main-immutable";
    internal const string Issuer = "https://token.actions.githubusercontent.com";
    internal const string Audience = "api://AzureADTokenExchange";
    internal const string SubjectPrefix = "repo:MattKotsenas@51421/MattKotsenas@8395662";
    internal const string Subject = SubjectPrefix + ":ref:refs/heads/main";

    internal static async Task<string> VerifyAsync(IConfiguration configuration, IEnumerable<ParameterResource> requiredGroups, CancellationToken cancellationToken)
    {
        var subscriptionId = configuration["Azure:SubscriptionId"];
        var configuredTenantId = configuration["Azure:TenantId"];
        if (!Guid.TryParseExact(subscriptionId, "D", out _) ||
            string.IsNullOrWhiteSpace(configuration["Azure:Location"]) ||
            !string.Equals(configuration["Azure:ResourceGroup"], ResourceGroup, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Bootstrap requires Azure:SubscriptionId (a GUID), Azure:Location, and resource group 'blog'.");
        }

        await GitHubBootstrap.VerifyRepositoryAsync(cancellationToken);
        var accountResult = await Cli.Wrap("az")
            .WithArguments(["account", "show", "--subscription", subscriptionId, "--only-show-errors", "--output", "json"])
            .ExecuteBufferedAsync(cancellationToken);
        using var account = JsonDocument.Parse(accountResult.StandardOutput);
        var tenantId = account.RootElement.GetProperty("tenantId").GetString();
        if (!string.Equals(account.RootElement.GetProperty("id").GetString(), subscriptionId, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(tenantId, "D", out _) ||
            (configuredTenantId is not null && !string.Equals(configuredTenantId, tenantId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Azure CLI account does not match the native Azure subscription and tenant configuration.");
        }

        foreach (var requiredGroup in requiredGroups)
        {
            var name = await requiredGroup.GetValueAsync(cancellationToken);
            await Cli.Wrap("az")
                .WithArguments(["group", "show", "--name", name!, "--subscription", subscriptionId, "--only-show-errors", "--output", "none"])
                .ExecuteBufferedAsync(cancellationToken);
        }

        var groupResult = await Cli.Wrap("az")
            .WithArguments(["group", "exists", "--name", ResourceGroup, "--subscription", subscriptionId, "--only-show-errors", "--output", "json"])
            .ExecuteBufferedAsync(cancellationToken);
        using var group = JsonDocument.Parse(groupResult.StandardOutput);
        if (!group.RootElement.GetBoolean())
        {
            return tenantId;
        }

        var identitiesResult = await Cli.Wrap("az")
            .WithArguments(["identity", "list", "--resource-group", ResourceGroup, "--subscription", subscriptionId, "--only-show-errors", "--output", "json"])
            .ExecuteBufferedAsync(cancellationToken);
        using var identities = JsonDocument.Parse(identitiesResult.StandardOutput);
        var matches = identities.RootElement.EnumerateArray()
            .Where(identity => string.Equals(identity.GetProperty("name").GetString(), IdentityName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length is 0)
        {
            return tenantId;
        }

        var expectedId = $"/subscriptions/{subscriptionId}/resourceGroups/{ResourceGroup}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/{IdentityName}";
        if (matches.Length != 1 ||
            !string.Equals(matches[0].GetProperty("id").GetString(), expectedId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(matches[0].GetProperty("tenantId").GetString(), tenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Existing bootstrap identity does not match the expected Azure resource and tenant.");
        }

        var credentialsResult = await Cli.Wrap("az")
            .WithArguments(["identity", "federated-credential", "list", "--identity-name", IdentityName, "--resource-group", ResourceGroup, "--subscription", subscriptionId, "--only-show-errors", "--output", "json"])
            .ExecuteBufferedAsync(cancellationToken);
        using var credentials = JsonDocument.Parse(credentialsResult.StandardOutput);
        VerifyCredentials(credentials.RootElement);
        return tenantId;
    }

    internal static void VerifyCredentials(JsonElement credentials)
    {
        if (credentials.GetArrayLength() is 0)
        {
            return; // A previous deployment may have created the identity before its credential.
        }

        if (credentials.GetArrayLength() != 1)
        {
            throw new InvalidOperationException("Refusing to adopt an existing bootstrap identity with unexpected federated credentials.");
        }

        var credential = credentials[0];
        if (credential.GetProperty("name").GetString() != CredentialName ||
            credential.GetProperty("issuer").GetString() != Issuer ||
            credential.GetProperty("subject").GetString() != Subject ||
            !credential.GetProperty("audiences").EnumerateArray().Select(value => value.GetString()).SequenceEqual([Audience], StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Existing bootstrap identity has unexpected federated trust. No Azure grants will be applied.");
        }
    }
}
