using System.Text.Json;
using System.Text.Json.Serialization;

using Aspire.Hosting.ApplicationModel;

namespace MattKotsenas.AppHost;

internal sealed class DeploymentSetup(ICommandRunner runner)
{
    private const string ApplicationName = "github-MattKotsenas-blog";
    private const string CredentialName = "github-main-immutable";
    private const string Repository = "MattKotsenas/MattKotsenas";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static async Task<ExecuteCommandResult> ExecuteAsync(
        ExecuteCommandContext context)
    {
        try
        {
            var applicationId = context.Arguments.GetString("applicationId");
            var configuredApplicationId = await new DeploymentSetup(
                    new CliWrapCommandRunner())
                .ConfigureAsync(applicationId, context.CancellationToken);

            return CommandResults.Success(
                $"Configured application '{configuredApplicationId}' for deployments from the main branch.");
        }
        catch (OperationCanceledException)
            when (context.CancellationToken.IsCancellationRequested)
        {
            return CommandResults.Canceled();
        }
        catch (InvalidOperationException exception)
        {
            return CommandResults.Failure(exception.Message);
        }
    }

    internal async Task<string> ConfigureAsync(
        string? applicationId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(applicationId) &&
            !Guid.TryParseExact(applicationId, "D", out _))
        {
            throw new InvalidOperationException(
                "The application ID must be a GUID in 00000000-0000-0000-0000-000000000000 format.");
        }

        var account = await RunJsonAsync<AzureAccount>(
            "az",
            cancellationToken,
            "account", "show",
            "--only-show-errors",
            "--output", "json");
        var repository = await RunJsonAsync<GitHubRepository>(
            "gh",
            cancellationToken,
            "api", $"repos/{Repository}");
        var canonicalRepository = $"{repository.Owner.Login}/{repository.Name}";
        var immutableSubjectPrefix =
            $"repo:{repository.Owner.Login}@{repository.Owner.Id}/{repository.Name}@{repository.Id}";
        var scope = $"/subscriptions/{account.Id}";

        var application = string.IsNullOrWhiteSpace(applicationId)
            ? await CreateApplicationAsync(cancellationToken)
            : await GetApplicationAsync(applicationId, cancellationToken);
        var servicePrincipal = await GetOrCreateServicePrincipalAsync(
            application.AppId,
            cancellationToken);

        await ConfigureFederatedCredentialAsync(
            application,
            immutableSubjectPrefix,
            cancellationToken);
        await EnableImmutableSubjectAsync(
            canonicalRepository,
            immutableSubjectPrefix,
            cancellationToken);
        await ConfigureRolesAsync(
            application,
            servicePrincipal,
            scope,
            cancellationToken);
        await SetRepositoryVariablesAsync(
            canonicalRepository,
            application.AppId,
            account,
            cancellationToken);

