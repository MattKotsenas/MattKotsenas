namespace MattKotsenas.AppHost;

internal sealed class BootstrapAzureOptions
{
    public string? SubscriptionId { get; set; }
    public string? TenantId { get; set; }
    public string? Location { get; set; }
    public string? ResourceGroup { get; set; }
}
