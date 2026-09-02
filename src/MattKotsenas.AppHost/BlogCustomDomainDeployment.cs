using System.Text.Json;

using Azure.Core;

namespace MattKotsenas.AppHost;

internal sealed class BlogCustomDomainDeployment(
    ICommandRunner runner,
    string azureSubscriptionId,
    string azureResourceGroup,
    string dnsResourceGroup,
    string appName,
    TimeSpan? pollInterval = null,
    TimeSpan? provisioningTimeout = null)
{
    private readonly TimeSpan pollInterval =
        pollInterval ?? TimeSpan.FromSeconds(10);
    private readonly TimeSpan provisioningTimeout =
        provisioningTimeout ?? TimeSpan.FromMinutes(20);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    internal async Task RecoverAsync(
        CancellationToken cancellationToken)
    {
        var app = await GetContainerAppAsync(cancellationToken);
        if (app is null)
        {
            return;
        }

        RejectUnmodeledCustomDomainBindings(app);

        var environmentId = new ResourceIdentifier(app.EnvironmentId);
        var environmentResourceGroup = environmentId.ResourceGroupName
            ?? throw new InvalidOperationException(
                $"Container Apps environment '{app.EnvironmentId}' has no resource group.");

        await DeleteUnboundTerminalCertificatesAsync(
            app,
            environmentResourceGroup,
            environmentId.Name,
            cancellationToken);
    }

    internal async Task PublishValidationAndWaitForCertificatesAsync(
        CancellationToken cancellationToken)
    {
        var app = await WaitForContainerAppAsync(cancellationToken);
        var environmentId = new ResourceIdentifier(app.EnvironmentId);
        var environmentResourceGroup = environmentId.ResourceGroupName
            ?? throw new InvalidOperationException(
                $"Container Apps environment '{app.EnvironmentId}' has no resource group.");

        await PublishValidationAndWaitForCertificatesAsync(
            environmentResourceGroup,
            environmentId.Name,
            cancellationToken);
    }

    internal async Task VerifyCurrentDeploymentAsync(
        CancellationToken cancellationToken)
    {
        var app = await WaitForContainerAppAsync(cancellationToken);
        await WaitForBindingsAndProbeAsync(
            app.EnvironmentId,
            cancellationToken);
    }

    private void RejectUnmodeledCustomDomainBindings(
        ContainerAppDetails app)
    {
        var domains = BlogCustomDomains.All.ToDictionary(
            domain => domain.Hostname,
            StringComparer.OrdinalIgnoreCase);
        var unmodeledBindings = (app.CustomDomains ?? [])
            .Where(binding =>
            {
                if (!domains.TryGetValue(binding.Name, out var domain))
                {
                    return true;
                }

                var expectedCertificateId =
                    $"{app.EnvironmentId}/managedCertificates/{domain.CertificateName}";
                return binding.BindingType switch
                {
                    "Auto" =>
                        !string.IsNullOrWhiteSpace(
                            binding.CertificateId) &&
                        !string.Equals(
                            binding.CertificateId,
                            expectedCertificateId,
                            StringComparison.OrdinalIgnoreCase),
                    "SniEnabled" =>
                        !string.Equals(
                            binding.CertificateId,
                            expectedCertificateId,
                            StringComparison.OrdinalIgnoreCase),
                    _ => true,
                };
            })
            .Select(binding =>
                $"{binding.Name} ({binding.BindingType}, {binding.CertificateId ?? "no certificate"})")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unmodeledBindings.Length > 0)
        {
            throw new InvalidOperationException(
                $"Container App '{appName}' has custom domain bindings outside the BlogCustomDomains model: {string.Join(", ", unmodeledBindings)}.");
        }
    }

    private async Task DeleteUnboundTerminalCertificatesAsync(
        ContainerAppDetails app,
        string environmentResourceGroup,
        string environmentName,
        CancellationToken cancellationToken)
    {
        var certificates = await ListManagedCertificatesAsync(
            environmentResourceGroup,
            environmentName,
            cancellationToken);
        var boundCertificateIds = (app.CustomDomains ?? [])
            .Select(binding => binding.CertificateId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var certificate in certificates.Where(certificate =>
            certificate.Properties.ProvisioningState is
                "Failed" or "Canceled" or "DeleteFailed"))
        {
            var domain = BlogCustomDomains.All.SingleOrDefault(
                candidate =>
                    candidate.CertificateName == certificate.Name)
                ?? throw new InvalidOperationException(
                    $"Unexpected terminal managed certificate '{certificate.Name}'.");
            if (boundCertificateIds.Contains(certificate.Id))
            {
                throw new InvalidOperationException(
                    $"Terminal managed certificate '{certificate.Name}' is still bound.");
            }

            if (!string.IsNullOrWhiteSpace(
                    certificate.Properties.ValidationToken))
            {
                await RemoveValidationTokenAsync(
                    domain,
                    certificate.Properties.ValidationToken,
                    cancellationToken);
            }

            await RunAsync(
                "az",
                cancellationToken,
                "containerapp", "env", "certificate", "delete",
                "--resource-group", environmentResourceGroup,
                "--name", environmentName,
                "--certificate", certificate.Name,
                "--yes",
                "--only-show-errors",
                "--output", "none");
        }
    }

    private async Task PublishValidationAndWaitForCertificatesAsync(
        string environmentResourceGroup,
        string environmentName,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + provisioningTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var certificates = await ListManagedCertificatesAsync(
                environmentResourceGroup,
                environmentName,
                cancellationToken);
            var certificatesByName = certificates.ToDictionary(
                certificate => certificate.Name,
                StringComparer.Ordinal);
            var validationTasks = BlogCustomDomains.All
                .Where(domain =>
                    certificatesByName.TryGetValue(
                        domain.CertificateName,
                        out var certificate) &&
                    !string.IsNullOrWhiteSpace(
                        certificate.Properties.ValidationToken))
                .Select(domain => EnsureValidationTokenAsync(
                    domain,
                    certificatesByName[domain.CertificateName]
                        .Properties
                        .ValidationToken!,
                    cancellationToken));
            await Task.WhenAll(validationTasks);

            var allSucceeded = true;
            foreach (var domain in BlogCustomDomains.All)
            {
                if (!certificatesByName.TryGetValue(
                        domain.CertificateName,
                        out var certificate))
                {
                    allSucceeded = false;
                    continue;
                }

                switch (certificate.Properties.ProvisioningState)
                {
                    case "Succeeded":
                        await RequireValidationRecordAsync(
                            domain,
                            cancellationToken);
                        break;
                    case "Failed":
                    case "Canceled":
                    case "DeleteFailed":
                        throw new InvalidOperationException(
                            $"Managed certificate '{domain.CertificateName}' entered state '{certificate.Properties.ProvisioningState}': {certificate.Properties.Error}");
                    default:
                        allSucceeded = false;
                        break;
                }
            }

            if (allSucceeded)
            {
                return;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        throw new InvalidOperationException(
            $"The managed certificates did not succeed within {provisioningTimeout.TotalMinutes} minutes.");
    }

    private async Task WaitForBindingsAndProbeAsync(
        string environmentId,
        CancellationToken cancellationToken)
    {
        var environment = await RunJsonAsync<ContainerAppEnvironment>(
            "az",
            cancellationToken,
            "containerapp", "env", "show",
            "--ids", environmentId,
            "--query", "{staticIp:properties.staticIp}",
            "--only-show-errors",
            "--output", "json");
        var deadline = DateTimeOffset.UtcNow + provisioningTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var app = await GetContainerAppAsync(cancellationToken);
            var bindings = (app?.CustomDomains ?? [])
                .ToDictionary(
                    binding => binding.Name,
                    StringComparer.OrdinalIgnoreCase);
            if (HasSecuredBindings(bindings, environmentId))
            {
                var probes = await Task.WhenAll(
                    BlogCustomDomains.All.Select(domain =>
                        ProbeAsync(
                            domain.Hostname,
                            environment.StaticIp,
                            cancellationToken)));
                if (probes.All(succeeded => succeeded))
                {
                    return;
                }
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        throw new InvalidOperationException(
            "The Container App custom domains did not become healthy.");
    }

    private async Task<bool> ProbeAsync(
        string hostname,
        string staticIp,
        CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            "curl",
            [
                "--silent",
                "--show-error",
                "--fail",
                "--head",
                "--connect-timeout", "30",
                "--max-time", "30",
                "--noproxy", "*",
                "--resolve", $"{hostname}:443:{staticIp}",
                $"https://{hostname}/",
            ],
            cancellationToken);

        return result.ExitCode == 0 &&
            ContainsOkStatus(result.StandardOutput);
    }

    private static bool HasSecuredBindings(
        IReadOnlyDictionary<string, CustomDomainBinding> bindings,
        string environmentId) =>
        BlogCustomDomains.All.All(domain =>
            bindings.TryGetValue(
                domain.Hostname,
                out var binding) &&
            string.Equals(
                binding.CertificateId,
                $"{environmentId}/managedCertificates/{domain.CertificateName}",
                StringComparison.OrdinalIgnoreCase) &&
            binding.BindingType is "Auto" or "SniEnabled");

    private async Task<ContainerAppDetails> WaitForContainerAppAsync(
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + provisioningTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var app = await GetContainerAppAsync(cancellationToken);
            if (app is not null)
            {
                return app;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Container App '{appName}' did not appear within {provisioningTimeout.TotalMinutes} minutes.");
    }

    private async Task<ContainerAppDetails?> GetContainerAppAsync(
        CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            "az",
            [
                "containerapp", "show",
                "--resource-group", azureResourceGroup,
                "--name", appName,
                "--query",
                "{environmentId:properties.environmentId,customDomains:properties.configuration.ingress.customDomains}",
                "--subscription", azureSubscriptionId,
                "--only-show-errors",
                "--output", "json",
            ],
            cancellationToken);
        if (result.ExitCode == 0)
        {
            return JsonSerializer.Deserialize<ContainerAppDetails>(
                    result.StandardOutput,
                    JsonOptions)
                ?? throw new InvalidOperationException(
                    "Azure returned no Container App JSON.");
        }

        if (IsNotFound(result.StandardError))
        {
            return null;
        }

        throw CommandFailed("az", result);
    }

    private static bool IsNotFound(string error) =>
        error.Contains(
            "ResourceNotFound",
            StringComparison.OrdinalIgnoreCase) ||
        error.Contains(
            "ResourceGroupNotFound",
            StringComparison.OrdinalIgnoreCase) ||
        error.Contains(
            "DeploymentNotFound",
            StringComparison.OrdinalIgnoreCase);

    private async Task EnsureValidationTokenAsync(
        BlogCustomDomain domain,
        string validationToken,
        CancellationToken cancellationToken)
    {
        var recordSet = await GetValidationRecordAsync(
            domain,
            cancellationToken);
        if (recordSet?.TxtRecords?
            .SelectMany(record => record.Value)
            .Contains(validationToken, StringComparer.Ordinal) is true)
        {
            return;
        }

        await RunAsync(
            "az",
            cancellationToken,
            "network", "dns", "record-set", "txt", "add-record",
            "--resource-group", dnsResourceGroup,
            "--zone-name", domain.Zone.Name,
            "--record-set-name", domain.ValidationRecordName,
            "--value", validationToken,
            "--only-show-errors",
            "--output", "none");
    }

    private async Task RemoveValidationTokenAsync(
        BlogCustomDomain domain,
        string validationToken,
        CancellationToken cancellationToken)
    {
        var recordSet = await GetValidationRecordAsync(
            domain,
            cancellationToken);
        if (recordSet?.TxtRecords?
            .SelectMany(record => record.Value)
            .Contains(validationToken, StringComparer.Ordinal) is not true)
        {
            return;
        }

        await RunAsync(
            "az",
            cancellationToken,
            "network", "dns", "record-set", "txt", "remove-record",
            "--resource-group", dnsResourceGroup,
            "--zone-name", domain.Zone.Name,
            "--record-set-name", domain.ValidationRecordName,
            "--value", validationToken,
            "--keep-empty-record-set",
            "--only-show-errors",
            "--output", "none");
    }

    private async Task RequireValidationRecordAsync(
        BlogCustomDomain domain,
        CancellationToken cancellationToken)
    {
        var recordSet = await GetValidationRecordAsync(
            domain,
            cancellationToken);
        if (recordSet?.TxtRecords is null ||
            !recordSet.TxtRecords
                .SelectMany(record => record.Value)
                .Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            throw new InvalidOperationException(
                $"DNS validation record for '{domain.Hostname}' is missing.");
        }
    }

    private async Task<DnsTxtRecordSet?> GetValidationRecordAsync(
        BlogCustomDomain domain,
        CancellationToken cancellationToken)
    {
        var recordSets = await RunJsonAsync<DnsTxtRecordSet[]>(
            "az",
            cancellationToken,
            "network", "dns", "record-set", "txt", "list",
            "--resource-group", dnsResourceGroup,
            "--zone-name", domain.Zone.Name,
            "--query", $"[?name=='{domain.ValidationRecordName}']",
            "--only-show-errors",
            "--output", "json");

        return recordSets.SingleOrDefault();
    }

    private Task<ManagedCertificate[]> ListManagedCertificatesAsync(
        string environmentResourceGroup,
        string environmentName,
        CancellationToken cancellationToken) =>
        RunJsonAsync<ManagedCertificate[]>(
            "az",
            cancellationToken,
            "containerapp", "env", "certificate", "list",
            "--resource-group", environmentResourceGroup,
            "--name", environmentName,
            "--managed-certificates-only",
            "--only-show-errors",
            "--output", "json");

    private async Task<T> RunJsonAsync<T>(
        string command,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var output = await RunAsync(
            command,
            cancellationToken,
            arguments);
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
            [.. arguments, "--subscription", azureSubscriptionId],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw CommandFailed(command, result);
        }

        return result.StandardOutput;
    }

    private static InvalidOperationException CommandFailed(
        string command,
        CommandOutput result)
    {
        var error = string.IsNullOrWhiteSpace(result.StandardError)
            ? "No error output was returned."
            : result.StandardError.Trim();
        return new InvalidOperationException(
            $"Command '{command}' failed: {error}");
    }

    private static bool ContainsOkStatus(string output) =>
        output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line =>
                line.StartsWith("HTTP/", StringComparison.Ordinal))
            .Select(line =>
                line.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries))
            .Any(parts =>
                parts.Length >= 2 &&
                parts[1] == "200");

    private sealed record ContainerAppDetails(
        string EnvironmentId,
        CustomDomainBinding[]? CustomDomains);

    private sealed record ContainerAppEnvironment(string StaticIp);

    private sealed record CustomDomainBinding(
        string Name,
        string BindingType,
        string? CertificateId);

    private sealed record ManagedCertificate(
        string Id,
        string Name,
        ManagedCertificateProperties Properties);

    private sealed record ManagedCertificateProperties(
        string ProvisioningState,
        string? ValidationToken,
        string? Error);

    private sealed record DnsTxtRecordSet(
        DnsTxtRecord[]? TxtRecords);

    private sealed record DnsTxtRecord(string[] Value);

}