        return application.AppId;
    }

    private async Task<EntraApplication> CreateApplicationAsync(
        CancellationToken cancellationToken)
    {
        var existing = (await ListApplicationsAsync(cancellationToken))
            .Where(application =>
                string.Equals(
                    application.DisplayName,
                    ApplicationName,
                    StringComparison.Ordinal))
            .ToArray();
        if (existing.Length != 0)
        {
            var applicationIds = string.Join(
                ", ",
                existing.Select(application => application.AppId));
            throw new InvalidOperationException(
                $"An Entra application named '{ApplicationName}' already exists. " +
                "Verify its ownership, then provide its application ID. " +
                $"Matching application IDs: {applicationIds}");
        }

        var created = await RunJsonAsync<EntraApplication>(
            "az",
            cancellationToken,
            "ad", "app", "create",
            "--display-name", ApplicationName,
            "--sign-in-audience", "AzureADMyOrg",
            "--only-show-errors",
            "--output", "json");
        var matches = (await ListApplicationsAsync(cancellationToken))
            .Where(application =>
                string.Equals(
                    application.DisplayName,
                    ApplicationName,
                    StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1 ||
            !string.Equals(
                matches[0].AppId,
                created.AppId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Application-name collision detected for '{ApplicationName}'. " +
                "No roles were assigned.");
        }

        return created;
    }

    private Task<EntraApplication[]> ListApplicationsAsync(
        CancellationToken cancellationToken) =>
        RunJsonAsync<EntraApplication[]>(
            "az",
            cancellationToken,
            "ad", "app", "list",
            "--display-name", ApplicationName,
            "--only-show-errors",
            "--output", "json");

    private Task<EntraApplication> GetApplicationAsync(
        string applicationId,
        CancellationToken cancellationToken) =>
        RunJsonAsync<EntraApplication>(
            "az",
            cancellationToken,
            "ad", "app", "show",
            "--id", applicationId,
            "--only-show-errors",
            "--output", "json");

    private async Task<ServicePrincipal> GetOrCreateServicePrincipalAsync(
        string applicationId,
        CancellationToken cancellationToken)
    {
        var servicePrincipals = await RunJsonAsync<ServicePrincipal[]>(
            "az",
            cancellationToken,
            "ad", "sp", "list",
            "--filter", $"appId eq '{applicationId}'",
            "--only-show-errors",
            "--output", "json");

        return servicePrincipals.Length switch
        {
            0 => await RunJsonAsync<ServicePrincipal>(
                "az",
                cancellationToken,
                "ad", "sp", "create",
                "--id", applicationId,
                "--only-show-errors",
                "--output", "json"),
            1 => servicePrincipals[0],
            _ => throw new InvalidOperationException(
                $"More than one service principal uses application ID '{applicationId}'."),
        };
    }

    private async Task ConfigureFederatedCredentialAsync(
        EntraApplication application,
        string immutableSubjectPrefix,
        CancellationToken cancellationToken)
    {
        var credentials = await RunJsonAsync<FederatedCredential[]>(
            "az",
            cancellationToken,
            "ad", "app", "federated-credential", "list",
            "--id", application.Id,
            "--only-show-errors",
            "--output", "json");
        var unexpectedCredentials = credentials
            .Where(credential =>
                !string.Equals(
                    credential.Name,
                    CredentialName,
                    StringComparison.Ordinal))
            .Select(credential => credential.Name)
            .ToArray();
        if (unexpectedCredentials.Length != 0)
        {
            throw new InvalidOperationException(
                "The deployment application contains unrecognized federated credentials: " +
                string.Join(", ", unexpectedCredentials));
        }

        var matchingCredentials = credentials
            .Where(credential =>
                string.Equals(
                    credential.Name,
                    CredentialName,
                    StringComparison.Ordinal))
            .ToArray();
        if (matchingCredentials.Length > 1)
        {
            throw new InvalidOperationException(
                $"The deployment application contains duplicate '{CredentialName}' credentials.");
        }

        var desired = new FederatedCredential(
            CredentialName,
            "https://token.actions.githubusercontent.com",
            $"{immutableSubjectPrefix}:ref:refs/heads/main",
            "GitHub Actions deployment from the main branch",
            ["api://AzureADTokenExchange"]);
        var current = matchingCredentials.SingleOrDefault();
        if (current is not null &&
            string.Equals(
                current.Issuer,
                desired.Issuer,
                StringComparison.Ordinal) &&
            string.Equals(
                current.Subject,
                desired.Subject,
                StringComparison.Ordinal) &&
            string.Equals(
                current.Description,
                desired.Description,
                StringComparison.Ordinal) &&
            current.Audiences.SequenceEqual(
                desired.Audiences,
                StringComparer.Ordinal))
        {
            return;
        }

        object credentialParameters =
            current is null
                ? desired
                : new FederatedCredentialUpdate(
                    desired.Issuer,
                    desired.Subject,
                    desired.Description,
                    desired.Audiences);
        var parameters = JsonSerializer.Serialize(
            credentialParameters,
            JsonOptions);
        if (current is null)
        {
            await RunAsync(
                "az",
                cancellationToken,
                "ad", "app", "federated-credential", "create",
                "--id", application.Id,
                "--parameters", parameters,
                "--only-show-errors",
                "--output", "none");
            return;
        }

        await RunAsync(
            "az",
            cancellationToken,
            "ad", "app", "federated-credential", "update",
            "--id", application.Id,
            "--federated-credential-id", CredentialName,
            "--parameters", parameters,
            "--only-show-errors",
            "--output", "none");
    }

    private async Task EnableImmutableSubjectAsync(
        string canonicalRepository,
        string immutableSubjectPrefix,
        CancellationToken cancellationToken)
    {
        await RunAsync(
            "gh",
            cancellationToken,
            "api",
            "--method", "PUT",
            $"repos/{canonicalRepository}/actions/oidc/customization/sub",
            "-F", "use_default=true",
            "-F", "use_immutable_subject=true",
            "--silent");
        var configuration = await RunJsonAsync<OidcConfiguration>(
            "gh",
            cancellationToken,
            "api",
            $"repos/{canonicalRepository}/actions/oidc/customization/sub");
        if (!configuration.UseDefault ||
            !configuration.UseImmutableSubject ||
            !string.Equals(
                configuration.SubjectClaimPrefix,
                immutableSubjectPrefix,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "GitHub did not enable the expected immutable default OIDC subject.");
        }
    }

    private async Task ConfigureRolesAsync(
        EntraApplication application,
        ServicePrincipal servicePrincipal,
        string scope,
        CancellationToken cancellationToken)
    {
        var roleAssignments = await RunJsonAsync<RoleAssignment[]>(
            "az",
            cancellationToken,
            "role", "assignment", "list",
            "--assignee", servicePrincipal.Id,
            "--scope", scope,
            "--only-show-errors",
            "--output", "json");

        foreach (var role in new[]
                 {
                     "Contributor",
                     "Role Based Access Control Administrator",
                 })
        {
            if (roleAssignments.Any(assignment =>
                    string.Equals(
                        assignment.RoleDefinitionName,
                        role,
                        StringComparison.Ordinal)))
            {
                continue;
            }

            var verified = await GetApplicationAsync(
                application.AppId,
                cancellationToken);
            if (!string.Equals(
                    verified.AppId,
                    application.AppId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The deployment application changed during setup. " +
                    "No role was assigned.");
            }

            await RunAsync(
                "az",
                cancellationToken,
                "role", "assignment", "create",
                "--assignee-object-id", servicePrincipal.Id,
                "--assignee-principal-type", "ServicePrincipal",
                "--role", role,
                "--scope", scope,
                "--only-show-errors",
                "--output", "none");
        }
    }

    private async Task SetRepositoryVariablesAsync(
        string canonicalRepository,
        string applicationId,
        AzureAccount account,
        CancellationToken cancellationToken)
    {
        await SetRepositoryVariableAsync(
            canonicalRepository,
            "AZURE_CLIENT_ID",
            applicationId,
            cancellationToken);
        await SetRepositoryVariableAsync(
            canonicalRepository,
            "AZURE_TENANT_ID",
            account.TenantId,
            cancellationToken);
        await SetRepositoryVariableAsync(
            canonicalRepository,
            "AZURE_SUBSCRIPTION_ID",
            account.Id,
            cancellationToken);
    }

    private Task<string> SetRepositoryVariableAsync(
        string canonicalRepository,
        string name,
        string value,
        CancellationToken cancellationToken) =>
        RunAsync(
            "gh",
            cancellationToken,
            "variable", "set", name,
            "--repo", canonicalRepository,
            "--body", value);

    private async Task<T> RunJsonAsync<T>(
        string command,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var output = await RunAsync(command, cancellationToken, arguments);
        return JsonSerializer.Deserialize<T>(output, JsonOptions)
            ?? throw new InvalidOperationException(
                $"Command '{command}' returned no JSON.");
    }

    private async Task<string> RunAsync(
        string command,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var result = await runner.RunAsync(
            command,
            arguments,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError)
                ? "No error output was returned."
                : result.StandardError.Trim();
            throw new InvalidOperationException(
                $"Command '{command} {string.Join(' ', arguments)}' failed: {error}");
        }

        return result.StandardOutput;
    }

    private sealed record AzureAccount(
        string Id,
        string TenantId);

    private sealed record GitHubRepository(
        long Id,
        string Name,
        GitHubOwner Owner);

    private sealed record GitHubOwner(
        long Id,
        string Login);

    private sealed record EntraApplication(
        string Id,
        string AppId,
        string? DisplayName);

    private sealed record ServicePrincipal(
        string Id,
        string AppId);

    private sealed record FederatedCredential(
        string Name,
        string Issuer,
        string Subject,
        string Description,
        string[] Audiences);

    private sealed record FederatedCredentialUpdate(
        string Issuer,
        string Subject,
        string Description,
        string[] Audiences);

    private sealed record RoleAssignment(
        string RoleDefinitionName);

    private sealed record OidcConfiguration(
        [property: JsonPropertyName("use_default")]
        bool UseDefault,
        [property: JsonPropertyName("use_immutable_subject")]
        bool UseImmutableSubject,
        [property: JsonPropertyName("sub_claim_prefix")]
        string SubjectClaimPrefix);
}
