using System.Text.Json;

using Azure.Core;

namespace MattKotsenas.AppHost;

internal sealed class BlogCustomDomainDeployment(
    ICommandRunner runner,
    TimeProvider timeProvider,
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

    internal async Task ValidateAsync(
        IReadOnlyList<BlogCustomDomainResource> domains,
        CancellationToken cancellationToken)
    {
        var app = await GetContainerAppAsync(cancellationToken);
        if (app is null)
        {
            return;
        }

        RejectUnmodeledCustomDomainBindings(app, domains);

        var environment = GetEnvironment(app);
        var modeledCertificates = domains
            .Select(domain => domain.CertificateName)
            .ToHashSet(StringComparer.Ordinal);
        var terminalCertificates =
            (await ListManagedCertificatesAsync(
                environment.ResourceGroup,
                environment.Name,
                cancellationToken))
            .Where(IsTerminal)
            .ToArray();
        var unexpectedTerminalCertificates = terminalCertificates
            .Where(certificate =>
                !modeledCertificates.Contains(certificate.Name))
            .Select(certificate => certificate.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unexpectedTerminalCertificates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Container App '{appName}' has terminal managed certificates outside the custom-domain resource graph: {string.Join(", ", unexpectedTerminalCertificates)}.");
        }

        var boundCertificateIds = (app.CustomDomains ?? [])
            .Select(binding => binding.CertificateId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var boundTerminalCertificates = terminalCertificates
            .Where(certificate =>
                boundCertificateIds.Contains(certificate.Id))
            .Select(certificate => certificate.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (boundTerminalCertificates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Container App '{appName}' has bound terminal managed certificates: {string.Join(", ", boundTerminalCertificates)}.");
        }
    }

    internal async Task RecoverAsync(
        BlogCustomDomainResource domain,
        CancellationToken cancellationToken)
    {
        var app = await GetContainerAppAsync(cancellationToken);
        if (app is null)
        {
            return;
        }

        var environment = GetEnvironment(app);
        var certificate = (await ListManagedCertificatesAsync(
                environment.ResourceGroup,
                environment.Name,
                cancellationToken))
            .SingleOrDefault(certificate =>
                certificate.Name == domain.CertificateName);
        if (certificate is null || !IsTerminal(certificate))
        {
            return;
        }

        if ((app.CustomDomains ?? []).Any(binding =>
            string.Equals(
                binding.CertificateId,
                certificate.Id,
                StringComparison.OrdinalIgnoreCase)))
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
            "--resource-group", environment.ResourceGroup,
            "--name", environment.Name,
            "--certificate", certificate.Name,
            "--yes",
            "--only-show-errors",
            "--output", "none");
    }

    internal async Task PublishValidationAndWaitForCertificateAsync(
        BlogCustomDomainResource domain,
        CancellationToken cancellationToken)
    {
        var app = await WaitForContainerAppAsync(cancellationToken);
        var environment = GetEnvironment(app);
        var deadline =
            timeProvider.GetUtcNow() + provisioningTimeout;

        while (timeProvider.GetUtcNow() < deadline)
        {
            var certificate = (await ListManagedCertificatesAsync(
                    environment.ResourceGroup,
                    environment.Name,
                    cancellationToken))
                .SingleOrDefault(certificate =>
                    certificate.Name == domain.CertificateName);
            if (certificate is null)
            {
                await DelayAsync(cancellationToken);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(
                    certificate.Properties.ValidationToken))
            {
                await EnsureValidationTokenAsync(
                    domain,
                    certificate.Properties.ValidationToken,
                    cancellationToken);
            }

            switch (certificate.Properties.ProvisioningState)
            {
                case "Succeeded":
                    await RequireValidationRecordAsync(
                        domain,
                        cancellationToken);
                    return;
                case "Failed":
                case "Canceled":
                case "DeleteFailed":
                    throw new InvalidOperationException(
                        $"Managed certificate '{domain.CertificateName}' entered state '{certificate.Properties.ProvisioningState}': {certificate.Properties.Error}");
            }

            await DelayAsync(cancellationToken);
        }

        throw new InvalidOperationException(
            $"Managed certificate '{domain.CertificateName}' did not succeed within {provisioningTimeout.TotalMinutes} minutes.");
    }

    internal async Task VerifyCurrentDeploymentAsync(
        BlogCustomDomainResource domain,
        CancellationToken cancellationToken)
    {
        var app = await WaitForContainerAppAsync(cancellationToken);
        var environment = await RunJsonAsync<ContainerAppEnvironment>(
            "az",
            cancellationToken,
            "containerapp", "env", "show",
            "--ids", app.EnvironmentId,
            "--query", "{staticIp:properties.staticIp}",
            "--only-show-errors",
            "--output", "json");
        var deadline =
            timeProvider.GetUtcNow() + provisioningTimeout;

        while (timeProvider.GetUtcNow() < deadline)
        {
            app = await GetContainerAppAsync(cancellationToken);
            var binding = (app?.CustomDomains ?? [])
                .SingleOrDefault(binding =>
                    string.Equals(
                        binding.Name,
                        domain.Hostname,
                        StringComparison.OrdinalIgnoreCase));
            if (binding is not null &&
                IsSecuredBinding(binding, domain, app!.EnvironmentId) &&
                await ProbeAsync(
                    domain.Hostname,
                    environment.StaticIp,
                    cancellationToken))
            {
                return;
            }

            await DelayAsync(cancellationToken);
        }

        throw new InvalidOperationException(
            $"Custom domain '{domain.Hostname}' did not become healthy within {provisioningTimeout.TotalMinutes} minutes.");
    }

    private static bool IsTerminal(ManagedCertificate certificate) =>
        certificate.Properties.ProvisioningState is
            "Failed" or "Canceled" or "DeleteFailed";

    private static ContainerAppEnvironmentId GetEnvironment(
        ContainerAppDetails app)
    {
        var environmentId = new ResourceIdentifier(app.EnvironmentId);
        var resourceGroup = environmentId.ResourceGroupName
            ?? throw new InvalidOperationException(
                $"Container Apps environment '{app.EnvironmentId}' has no resource group.");
        return new(
            ResourceGroup: resourceGroup,
            Name: environmentId.Name);
    }

    private void RejectUnmodeledCustomDomainBindings(
        ContainerAppDetails app,
        IReadOnlyList<BlogCustomDomainResource> domains)
    {
        var resourcesByHostname = domains.ToDictionary(
            domain => domain.Hostname,
            StringComparer.OrdinalIgnoreCase);
        var unmodeledBindings = (app.CustomDomains ?? [])
            .Where(binding =>
            {
                if (!resourcesByHostname.TryGetValue(
                        binding.Name,
                        out var domain))
                {
                    return true;
                }

                return !IsSupportedBinding(
                    binding,
                    domain,
                    app.EnvironmentId);
            })
            .Select(binding =>
                $"{binding.Name} ({binding.BindingType}, {binding.CertificateId ?? "no certificate"})")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unmodeledBindings.Length > 0)
        {
            throw new InvalidOperationException(
                $"Container App '{appName}' has custom domain bindings outside the resource graph: {string.Join(", ", unmodeledBindings)}.");
        }
    }

    private static bool IsSupportedBinding(
        CustomDomainBinding binding,
        BlogCustomDomainResource domain,
        string environmentId)
    {
        var expectedCertificateId =
            $"{environmentId}/managedCertificates/{domain.CertificateName}";
        return binding.BindingType switch
        {
            "Auto" =>
                string.IsNullOrWhiteSpace(binding.CertificateId) ||
                string.Equals(
                    binding.CertificateId,
                    expectedCertificateId,
                    StringComparison.OrdinalIgnoreCase),
            "SniEnabled" =>
                string.Equals(
                    binding.CertificateId,
                    expectedCertificateId,
                    StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static bool IsSecuredBinding(
        CustomDomainBinding binding,
        BlogCustomDomainResource domain,
        string environmentId) =>
        !string.IsNullOrWhiteSpace(binding.CertificateId) &&
        IsSupportedBinding(binding, domain, environmentId);

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

    private async Task<ContainerAppDetails> WaitForContainerAppAsync(
        CancellationToken cancellationToken)
    {
        var deadline =
            timeProvider.GetUtcNow() + provisioningTimeout;
        while (timeProvider.GetUtcNow() < deadline)
        {
            var app = await GetContainerAppAsync(cancellationToken);
            if (app is not null)
            {
                return app;
            }

            await DelayAsync(cancellationToken);
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
        BlogCustomDomainResource domain,
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
        BlogCustomDomainResource domain,
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
        BlogCustomDomainResource domain,
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
        BlogCustomDomainResource domain,
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

    private Task DelayAsync(CancellationToken cancellationToken) =>
        Task.Delay(
            pollInterval,
            timeProvider,
            cancellationToken);

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

    private sealed record ContainerAppEnvironmentId(
        string ResourceGroup,
        string Name);

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
