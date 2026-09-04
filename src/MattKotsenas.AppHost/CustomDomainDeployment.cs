using Azure.Core;
using Aspire.Hosting.Azure;

namespace MattKotsenas.AppHost;

internal sealed class CustomDomainDeployment(
    IContainerAppControlPlane containerApps,
    IDnsValidationRecords dns,
    IHttpsEndpointProbe https,
    TimeProvider timeProvider,
    ResourceIdentifier appId,
    ResourceIdentifier selectedEnvironmentId,
    TimeSpan? pollInterval = null,
    TimeSpan? provisioningTimeout = null)
{
    private readonly TimeSpan pollInterval =
        pollInterval ?? TimeSpan.FromSeconds(10);
    private readonly TimeSpan provisioningTimeout =
        provisioningTimeout ?? TimeSpan.FromMinutes(20);

    internal async Task ValidateAsync(
        IReadOnlyList<CustomDomainResource> domains,
        IReadOnlyList<CustomDomainCertificateResource> certificates,
        CancellationToken cancellationToken)
    {
        var app = await containerApps.GetAppAsync(
            appId,
            cancellationToken);
        if (app is null)
        {
            return;
        }

        RequireSelectedEnvironment(app, selectedEnvironmentId);
        RejectUnmodeledCustomDomainBindings(app, domains);

        var modeledCertificates = certificates
            .Select(certificate =>
                certificate.GetManagedCertificateName())
            .ToHashSet(StringComparer.Ordinal);
        var terminalCertificates =
            (await containerApps.GetManagedCertificatesAsync(
                selectedEnvironmentId,
                cancellationToken))
            .Where(IsTerminal)
            .ToArray();
        var unexpectedTerminalCertificates = terminalCertificates
            .Where(certificate =>
                !modeledCertificates.Contains(certificate.Id.Name))
            .Select(certificate => certificate.Id.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unexpectedTerminalCertificates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Container App '{appId.Name}' has terminal managed certificates outside the custom domain resource graph: {string.Join(", ", unexpectedTerminalCertificates)}.");
        }

        var boundCertificateIds = app.CustomDomains
            .Select(binding => binding.CertificateId)
            .Where(id => id is not null)
            .ToHashSet();
        var boundTerminalCertificates = terminalCertificates
            .Where(certificate =>
                boundCertificateIds.Contains(certificate.Id))
            .Select(certificate => certificate.Id.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (boundTerminalCertificates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Container App '{appId.Name}' has bound terminal managed certificates: {string.Join(", ", boundTerminalCertificates)}.");
        }
    }

    internal async Task RecoverAsync(
        CustomDomainCertificateResource certificateResource,
        CancellationToken cancellationToken)
    {
        var app = await containerApps.GetAppAsync(
            appId,
            cancellationToken);
        if (app is null)
        {
            return;
        }

        RequireSelectedEnvironment(app, selectedEnvironmentId);
        var certificate =
            (await containerApps.GetManagedCertificatesAsync(
                selectedEnvironmentId,
                cancellationToken))
            .SingleOrDefault(certificate =>
                certificate.Id.Name ==
                certificateResource.GetManagedCertificateName());
        if (certificate is null || !IsTerminal(certificate))
        {
            return;
        }

        if (app.CustomDomains.Any(binding =>
            binding.CertificateId is not null &&
            binding.CertificateId.Equals(certificate.Id)))
        {
            throw new InvalidOperationException(
                $"Terminal managed certificate '{certificate.Id.Name}' is still bound.");
        }

        if (!string.IsNullOrWhiteSpace(certificate.ValidationToken))
        {
            await dns.RemoveValueAsync(
                GetValidationRecordKey(certificateResource),
                certificate.ValidationToken,
                keepEmptyRecordSet: true,
                cancellationToken);
        }

        await containerApps.DeleteManagedCertificateAsync(
            certificate.Id,
            cancellationToken);
    }

    internal async Task PublishValidationAndWaitForCertificateAsync(
        CustomDomainCertificateResource certificateResource,
        CancellationToken cancellationToken)
    {
        var app = await WaitForContainerAppAsync(cancellationToken);
        RequireSelectedEnvironment(app, selectedEnvironmentId);
        var deadline =
            timeProvider.GetUtcNow() + provisioningTimeout;
        var record = GetValidationRecordKey(certificateResource);

        while (timeProvider.GetUtcNow() < deadline)
        {
            var certificate =
                (await containerApps.GetManagedCertificatesAsync(
                    selectedEnvironmentId,
                    cancellationToken))
                .SingleOrDefault(certificate =>
                    certificate.Id.Name ==
                    certificateResource.GetManagedCertificateName());
            if (certificate is null)
            {
                await DelayAsync(cancellationToken);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(
                    certificate.ValidationToken))
            {
                await dns.EnsureValueAsync(
                    record,
                    certificate.ValidationToken,
                    TimeSpan.FromHours(1),
                    cancellationToken);
            }

            switch (certificate.State)
            {
                case ManagedCertificateState.Succeeded:
                    if (!await dns.HasAnyValueAsync(
                            record,
                            cancellationToken))
                    {
                        throw new InvalidOperationException(
                            $"DNS validation record for '{certificateResource.Parent.Hostname}' is missing.");
                    }

                    return;
                case ManagedCertificateState.Failed:
                case ManagedCertificateState.Canceled:
                case ManagedCertificateState.DeleteFailed:
                    throw new InvalidOperationException(
                        $"Managed certificate '{certificate.Id.Name}' entered state '{certificate.State}': {certificate.Error}");
            }

            await DelayAsync(cancellationToken);
        }

        throw new InvalidOperationException(
            $"Managed certificate '{certificateResource.GetManagedCertificateName()}' did not succeed within {provisioningTimeout.TotalMinutes} minutes.");
    }

    internal async Task VerifyCurrentDeploymentAsync(
        CustomDomainResource domain,
        CustomDomainCertificateResource certificate,
        CancellationToken cancellationToken)
    {
        var app = await WaitForContainerAppAsync(cancellationToken);
        RequireSelectedEnvironment(app, selectedEnvironmentId);
        var environment = await containerApps.GetEnvironmentAsync(
            selectedEnvironmentId,
            cancellationToken);
        var provider = certificate.Annotations
            .OfType<CustomDomainCertificateProviderAnnotation>()
            .Single();
        var deadline =
            timeProvider.GetUtcNow() + provisioningTimeout;

        while (timeProvider.GetUtcNow() < deadline)
        {
            app = await containerApps.GetAppAsync(
                appId,
                cancellationToken);
            var binding = app?.CustomDomains
                .SingleOrDefault(binding =>
                    string.Equals(
                        binding.Hostname,
                        domain.Hostname,
                        StringComparison.OrdinalIgnoreCase));
            if (binding is not null &&
                provider.IsReadyBinding(
                    binding,
                    selectedEnvironmentId) &&
                await https.IsHealthyAsync(
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

    private void RejectUnmodeledCustomDomainBindings(
        ContainerAppSnapshot app,
        IReadOnlyList<CustomDomainResource> domains)
    {
        var resourcesByHostname = domains.ToDictionary(
            domain => domain.Hostname,
            StringComparer.OrdinalIgnoreCase);
        var unmodeledBindings = app.CustomDomains
            .Where(binding =>
            {
                if (!resourcesByHostname.TryGetValue(
                        binding.Hostname,
                        out var domain))
                {
                    return true;
                }

                if (!domain.TryGetLastAnnotation<
                        CustomDomainCertificateSelectionAnnotation>(
                        out var selection))
                {
                    return binding.BindingKind is not
                            CustomDomainBindingKind.Disabled ||
                        binding.CertificateId is not null;
                }

                var provider = selection.Certificate.Annotations
                    .OfType<
                        CustomDomainCertificateProviderAnnotation>()
                    .Single();
                return !provider.IsAllowedBinding(
                    binding,
                    app.EnvironmentId);
            })
            .Select(binding =>
                $"{binding.Hostname} ({binding.BindingKind}, {binding.CertificateId?.ToString() ?? "no certificate"})")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unmodeledBindings.Length > 0)
        {
            throw new InvalidOperationException(
                $"Container App '{appId.Name}' has custom domain bindings outside the resource graph: {string.Join(", ", unmodeledBindings)}.");
        }
    }

    private static void RequireSelectedEnvironment(
        ContainerAppSnapshot app,
        ResourceIdentifier selectedEnvironmentId)
    {
        if (!app.EnvironmentId.Equals(selectedEnvironmentId))
        {
            throw new InvalidOperationException(
                $"Container App is deployed to environment '{app.EnvironmentId}', but the model selects '{selectedEnvironmentId}'.");
        }
    }

    private static bool IsTerminal(
        ManagedCertificateSnapshot certificate) =>
        certificate.State is
            ManagedCertificateState.Failed or
            ManagedCertificateState.Canceled or
            ManagedCertificateState.DeleteFailed;

    private async Task<ContainerAppSnapshot>
        WaitForContainerAppAsync(
            CancellationToken cancellationToken)
    {
        var deadline =
            timeProvider.GetUtcNow() + provisioningTimeout;
        while (timeProvider.GetUtcNow() < deadline)
        {
            var app = await containerApps.GetAppAsync(
                appId,
                cancellationToken);
            if (app is not null)
            {
                return app;
            }

            await DelayAsync(cancellationToken);
        }

        throw new InvalidOperationException(
            $"Container App '{appId.Name}' did not appear within {provisioningTimeout.TotalMinutes} minutes.");
    }

    private static DnsTxtRecordKey GetValidationRecordKey(
        CustomDomainCertificateResource certificate)
    {
        var validation = certificate
            .GetManagedCertificate()
            .ValidationRecord;
        var existing = validation.Parent.Annotations
            .OfType<ExistingAzureResourceAnnotation>()
            .Single();
        return new(
            SubscriptionId:
                existing.Subscription as string
                ?? throw new InvalidOperationException(
                    $"Azure DNS zone '{validation.Parent.Name}' must use a literal subscription."),
            ResourceGroup:
                existing.ResourceGroup as string
                ?? throw new InvalidOperationException(
                    $"Azure DNS zone '{validation.Parent.Name}' must use a literal resource group."),
            Zone: validation.Parent.ZoneName,
            RelativeName: validation.RelativeName.Value);
    }

    private Task DelayAsync(CancellationToken cancellationToken) =>
        Task.Delay(
            pollInterval,
            timeProvider,
            cancellationToken);
}
