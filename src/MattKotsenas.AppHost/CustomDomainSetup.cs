using System.Text.Json;

using Aspire.Hosting.ApplicationModel;
using Azure.Core;

namespace MattKotsenas.AppHost;

internal sealed class CustomDomainSetup(ICommandRunner runner)
{
    private const string ContainerAppName = "blog";
    private const string ContainerAppResourceGroup = "blog";
    private const string DnsResourceGroup = "dns";
    private static readonly TimeSpan CertificatePollInterval =
        TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CertificateTimeout =
        TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    internal static IReadOnlyList<CustomDomainDefinition> Domains { get; } =
        LoadDomains();

    public static async Task<ExecuteCommandResult> ExecuteAsync(
        ExecuteCommandContext context)
    {
        try
        {
            await new CustomDomainSetup(new CliWrapCommandRunner())
                .PrepareAsync(context.CancellationToken);

            return CommandResults.Success(
                "Prepared and verified all production custom domains.");
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

    internal async Task PrepareAsync(CancellationToken cancellationToken)
    {
        var app = await RunJsonAsync<ContainerAppDetails>(
            "az",
            cancellationToken,
            "containerapp", "show",
            "--resource-group", ContainerAppResourceGroup,
            "--name", ContainerAppName,
            "--query",
            "{environmentId:properties.environmentId,customDomains:properties.configuration.ingress.customDomains}",
            "--only-show-errors",
            "--output", "json");
        var environmentId = new ResourceIdentifier(app.EnvironmentId);
        var environmentResourceGroup = environmentId.ResourceGroupName
            ?? throw new InvalidOperationException(
                $"Container Apps environment '{app.EnvironmentId}' has no resource group.");
        var environment = await RunJsonAsync<ContainerAppEnvironment>(
            "az",
            cancellationToken,
            "containerapp", "env", "show",
            "--ids", app.EnvironmentId,
            "--query",
            "{staticIp:properties.staticIp}",
            "--only-show-errors",
            "--output", "json");
        var bindings = (app.CustomDomains ?? [])
            .ToDictionary(binding => binding.Name, StringComparer.OrdinalIgnoreCase);
        var unexpectedBindings = bindings.Keys
            .Except(
                Domains.Select(domain => domain.Hostname),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unexpectedBindings.Length != 0)
        {
            throw new InvalidOperationException(
                "The Container App contains unexpected custom domains: " +
                string.Join(", ", unexpectedBindings));
        }

        foreach (var domain in Domains)
        {
            if (!bindings.TryGetValue(domain.Hostname, out var binding))
            {
                continue;
            }

            var expectedCertificateId =
                $"{app.EnvironmentId}/managedCertificates/{domain.CertificateName}";
            var isDisabled =
                string.Equals(
                    binding.BindingType,
                    "Disabled",
                    StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(binding.CertificateId);
            var isEnabled =
                string.Equals(
                    binding.BindingType,
                    "SniEnabled",
                    StringComparison.Ordinal) &&
                string.Equals(
                    binding.CertificateId,
                    expectedCertificateId,
                    StringComparison.OrdinalIgnoreCase);
            if (!isDisabled && !isEnabled)
            {
                throw new InvalidOperationException(
                    $"Hostname '{domain.Hostname}' has unexpected binding '{binding.CertificateId ?? binding.BindingType}'.");
            }
        }

        var certificates = (await ListManagedCertificatesAsync(
                environmentResourceGroup,
                environmentId.Name,
                cancellationToken))
            .ToList();

        foreach (var domain in Domains)
        {
            await VerifyOwnershipAsync(
                domain,
                environmentId.SubscriptionId
                    ?? throw new InvalidOperationException(
                        $"Container Apps environment '{app.EnvironmentId}' has no subscription."),
                cancellationToken);
            await EnsureHostnameAsync(
                domain,
                bindings,
                cancellationToken);
            var certificate = await EnsureCertificateAsync(
                domain,
                environmentResourceGroup,
                environmentId.Name,
                certificates,
                bindings,
                cancellationToken);
            await EnsureBindingAsync(
                domain,
                certificate,
                bindings,
                environmentId.Name,
                cancellationToken);
            await ProbeAsync(
                domain.Hostname,
                environment.StaticIp,
                cancellationToken);
        }
    }

    private async Task VerifyOwnershipAsync(
        CustomDomainDefinition domain,
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        var resourcePath =
            $"/subscriptions/{subscriptionId}/resourceGroups/{ContainerAppResourceGroup}/providers/Microsoft.App/containerApps/{ContainerAppName}";
        var analysisUri =
            $"https://management.azure.com{resourcePath}/listCustomHostNameAnalysis";
        var analysis = await RunJsonAsync<DomainAnalysis>(
            "az",
            cancellationToken,
            "rest",
            "--method", "post",
            "--uri", analysisUri,
            "--url-parameters",
            "api-version=2025-07-01",
            $"customHostname={domain.Hostname}",
            "--only-show-errors",
            "--output", "json");
        if (!string.Equals(
                analysis.CustomDomainVerificationTest,
                "Passed",
                StringComparison.Ordinal) ||
            analysis.HasConflictOnManagedEnvironment)
        {
            throw new InvalidOperationException(
                $"Container Apps did not verify ownership of '{domain.Hostname}': " +
                $"{analysis.CustomDomainVerificationFailureInfo?.Message ?? "No failure detail was returned."}");
        }
    }

    private async Task EnsureHostnameAsync(
        CustomDomainDefinition domain,
        IDictionary<string, CustomDomainBinding> bindings,
        CancellationToken cancellationToken)
    {
        if (bindings.ContainsKey(domain.Hostname))
        {
            return;
        }

        await RunAsync(
            "az",
            cancellationToken,
            "containerapp", "hostname", "add",
            "--resource-group", ContainerAppResourceGroup,
            "--name", ContainerAppName,
            "--hostname", domain.Hostname,
            "--only-show-errors",
            "--output", "none");
        bindings.Add(
            domain.Hostname,
            new CustomDomainBinding(domain.Hostname, "Disabled", null));
    }

    private async Task<ManagedCertificate> EnsureCertificateAsync(
        CustomDomainDefinition domain,
        string environmentResourceGroup,
        string environmentName,
        ICollection<ManagedCertificate> certificates,
        IReadOnlyDictionary<string, CustomDomainBinding> bindings,
        CancellationToken cancellationToken)
    {
        var matchingName = certificates
            .Where(certificate =>
                string.Equals(
                    certificate.Name,
                    domain.CertificateName,
                    StringComparison.Ordinal))
            .ToArray();
        if (matchingName.Length > 1)
        {
            throw new InvalidOperationException(
                $"More than one managed certificate is named '{domain.CertificateName}'.");
        }

        var matchingSubject = certificates
            .Where(certificate =>
                CertificateSubjectMatches(
                    certificate.Properties.SubjectName,
                    domain.Hostname))
            .ToArray();
        if (matchingSubject.Length > 1)
        {
            throw new InvalidOperationException(
                $"More than one managed certificate uses hostname '{domain.Hostname}'.");
        }

        var certificate = matchingName.SingleOrDefault()
            ?? matchingSubject.SingleOrDefault();
        if (certificate is not null &&
            !CertificateSubjectMatches(
                certificate.Properties.SubjectName,
                domain.Hostname))
        {
            throw new InvalidOperationException(
                $"Managed certificate '{certificate.Name}' uses unexpected hostname '{certificate.Properties.SubjectName}'.");
        }

        if (certificate is not null &&
            !string.Equals(
                certificate.Name,
                domain.CertificateName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Hostname '{domain.Hostname}' uses unexpected managed certificate '{certificate.Name}'.");
        }

        if (certificate is not null)
        {
            VerifyTxtValidation(certificate);
        }

        if (certificate?.Properties.ProvisioningState is
            "Failed" or "Canceled" or "DeleteFailed")
        {
            if (bindings.Values.Any(binding =>
                    string.Equals(
                        binding.CertificateId,
                        certificate.Id,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Failed managed certificate '{certificate.Name}' is still bound to a custom domain.");
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
            certificates.Remove(certificate);
            certificate = null;
        }

        if (certificate is null)
        {
            certificate = await RunJsonAsync<ManagedCertificate>(
                "az",
                cancellationToken,
                "containerapp", "env", "certificate", "create",
                "--resource-group", environmentResourceGroup,
                "--name", environmentName,
                "--certificate-name", domain.CertificateName,
                "--hostname", domain.Hostname,
                "--validation-method", "TXT",
                "--only-show-errors",
                "--output", "json");
            certificates.Add(certificate);
        }

        VerifyTxtValidation(certificate);

        if (!string.IsNullOrWhiteSpace(certificate.Properties.ValidationToken))
        {
            await EnsureValidationTokenAsync(
                domain,
                certificate.Properties.ValidationToken,
                cancellationToken);
        }
        else if (!await HasValidationTokenAsync(domain, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Managed certificate '{certificate.Name}' does not expose its validation token, " +
                $"and the DNS validation record for '{domain.Hostname}' does not contain one.");
        }

        return await WaitForCertificateAsync(
            domain.CertificateName,
            environmentResourceGroup,
            environmentName,
            cancellationToken);
    }

    private async Task EnsureValidationTokenAsync(
        CustomDomainDefinition domain,
        string validationToken,
        CancellationToken cancellationToken)
    {
        var recordSet = await GetValidationRecordAsync(
            domain,
            cancellationToken);
        if (recordSet is null)
        {
            await RunAsync(
                "az",
                cancellationToken,
                "network", "dns", "record-set", "txt", "create",
                "--resource-group", DnsResourceGroup,
                "--zone-name", domain.ZoneName,
                "--name", domain.ValidationRecordName,
                "--ttl", "3600",
                "--only-show-errors",
                "--output", "none");
        }
        else if (recordSet.TxtRecords
            .SelectMany(record => record.Value)
            .Contains(validationToken, StringComparer.Ordinal))
        {
            return;
        }

        await RunAsync(
            "az",
            cancellationToken,
            "network", "dns", "record-set", "txt", "add-record",
            "--resource-group", DnsResourceGroup,
            "--zone-name", domain.ZoneName,
            "--record-set-name", domain.ValidationRecordName,
            "--value", validationToken,
            "--only-show-errors",
            "--output", "none");
    }

    private async Task RemoveValidationTokenAsync(
        CustomDomainDefinition domain,
        string validationToken,
        CancellationToken cancellationToken)
    {
        var recordSet = await GetValidationRecordAsync(
            domain,
            cancellationToken);
        if (recordSet is null ||
            !recordSet.TxtRecords
                .SelectMany(record => record.Value)
                .Contains(validationToken, StringComparer.Ordinal))
        {
            return;
        }

        await RunAsync(
            "az",
            cancellationToken,
            "network", "dns", "record-set", "txt", "remove-record",
            "--resource-group", DnsResourceGroup,
            "--zone-name", domain.ZoneName,
            "--record-set-name", domain.ValidationRecordName,
            "--value", validationToken,
            "--keep-empty-record-set",
            "--only-show-errors",
            "--output", "none");
    }

    private async Task<bool> HasValidationTokenAsync(
        CustomDomainDefinition domain,
        CancellationToken cancellationToken)
    {
        var recordSet = await GetValidationRecordAsync(
            domain,
            cancellationToken);

        return recordSet is not null &&
            recordSet.TxtRecords
                .SelectMany(record => record.Value)
                .Any(value => !string.IsNullOrWhiteSpace(value));
    }

    private async Task<DnsTxtRecordSet?> GetValidationRecordAsync(
        CustomDomainDefinition domain,
        CancellationToken cancellationToken)
    {
        var recordSets = await RunJsonAsync<DnsTxtRecordSet[]>(
            "az",
            cancellationToken,
            "network", "dns", "record-set", "txt", "list",
            "--resource-group", DnsResourceGroup,
            "--zone-name", domain.ZoneName,
            "--query", $"[?name=='{domain.ValidationRecordName}']",
            "--only-show-errors",
            "--output", "json");

        return recordSets.SingleOrDefault();
    }

    private static bool CertificateSubjectMatches(
        string subjectName,
        string hostname) =>
        string.Equals(
            subjectName,
            hostname,
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            subjectName,
            $"CN={hostname}",
            StringComparison.OrdinalIgnoreCase);

    private static void VerifyTxtValidation(ManagedCertificate certificate)
    {
        if (!string.Equals(
                certificate.Properties.ValidationMethod
                    ?? certificate.Properties.DomainControlValidation,
                "TXT",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Managed certificate '{certificate.Name}' does not use TXT validation.");
        }
    }

    private static CustomDomainDefinition[] LoadDomains()
    {
        using var stream = typeof(CustomDomainSetup)
            .Assembly
            .GetManifestResourceStream("custom-domains.json")
            ?? throw new InvalidOperationException(
                "Embedded custom-domain configuration was not found.");

        return JsonSerializer.Deserialize<CustomDomainDefinition[]>(
                stream,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "Embedded custom-domain configuration was empty.");
    }

    private async Task<ManagedCertificate> WaitForCertificateAsync(
        string certificateName,
        string environmentResourceGroup,
        string environmentName,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + CertificateTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var certificates = await RunJsonAsync<ManagedCertificate[]>(
                "az",
                cancellationToken,
                "containerapp", "env", "certificate", "list",
                "--resource-group", environmentResourceGroup,
                "--name", environmentName,
                "--certificate", certificateName,
                "--managed-certificates-only",
                "--only-show-errors",
                "--output", "json");
            var certificate = certificates.SingleOrDefault()
                ?? throw new InvalidOperationException(
                    $"Managed certificate '{certificateName}' disappeared while provisioning.");

            switch (certificate.Properties.ProvisioningState)
            {
                case "Succeeded":
                    return certificate;
                case "Failed":
                case "Canceled":
                case "DeleteFailed":
                    throw new InvalidOperationException(
                        $"Managed certificate '{certificateName}' entered state '{certificate.Properties.ProvisioningState}': {certificate.Properties.Error}");
            }

            await Task.Delay(
                CertificatePollInterval,
                cancellationToken);
        }

        throw new InvalidOperationException(
            $"Managed certificate '{certificateName}' did not succeed within {CertificateTimeout.TotalMinutes} minutes.");
    }

    private async Task EnsureBindingAsync(
        CustomDomainDefinition domain,
        ManagedCertificate certificate,
        IDictionary<string, CustomDomainBinding> bindings,
        string environmentName,
        CancellationToken cancellationToken)
    {
        var binding = bindings[domain.Hostname];
        if (string.Equals(
                binding.BindingType,
                "SniEnabled",
                StringComparison.Ordinal) &&
            string.Equals(
                binding.CertificateId,
                certificate.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(binding.CertificateId) &&
            !string.Equals(
                binding.CertificateId,
                certificate.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Hostname '{domain.Hostname}' is bound to unexpected certificate '{binding.CertificateId}'.");
        }

        await RunAsync(
            "az",
            cancellationToken,
            "containerapp", "hostname", "bind",
            "--resource-group", ContainerAppResourceGroup,
            "--name", ContainerAppName,
            "--hostname", domain.Hostname,
            "--environment", environmentName,
            "--certificate", certificate.Id,
            "--only-show-errors",
            "--output", "none");
        bindings[domain.Hostname] =
            binding with
            {
                BindingType = "SniEnabled",
                CertificateId = certificate.Id,
            };
    }

    private async Task ProbeAsync(
        string hostname,
        string staticIp,
        CancellationToken cancellationToken)
    {
        var output = await RunAsync(
            "curl",
            cancellationToken,
            "--silent",
            "--show-error",
            "--fail",
            "--head",
            "--connect-timeout", "30",
            "--max-time", "30",
            "--noproxy", "*",
            "--resolve", $"{hostname}:443:{staticIp}",
            $"https://{hostname}/");
        var statusCode = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("HTTP/", StringComparison.Ordinal))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2)
            .Select(parts => parts[1])
            .LastOrDefault();
        if (!string.Equals(statusCode, "200", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Direct HTTPS probe for '{hostname}' returned status '{statusCode ?? "unknown"}'.");
        }
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

    internal sealed record CustomDomainDefinition(
        string Hostname,
        string ZoneName,
        string ZoneIdentifier,
        bool RetainLegacyApexVerificationId,
        string ValidationRecordName,
        string DomainParameterName,
        string CertificateParameterName,
        string CertificateName);

    private sealed record ContainerAppDetails(
        string EnvironmentId,
        CustomDomainBinding[]? CustomDomains);

    private sealed record ContainerAppEnvironment(string StaticIp);

    private sealed record CustomDomainBinding(
        string Name,
        string BindingType,
        string? CertificateId);

    private sealed record DomainAnalysis(
        string CustomDomainVerificationTest,
        bool HasConflictOnManagedEnvironment,
        DomainVerificationFailureInfo? CustomDomainVerificationFailureInfo);

    private sealed record DomainVerificationFailureInfo(string Message);

    private sealed record ManagedCertificate(
        string Id,
        string Name,
        ManagedCertificateProperties Properties);

    private sealed record ManagedCertificateProperties(
        string ProvisioningState,
        string SubjectName,
        string? DomainControlValidation,
        string? ValidationMethod,
        string? ValidationToken,
        string? Error);

    private sealed record DnsTxtRecordSet(
        string Name,
        DnsTxtRecord[] TxtRecords);

    private sealed record DnsTxtRecord(string[] Value);
}
