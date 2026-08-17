using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using EquipmentWizardTui.Models;

namespace EquipmentWizardTui.Services;

public sealed class EquipmentWizardApiClient
{
    private static readonly string DataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;

    public EquipmentWizardApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<WizardSite>> GetSitesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var sites = await _httpClient.GetFromJsonAsync<List<WizardSite>>("sites", cancellationToken);
            return sites ?? [];
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load sites: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<WizardDeviceGroup>> GetDeviceGroupsBySiteAsync(string siteId, CancellationToken cancellationToken = default)
    {
        try
        {
            var groups = await _httpClient.GetFromJsonAsync<List<WizardDeviceGroup>>(
                $"sites/{Uri.EscapeDataString(siteId)}/devicegroups", cancellationToken);
            return groups ?? [];
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load device groups: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<WizardBuilding>> GetBuildingsBySiteAsync(string siteId, CancellationToken cancellationToken = default)
    {
        try
        {
            var buildings = await _httpClient.GetFromJsonAsync<List<WizardBuilding>>(
                $"sites/{Uri.EscapeDataString(siteId)}/buildings", cancellationToken);
            return buildings ?? [];
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load buildings: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<WizardFloor>> GetFloorsByBuildingAsync(string buildingId, CancellationToken cancellationToken = default)
    {
        try
        {
            var floors = await _httpClient.GetFromJsonAsync<List<WizardFloor>>(
                $"buildings/{Uri.EscapeDataString(buildingId)}/floors", cancellationToken);
            return floors ?? [];
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load floors: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<WizardSpace>> GetSpacesByFloorAsync(string floorId, CancellationToken cancellationToken = default)
    {
        try
        {
            var spaces = await _httpClient.GetFromJsonAsync<List<WizardSpace>>(
                $"floors/{Uri.EscapeDataString(floorId)}/spaces", cancellationToken);
            return spaces ?? [];
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load spaces: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<WizardBrickClass>> GetBrickClassesAsync(string type, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = Path.Combine(DataDirectory, "brick-classes.json");
            await using var stream = File.OpenRead(path);
            var byType = await JsonSerializer.DeserializeAsync<Dictionary<string, List<WizardBrickClass>>>(
                stream, CaseInsensitiveJsonOptions, cancellationToken);
            return byType is not null && byType.TryGetValue(type, out var classes) ? classes : [];
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load brick classes: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<WizardQudtUnit>> GetQudtUnitsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var path = Path.Combine(DataDirectory, "qudt-units.json");
            await using var stream = File.OpenRead(path);
            var units = await JsonSerializer.DeserializeAsync<List<WizardQudtUnit>>(
                stream, CaseInsensitiveJsonOptions, cancellationToken);
            return units ?? [];
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load QUDT units: {ex.Message}", ex);
        }
    }

    public async Task<BrickStatusResponse?> GetBrickStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<BrickStatusResponse?>("brick/status", cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<QudtStatusResponse?> GetQudtStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<QudtStatusResponse?>("qudt/status", cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
