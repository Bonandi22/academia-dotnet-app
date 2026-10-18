namespace Academia.Infrastructure.Security;

public sealed class TokenSettings
{
    public string Issuer { get; set; } = "AcademiaDotNet";
    public string Audience { get; set; } = "AcademiaDotNet.Client";
    public string Secret { get; set; } = string.Empty;
}
