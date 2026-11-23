using System.Text.Json;

using MattKotsenas.AppHost;

namespace MattKotsenas.AppHost.Tests;

public sealed class DeploymentSetupTests
{
    private const string AppId = "44444444-4444-4444-4444-444444444444";
    private const string AppObjectId = "33333333-3333-3333-3333-333333333333";
    private const string ServicePrincipalId = "55555555-5555-5555-5555-555555555555";
    private const string SubscriptionId = "11111111-1111-1111-1111-111111111111";
    private const string SubscriptionScope = "/subscriptions/" + SubscriptionId;
    private const string BlogScope = SubscriptionScope + "/resourceGroups/blog";
    private const string DnsScope = SubscriptionScope + "/resourceGroups/dns";
    private const string RegistryId = BlogScope + "/providers/Microsoft.ContainerRegistry/registries/blogregistry";
    private static readonly (string Role, string Scope)[] RequiredGrants =
    [
        ("b24988ac-6180-42a0-ab88-20f7382dd24c", BlogScope),
        ("1c9b6475-caf0-4164-b5a1-2142a7116f4b", DnsScope),
        ("befefa01-2a29-4197-83a8-272ff33ce314", DnsScope + "/providers/Microsoft.Network/dnsZones/kotsenas.com"),
        ("befefa01-2a29-4197-83a8-272ff33ce314", DnsScope + "/providers/Microsoft.Network/dnsZones/matt.kotsenas.com"),
        ("acdd72a7-3385-48ef-bd42-f606fba81ae7", SubscriptionScope + "/resourceGroups/Default-Web-WestUS/providers/Microsoft.Web/sites/mattkotsenas"),
        ("f58310d9-a9f6-439a-9e8d-f62e7b41a168", RegistryId),
    ];
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
            .ConfigureAsync(applicationId: null, TestContext.Current.CancellationToken, RegistryId);

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
        AssertScopedGrants(runner);
        Assert.Collection(
            runner.Invocations.Where(invocation =>
                invocation.Arguments.StartsWith(
                    "variable set ",
                    StringComparison.Ordinal)),
            invocation => Assert.Equal(
                $"variable set AZURE_CLIENT_ID --repo MattKotsenas/MattKotsenas --body {AppId}",
                invocation.Arguments),
            invocation => Assert.Equal(
                $"variable set AZURE_DEPLOYMENT_PRINCIPAL_ID --repo MattKotsenas/MattKotsenas --body {ServicePrincipalId}",
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
                    Json(new { id = SubscriptionId, tenantId = "tenant" }),
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
                    TestContext.Current.CancellationToken, RegistryId));

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
                    TestContext.Current.CancellationToken, RegistryId));

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
            roles: RequiredGrants.Select(grant => (object)new
            {
                roleDefinitionId = RoleDefinitionId(grant.Role),
                scope = grant.Scope,
            }).ToArray());

        var configuredId = await new DeploymentSetup(runner)
            .ConfigureAsync(AppId, TestContext.Current.CancellationToken, RegistryId);

        Assert.Equal(AppId, configuredId);
        Assert.DoesNotContain(
            runner.Invocations,
            invocation =>
                invocation.Arguments.Contains(" create ", StringComparison.Ordinal) ||
                invocation.Arguments.Contains(" update ", StringComparison.Ordinal));
        Assert.Equal(
            4,
            runner.Invocations.Count(invocation =>
                invocation.Arguments.StartsWith(
                    "variable set ",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public async Task WrongScopeOrDisplayNameGrantsDoNotSatisfyRequiredGrants()
    {
        var roles = RequiredGrants.Select(grant => (object)new
        {
            roleDefinitionName = "Contributor",
            roleDefinitionId = RoleDefinitionId(grant.Role),
            scope = SubscriptionScope,
        }).Concat(RequiredGrants.Select(grant => (object)new
        {
            roleDefinitionName = "Contributor",
            roleDefinitionId = RoleDefinitionId("00000000-0000-0000-0000-000000000000"),
            scope = grant.Scope,
        })).ToArray();
        var runner = CreateExistingIdentityRunner([], roles);

        await new DeploymentSetup(runner).ConfigureAsync(AppId, TestContext.Current.CancellationToken, RegistryId);

        AssertScopedGrants(runner);
    }

    [Fact]
    public async Task RegistryResourceIdIsRequiredForExistingCallSignature()
    {
        var runner = CreateExistingIdentityRunner([]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeploymentSetup(runner).ConfigureAsync(AppId, TestContext.Current.CancellationToken));

        Assert.Contains("registry", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Invocations);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-resource-id")]
    [InlineData(SubscriptionScope)]
    [InlineData(BlogScope)]
    [InlineData("/subscriptions/99999999-9999-9999-9999-999999999999/resourceGroups/blog/providers/Microsoft.ContainerRegistry/registries/blogregistry")]
    [InlineData(SubscriptionScope + "/resourceGroups/other/providers/Microsoft.ContainerRegistry/registries/blogregistry")]
    [InlineData(BlogScope + "/providers/Microsoft.Web/sites/blogregistry")]
    [InlineData(RegistryId + "/replications/westus")]
    public async Task RegistryInputMustIdentifyBlogRegistry(string? registryId)
    {
        var runner = CreateExistingIdentityRunner([]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeploymentSetup(runner).ConfigureAsync(AppId, TestContext.Current.CancellationToken, registryId));

        Assert.Contains("registry", exception.Message, StringComparison.OrdinalIgnoreCase);
        AssertNoMutations(runner);
    }

    [Theory]
    [InlineData("registry-mismatch")]
    [InlineData("registry-missing")]
    [InlineData("blog-mismatch")]
    [InlineData("dns-mismatch")]
    [InlineData("group-missing")]
    public async Task ResourcePreflightFailsBeforeIdentityMutation(string failure)
    {
        var runner = CreateExistingIdentityRunner([], response: (command, arguments) =>
        {
            if (command == "az" && arguments is ["acr", "show", ..])
            {
                if (failure == "registry-mismatch")
                {
                    return Json(new { id = RegistryId + "other" });
                }

                if (failure == "registry-missing")
                {
                    return new CommandOutput(1, "", "registry not found");
                }
            }

            if (command == "az" && arguments is ["group", "show", ..])
            {
                if (failure == "group-missing")
                {
                    return new CommandOutput(1, "", "group not found");
                }

                if (failure == arguments[3] + "-mismatch")
                {
                    return Json(new { id = BlogScope + "other" });
                }
            }

            return null;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeploymentSetup(runner).ConfigureAsync(AppId, TestContext.Current.CancellationToken, RegistryId));

        AssertNoMutations(runner);
    }

    [Fact]
    public async Task RoleFailureStopsAssignmentsAndVariableWrites()
    {
        var runner = CreateExistingIdentityRunner([], response: (command, arguments) =>
            command == "az" && arguments is ["role", "assignment", "create", ..]
                ? new CommandOutput(1, "", "role assignment denied")
                : null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeploymentSetup(runner).ConfigureAsync(AppId, TestContext.Current.CancellationToken, RegistryId));

        Assert.Contains("role assignment denied", exception.Message, StringComparison.Ordinal);
        Assert.Single(runner.Invocations, invocation => invocation.Arguments.StartsWith("role assignment create ", StringComparison.Ordinal));
        Assert.DoesNotContain(runner.Invocations, invocation => invocation.Arguments.StartsWith("variable set ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task ChangedApplicationBlocksNextGrant(bool changeObjectId, int successfulGrants)
    {
        var reads = 0;
        var runner = CreateExistingIdentityRunner([], response: (command, arguments) =>
        {
            if (command != "az" || arguments is not ["ad", "app", "show", ..])
            {
                return null;
            }

            reads++;
            return reads > successfulGrants + 1
                ? Json(new
                {
                    id = changeObjectId ? "66666666-6666-6666-6666-666666666666" : AppObjectId,
                    appId = changeObjectId ? AppId : "77777777-7777-7777-7777-777777777777",
                })
                : Json(Application());
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeploymentSetup(runner).ConfigureAsync(AppId, TestContext.Current.CancellationToken, RegistryId));

        Assert.Contains("changed during setup", exception.Message, StringComparison.Ordinal);
        Assert.Equal(successfulGrants, runner.Invocations.Count(invocation => invocation.Arguments.StartsWith("role assignment create ", StringComparison.Ordinal)));
        Assert.DoesNotContain(runner.Invocations, invocation => invocation.Arguments.StartsWith("variable set ", StringComparison.Ordinal));
    }

    private static void AssertScopedGrants(RecordingCommandRunner runner)
    {
        var creates = runner.Invocations.Where(invocation =>
            invocation.Arguments.StartsWith("role assignment create ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(RequiredGrants.Length, creates.Length);
        foreach (var (role, scope) in RequiredGrants)
        {
            Assert.Contains(creates, invocation => invocation.Command == "az" &&
                invocation.Arguments == $"role assignment create --assignee-object-id {ServicePrincipalId} --assignee-principal-type ServicePrincipal --role {role} --scope {scope} --subscription {SubscriptionId} --only-show-errors --output none");
            Assert.Contains(runner.Invocations, invocation => invocation.Arguments ==
                $"role assignment list --assignee {ServicePrincipalId} --scope {scope} --subscription {SubscriptionId} --only-show-errors --output json");
        }

        Assert.DoesNotContain(creates, invocation => invocation.Arguments.Contains($"--scope {SubscriptionScope} --", StringComparison.Ordinal));
        Assert.DoesNotContain(runner.Invocations, invocation => invocation.Arguments.Contains(" delete ", StringComparison.Ordinal));
    }

    private static void AssertNoMutations(RecordingCommandRunner runner) =>
        Assert.DoesNotContain(runner.Invocations, invocation =>
            invocation.Arguments.Contains(" create ", StringComparison.Ordinal) ||
            invocation.Arguments.Contains(" update ", StringComparison.Ordinal) ||
            invocation.Arguments.Contains(" set ", StringComparison.Ordinal) ||
            invocation.Arguments.Contains("--method PUT", StringComparison.Ordinal));

    private static string RoleDefinitionId(string role) =>
        SubscriptionScope + "/providers/Microsoft.Authorization/roleDefinitions/" + role;

    private static CommandOutput? ResourceRead(string command, IReadOnlyList<string> arguments)
    {
        if (command != "az")
        {
            return null;
        }

        if (arguments is ["acr", "show", ..])
        {
            Assert.Equal($"acr show --ids {RegistryId} --subscription {SubscriptionId} --only-show-errors --output json", string.Join(' ', arguments));
            return Json(new { id = RegistryId });
        }

        if (arguments is ["group", "show", ..])
        {
            Assert.Equal($"group show --name {arguments[3]} --subscription {SubscriptionId} --only-show-errors --output json", string.Join(' ', arguments));
            Assert.True(arguments[3] is "blog" or "dns");
            return Json(new { id = SubscriptionScope + "/resourceGroups/" + arguments[3] });
        }

        return null;
    }

    private static RecordingCommandRunner CreateExistingIdentityRunner(
        object[] federatedCredentials,
        object[]? roles = null,
        Func<string, IReadOnlyList<string>, CommandOutput?>? response = null) =>
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
        }, response);

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
        Func<string, IReadOnlyList<string>, CommandOutput> handler,
        Func<string, IReadOnlyList<string>, CommandOutput?>? responseOverride = null)
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
            return Task.FromResult(responseOverride?.Invoke(command, arguments) ?? ResourceRead(command, arguments) ?? handler(command, arguments));
        }
    }
}
