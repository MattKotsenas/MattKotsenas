using System.Text.Json;

using MattKotsenas.AppHost;

namespace MattKotsenas.AppHost.Tests;

public sealed class DeploymentSetupTests
{
    private const string AppId = "44444444-4444-4444-4444-444444444444";
    private const string AppObjectId = "33333333-3333-3333-3333-333333333333";
    private const string ServicePrincipalId = "55555555-5555-5555-5555-555555555555";
    private static readonly string[] AzureAdTokenExchangeAudience =
        ["api://AzureADTokenExchange"];

    [Fact]
    public async Task ConfigureCreatesImmutableDeploymentIdentity()
    {
        var applicationCreated = false;
        var runner = new RecordingCommandRunner((command, arguments) =>
        {
            var invocation = string.Join(' ', arguments);
            return (command, invocation) switch
            {
                ("az", "account show --only-show-errors --output json") =>
                    Json(new
                    {
                        id = "11111111-1111-1111-1111-111111111111",
                        tenantId = "22222222-2222-2222-2222-222222222222",
                    }),
                ("gh", "api repos/MattKotsenas/MattKotsenas") =>
                    Json(new
                    {
                        id = 456,
                        name = "MattKotsenas",
                        owner = new { id = 123, login = "MattKotsenas" },
                    }),
                ("az", _) when invocation.StartsWith(
                    "ad app list ",
                    StringComparison.Ordinal) =>
                    Json(applicationCreated
                        ? new[] { Application() }
                        : []),
                ("az", _) when invocation.StartsWith(
                    "ad app create ",
                    StringComparison.Ordinal) =>
                    CreateApplication(),
                ("az", _) when invocation.StartsWith(
                    "ad app show ",
                    StringComparison.Ordinal) =>
                    Json(Application()),
                ("az", _) when invocation.StartsWith(
                    "ad sp list ",
                    StringComparison.Ordinal) =>
                    Json(Array.Empty<object>()),
                ("az", _) when invocation.StartsWith(
                    "ad sp create ",
                    StringComparison.Ordinal) =>
                    Json(new { id = ServicePrincipalId, appId = AppId }),
                ("az", _) when invocation.StartsWith(
                    "ad app federated-credential list ",
                    StringComparison.Ordinal) =>
                    Json(Array.Empty<object>()),
                ("az", _) when invocation.StartsWith(
                    "role assignment list ",
                    StringComparison.Ordinal) =>
                    Json(Array.Empty<object>()),
                ("gh", _) when invocation.StartsWith(
                    "api --method PUT ",
                    StringComparison.Ordinal) =>
                    Success(),
                ("gh", "api repos/MattKotsenas/MattKotsenas/actions/oidc/customization/sub") =>
                    Json(new
                    {
                        use_default = true,
                        use_immutable_subject = true,
                        sub_claim_prefix = "repo:MattKotsenas@123/MattKotsenas@456",
                    }),
                _ => Success(),
            };

            CommandOutput CreateApplication()
            {
                applicationCreated = true;
                return Json(Application());
            }
        });

        var configuredId = await new DeploymentSetup(runner)
            .ConfigureAsync(applicationId: null, TestContext.Current.CancellationToken);

        Assert.Equal(AppId, configuredId);
        Assert.Contains(
            runner.Invocations,
            invocation =>
                invocation.Command == "az" &&
                invocation.Arguments.Contains(
                    "repo:MattKotsenas@123/MattKotsenas@456:ref:refs/heads/main",
                    StringComparison.Ordinal));
        var federatedCredentialIndex = runner.Invocations.FindIndex(invocation =>
            invocation.Arguments.StartsWith(
                "ad app federated-credential create ",
                StringComparison.Ordinal));
        var immutableSubjectIndex = runner.Invocations.FindIndex(invocation =>
            invocation.Arguments.StartsWith(
                "api --method PUT ",
                StringComparison.Ordinal));
        var firstRoleIndex = runner.Invocations.FindIndex(invocation =>
            invocation.Arguments.StartsWith(
                "role assignment create ",
                StringComparison.Ordinal));
        Assert.True(federatedCredentialIndex >= 0);
        Assert.True(immutableSubjectIndex > federatedCredentialIndex);
        Assert.True(firstRoleIndex > immutableSubjectIndex);
        Assert.Collection(
            runner.Invocations.Where(invocation =>
                invocation.Arguments.StartsWith(
                    "role assignment create ",
                    StringComparison.Ordinal)),
            invocation => Assert.Contains(
                "--role Contributor ",
                invocation.Arguments,
                StringComparison.Ordinal),
            invocation => Assert.Contains(
                "--role Role Based Access Control Administrator ",
                invocation.Arguments,
                StringComparison.Ordinal));
        Assert.Collection(
            runner.Invocations.Where(invocation =>
                invocation.Arguments.StartsWith(
                    "variable set ",
                    StringComparison.Ordinal)),
            invocation => Assert.Equal(
                $"variable set AZURE_CLIENT_ID --repo MattKotsenas/MattKotsenas --body {AppId}",
                invocation.Arguments),
            invocation => Assert.Equal(
                "variable set AZURE_TENANT_ID --repo MattKotsenas/MattKotsenas --body 22222222-2222-2222-2222-222222222222",
                invocation.Arguments),
            invocation => Assert.Equal(
                "variable set AZURE_SUBSCRIPTION_ID --repo MattKotsenas/MattKotsenas --body 11111111-1111-1111-1111-111111111111",
                invocation.Arguments));
    }

