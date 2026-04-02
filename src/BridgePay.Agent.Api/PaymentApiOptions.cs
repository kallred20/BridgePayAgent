namespace BridgePay.Agent.Api;

public sealed class PaymentApiOptions
{
    public const string SectionName = "PaymentApi";

    public string BaseUrl { get; set; } = string.Empty;
}
