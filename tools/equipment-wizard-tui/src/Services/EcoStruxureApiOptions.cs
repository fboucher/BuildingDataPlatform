namespace EquipmentWizardTui.Services;

public sealed class EcoStruxureApiOptions
{
    public string BaseUrl { get; set; } = "https://ecostruxure-building-platform-api-uat.se.app";
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
}
