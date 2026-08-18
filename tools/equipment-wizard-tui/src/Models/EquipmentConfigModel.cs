using System.Text.Json;
using System.Text.Json.Serialization;

namespace EquipmentWizardTui.Models;

public sealed class EquipmentConfigModel
{
    // Step 1 — Site + Device Group
    public string? SelectedSiteId { get; set; }
    public string? SelectedSiteName { get; set; }
    public string? SourceId { get; set; }             // DeviceGroup id
    public string? PointGroupReferenceId { get; set; } // DeviceGroup referenceId
    public string? SelectedDeviceGroupName { get; set; }

    // Step 2 — Equipment
    public string? EquipmentReferenceId { get; set; }
    public string? VendorId { get; set; }
    public string? EquipmentExactType { get; set; }
    public string EquipmentProductType { get; set; } = "Generic";
    public string? EquipmentName { get; set; }
    public string? LocatedInReferenceId { get; set; }

    // Step 2 — Location cascade
    public string? SelectedBuildingId { get; set; }
    public string? SelectedFloorId { get; set; }
    public string? SelectedSpaceId { get; set; }

    // Step 3 — Point
    public string? PointReferenceId { get; set; }
    public bool PointReferenceIdManuallyEdited { get; set; }
    public string? PointExactType { get; set; }
    public string? PointName { get; set; }
    public bool PointIsWritable { get; set; }
    public string? UnitExactType { get; set; }
    public string PointDataType { get; set; } = "Double";

    public bool Step1Valid =>
        !string.IsNullOrEmpty(SourceId) && !string.IsNullOrEmpty(PointGroupReferenceId);

    // Step 2 — Equipment fields only
    public bool Step2Valid =>
        !string.IsNullOrEmpty(EquipmentReferenceId) &&
        !string.IsNullOrEmpty(VendorId) &&
        !string.IsNullOrEmpty(EquipmentExactType) &&
        !string.IsNullOrEmpty(EquipmentName);

    // Step 3 — Location
    public bool Step3Valid =>
        !string.IsNullOrEmpty(LocatedInReferenceId);

    // Step 4 — Point
    public bool Step4Valid =>
        !string.IsNullOrEmpty(PointReferenceId) &&
        !string.IsNullOrEmpty(PointExactType) &&
        !string.IsNullOrEmpty(PointName) &&
        !string.IsNullOrEmpty(UnitExactType);

    public bool AllValid => Step1Valid && Step2Valid && Step3Valid && Step4Valid;

    public string ToJson()
    {
        var payload = new
        {
            Version = "1.0",
            SourceId,
            PointGroupReferenceId,
            Equipments = new[]
            {
                new
                {
                    ReferenceId = EquipmentReferenceId,
                    VendorId,
                    ExactType = EquipmentExactType,
                    ProductType = EquipmentProductType,
                    Name = EquipmentName,
                    LocatedInReferenceId,
                    MonitorsReferenceId = (string?)null,
                    Points = new[]
                    {
                        new
                        {
                            ReferenceId = PointReferenceId,
                            ExactType = PointExactType,
                            Name = PointName,
                            IsWritable = PointIsWritable,
                            UnitExactType,
                            DataType = PointDataType
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        });
    }

    public void AutoGenerateEquipmentReferenceId()
    {
        if (!string.IsNullOrEmpty(EquipmentReferenceId) || string.IsNullOrWhiteSpace(EquipmentName))
            return;

        var words = System.Text.RegularExpressions.Regex.Split(EquipmentName.Trim(), @"[\s\-_]+");
        EquipmentReferenceId = string.Concat(words.Where(w => w.Length > 0)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    public void AutoGeneratePointReferenceId()
    {
        if (PointReferenceIdManuallyEdited || string.IsNullOrEmpty(PointName))
        {
            return;
        }

        var safePointName = System.Text.RegularExpressions.Regex.Replace(PointName, @"[^a-zA-Z0-9\-]", "");
        var baseRef = EquipmentReferenceId ?? string.Empty;
        PointReferenceId = string.IsNullOrEmpty(baseRef)
            ? safePointName
            : $"{baseRef}-{safePointName}";
    }
}

public sealed record WizardSite(string Id, string Name, int BuildingCount);
public sealed record WizardDeviceGroup(string Id, string ReferenceId, string Name);
public sealed record WizardBuilding(string Id, string Name, string ReferenceId);
public sealed record WizardFloor(string Id, string Name, string ReferenceId);
public sealed record WizardSpace(string Id, string Name, string ReferenceId);
public sealed record WizardBrickClass(string Label, string Uri);
public sealed record WizardQudtUnit(string Label, string Uri);
public sealed record BrickStatusResponse(DateTime? LastSyncedAt, int EquipmentCount, int SensorCount);
public sealed record QudtStatusResponse(DateTime? LastSyncedAt, int UnitCount);