    [Fact]
    public async Task ConfigureRejectsExistingNameWithoutApplicationId()
    {
        var runner = new RecordingCommandRunner((command, arguments) =>
        {
            var invocation = string.Join(' ', arguments);
            return (command, invocation) switch
            {
                ("az", "account show --only-show-errors --output json") =>
                    Json(new { id = "subscription", tenantId = "tenant" }),
                ("gh", "api repos/MattKotsenas/MattKotsenas") =>
                    Json(new
                    {
                        id = 456,
                        name = "MattKotsenas",
                        owner = new { id = 123, login = "MattKotsenas" },
                    }),
                ("az", _) when invocation.StartsWith(
                    "ad app list ",
                    StringComparison.Ordinal) =>
                    Json(new[] { Application() }),
                _ => throw new InvalidOperationException(
                    $"Unexpected invocation: {command} {invocation}"),
            };
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new DeploymentSetup(runner)
                .ConfigureAsync(
                    applicationId: null,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "provide its application ID",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            runner.Invocations,
            invocation => invocation.Arguments.StartsWith(
                "role assignment create ",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfigureRejectsInvalidApplicationIdBeforeRunningCommands()
    {
        var runner = new RecordingCommandRunner((command, arguments) =>
            throw new InvalidOperationException(
                $"Unexpected invocation: {command} {string.Join(' ', arguments)}"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new DeploymentSetup(runner)
                .ConfigureAsync(
                    "not-an-application-id",
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "must be a GUID",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task ConfigureRejectsUnexpectedFederatedCredential()
    {
        var runner = CreateExistingIdentityRunner(
            federatedCredentials:
            [
                new
                {
                    name = "unexpected",
                    issuer = "https://token.actions.githubusercontent.com",
                    subject = "repo:old/subject",
                    description = "unexpected",
                    audiences = AzureAdTokenExchangeAudience,
                },
            ]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new DeploymentSetup(runner)
                .ConfigureAsync(
                    AppId,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "unrecognized federated credentials",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            runner.Invocations,
            invocation => invocation.Arguments.StartsWith(
                "role assignment create ",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfigureExistingIdentityIsIdempotent()
    {
        var runner = CreateExistingIdentityRunner(
            federatedCredentials:
            [
                new
                {
                    name = "github-main-immutable",
                    issuer = "https://token.actions.githubusercontent.com",
                    subject = "repo:MattKotsenas@123/MattKotsenas@456:ref:refs/heads/main",
                    description = "GitHub Actions deployment from the main branch",
                    audiences = AzureAdTokenExchangeAudience,
                },
            ],
            roles:
            [
                new { roleDefinitionName = "Contributor" },
                new
                {
                    roleDefinitionName =
                        "Role Based Access Control Administrator",
                },
            ]);

        var configuredId = await new DeploymentSetup(runner)
            .ConfigureAsync(AppId, TestContext.Current.CancellationToken);

        Assert.Equal(AppId, configuredId);
        Assert.DoesNotContain(
            runner.Invocations,
            invocation =>
                invocation.Arguments.Contains(" create ", StringComparison.Ordinal) ||
                invocation.Arguments.Contains(" update ", StringComparison.Ordinal));
        Assert.Equal(
            3,
            runner.Invocations.Count(invocation =>
                invocation.Arguments.StartsWith(
                    "variable set ",
                    StringComparison.Ordinal)));
    }

    private static RecordingCommandRunner CreateExistingIdentityRunner(
        object[] federatedCredentials,
        object[]? roles = null) =>
        new((command, arguments) =>
        {
            var invocation = string.Join(' ', arguments);
            return (command, invocation) switch
            {
                ("az", "account show --only-show-errors --output json") =>
                    Json(new
                    {
                        id = "11111111-1111-1111-1111-111111111111",
                        tenantId = "22222222-2222-2222-2222-222222222222",
                    }),
                ("gh", "api repos/MattKotsenas/MattKotsenas") =>
                    Json(new
                    {
                        id = 456,
                        name = "MattKotsenas",
                        owner = new { id = 123, login = "MattKotsenas" },
                    }),
                ("az", _) when invocation.StartsWith(
                    "ad app show ",
                    StringComparison.Ordinal) =>
                    Json(Application()),
                ("az", _) when invocation.StartsWith(
                    "ad sp list ",
                    StringComparison.Ordinal) =>
                    Json(
                    new[]
                    {
                        new { id = ServicePrincipalId, appId = AppId },
                    }),
                ("az", _) when invocation.StartsWith(
                    "ad app federated-credential list ",
                    StringComparison.Ordinal) =>
                    Json(federatedCredentials),
                ("az", _) when invocation.StartsWith(
                    "role assignment list ",
                    StringComparison.Ordinal) =>
                    Json(roles ?? []),
                ("gh", _) when invocation.StartsWith(
                    "api --method PUT ",
                    StringComparison.Ordinal) =>
                    Success(),
                ("gh", "api repos/MattKotsenas/MattKotsenas/actions/oidc/customization/sub") =>
                    Json(new
                    {
                        use_default = true,
                        use_immutable_subject = true,
                        sub_claim_prefix = "repo:MattKotsenas@123/MattKotsenas@456",
                    }),
                _ => Success(),
            };
        });

    private static object Application() =>
        new
        {
            id = AppObjectId,
            appId = AppId,
            displayName = "github-MattKotsenas-blog",
        };

    private static CommandOutput Json(object value) =>
        new(0, JsonSerializer.Serialize(value), string.Empty);

    private static CommandOutput Success() =>
        new(0, string.Empty, string.Empty);

    private sealed class RecordingCommandRunner(
        Func<string, IReadOnlyList<string>, CommandOutput> handler)
        : ICommandRunner
    {
        public List<(string Command, string Arguments)> Invocations { get; } = [];

        public Task<CommandOutput> RunAsync(
            string command,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invocations.Add((command, string.Join(' ', arguments)));
            return Task.FromResult(handler(command, arguments));
        }
    }
}
