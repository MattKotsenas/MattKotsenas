using Microsoft.Extensions.Options;

namespace MattKotsenas.AppHost;

internal sealed class BootstrapAzureOptionsValidator : IValidateOptions<BootstrapAzureOptions>
{
    public ValidateOptionsResult Validate(string? name, BootstrapAzureOptions options)
    {
        List<string> failures = [];
        if (!Guid.TryParseExact(options.SubscriptionId, "D", out _))
        {
            failures.Add("Azure:SubscriptionId must be a GUID in D format.");
        }
        if (string.IsNullOrWhiteSpace(options.Location))
        {
            failures.Add("Azure:Location is required.");
        }
        if (!string.Equals(options.ResourceGroup, BootstrapPreflight.ResourceGroup, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Azure:ResourceGroup must be 'blog' (case-insensitive).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
