using Spectre.Console;
using Sharprompt;
using EquipmentWizardTui.Models;
using EquipmentWizardTui.Services;

namespace EquipmentWizardTui;

public sealed class EquipmentWizard
{
    private readonly EquipmentWizardApiClient _apiClient;
    private readonly EquipmentConfigModel _model = new();
    private readonly static string[] Vendors = ["Ebo", "EcoStruxureBuildingIotSensors", "Density", "Vergesense", "Metry"];

    private IReadOnlyList<WizardSite> _sites = [];
    private WizardSite? _selectedSite;
    private IReadOnlyList<WizardDeviceGroup> _deviceGroups = [];
    private WizardDeviceGroup? _selectedDeviceGroup;

    private IReadOnlyList<WizardBuilding> _buildings = [];
    private WizardBuilding? _selectedBuilding;
    private IReadOnlyList<WizardFloor> _floors = [];
    private WizardFloor? _selectedFloor;
    private IReadOnlyList<WizardSpace> _spaces = [];
    private WizardSpace? _selectedSpace;

    private IReadOnlyList<WizardBrickClass> _equipmentClasses = [];
    private WizardBrickClass? _selectedEquipmentClass;
    private IReadOnlyList<WizardBrickClass> _sensorClasses = [];
    private WizardBrickClass? _selectedSensorClass;
    private IReadOnlyList<WizardQudtUnit> _qudtUnits = [];
    private WizardQudtUnit? _selectedQudtUnit;

    public EquipmentWizard(EquipmentWizardApiClient apiClient)
    {
        _apiClient = apiClient;
    }


    private void DisplayStepHeader(int currentStep, string title)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[bold cyan]" + $"Step {currentStep}/5: {title}" + "[/]");
    }

    public async Task RunAsync()
    {
        try
        {
            AnsiConsole.MarkupLine("[bold green]Welcome to Equipment Wizard TUI[/]");
            AnsiConsole.MarkupLine("[dim]Creating equipment configuration step by step[/]\n");

            // Initialize data
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Loading initial data...[/]", async _ =>
                {
                    _sites = await _apiClient.GetSitesAsync();
                });

            if (_sites.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]Error: No sites available[/]");
                return;
            }

            int currentStep = 1;
            bool finished = false;

            while (!finished)
            {
                switch (currentStep)
                {
                    case 1:
                        if (await RunStep1Async()) currentStep++;
                        break;
                    case 2:
                        if (await RunStep2Async()) currentStep++;
                        break;
                    case 3:
                        if (await RunStep3Async()) currentStep++;
                        break;
                    case 4:
                        if (await RunStep4Async()) currentStep++;
                        break;
                    case 5:
                        finished = await RunStep5Async();
                        break;
                    default:
                        finished = true;
                        break;
                }
            }

            AnsiConsole.MarkupLine("[bold green]✓ Wizard completed[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            if (!string.IsNullOrWhiteSpace(ex.InnerException?.Message))
            {
                AnsiConsole.MarkupLine($"[dim]{ex.InnerException.Message}[/]");
            }
        }
    }

    private async Task<bool> RunStep1Async()
    {
        DisplayStepHeader(1, "Site & Device Group");

        try
        {
            // Select Site
            _selectedSite = AnsiConsole.Prompt(
                new SelectionPrompt<WizardSite>()
                    .Title("Select a [green]Site[/]:")
                    .AddChoices(_sites)
                    .MoreChoicesText("[grey](Use arrow keys, type to search)[/]")
                    .UseConverter(s => s.Name)
                    .PageSize(10));

            _model.SelectedSiteId = _selectedSite.Id;
            _model.SelectedSiteName = _selectedSite.Name;

            // Load device groups
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Loading device groups...[/]", async _ =>
                {
                    _deviceGroups = await _apiClient.GetDeviceGroupsBySiteAsync(_selectedSite.Id);
                });

            if (_deviceGroups.Count == 0)
            {
                AnsiConsole.MarkupLine($"[red]No device groups found for site '{_selectedSite.Name}'[/]");
                return false;
            }

            // Select Device Group
            _selectedDeviceGroup = AnsiConsole.Prompt(
                new SelectionPrompt<WizardDeviceGroup>()
                    .Title("Select a [green]Device Group[/]:")
                    .AddChoices(_deviceGroups)
                    .MoreChoicesText("[grey](Use arrow keys, type to search)[/]")
                    .UseConverter(g => string.IsNullOrWhiteSpace(g.Name) ? g.ReferenceId : g.Name)
                    .PageSize(10));

            _model.SourceId = _selectedDeviceGroup.Id;
            _model.PointGroupReferenceId = _selectedDeviceGroup.ReferenceId;
            _model.SelectedDeviceGroupName = _selectedDeviceGroup.Name;

            // Load buildings too
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Loading buildings...[/]", async _ =>
                {
                    _buildings = await _apiClient.GetBuildingsBySiteAsync(_selectedSite.Id);
                });

            return true;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled[/]");
            return false;
        }
    }

    private async Task<bool> RunStep2Async()
    {
        DisplayStepHeader(2, "Equipment Details");

        try
        {
            // Load equipment classes if needed
            if (_equipmentClasses.Count == 0)
            {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("[yellow]Loading equipment types...[/]", async _ =>
                    {
                        _equipmentClasses = await _apiClient.GetBrickClassesAsync("equipment");
                    });
            }

            // Equipment Name
            var equipmentName = AnsiConsole.Ask<string>("Equipment [green]Name[/]:");
            _model.EquipmentName = equipmentName;
            _model.AutoGenerateEquipmentReferenceId();

            // Equipment Reference ID
            var refId = AnsiConsole.Ask($"Equipment [green]Reference ID[/] (auto-suggested: [yellow]{_model.EquipmentReferenceId}[/]):", _model.EquipmentReferenceId ?? "");
            _model.EquipmentReferenceId = refId;
            _model.AutoGeneratePointReferenceId();

            // Vendor
            var vendor = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select [green]Vendor[/]:")
                    .AddChoices(Vendors)
                    .PageSize(10));
            _model.VendorId = vendor;

            // Equipment Type
            _selectedEquipmentClass = AnsiConsole.Prompt(
                new SelectionPrompt<WizardBrickClass>()
                    .Title("Select [green]Equipment Type[/] (ExactType):")
                    .AddChoices(_equipmentClasses)
                    .EnableSearch()
                    .SearchPlaceholderText("[grey](type to filter, case-insensitive)[/]")
                    .MoreChoicesText("[grey](Use arrow keys and type to filter)[/]")
                    .UseConverter(c => c.Label)
                    .PageSize(10));
            _model.EquipmentExactType = _selectedEquipmentClass.Uri;
            AnsiConsole.MarkupLine("Equipment [green]Type[/] (ExactType): [yellow]" + _selectedEquipmentClass.Label + "[/]");

            // Product Type (optional, has default)
            var productType = AnsiConsole.Ask("Equipment Product Type (default: Generic):", _model.EquipmentProductType);
            _model.EquipmentProductType = productType;

            return true;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled[/]");
            return false;
        }
    }

    private async Task<bool> RunStep3Async()
    {
        DisplayStepHeader(3, "Location");

        try
        {
            if (_buildings.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]No buildings found for this site[/]");
                return false;
            }

            // Building
            _selectedBuilding = AnsiConsole.Prompt(
                new SelectionPrompt<WizardBuilding>()
                    .Title("Select [green]Building[/]:")
                    .AddChoices(_buildings)
                    .MoreChoicesText("[grey](Use arrow keys, type to search)[/]")
                    .UseConverter(b => b.Name)
                    .PageSize(10));
            _model.SelectedBuildingId = _selectedBuilding.Id;

            // Load floors
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Loading floors...[/]", async _ =>
                {
                    _floors = await _apiClient.GetFloorsByBuildingAsync(_selectedBuilding.Id);
                });

            if (_floors.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]No floors found for this building[/]");
                return false;
            }

            // Floor
            _selectedFloor = AnsiConsole.Prompt(
                new SelectionPrompt<WizardFloor>()
                    .Title("Select [green]Floor[/]:")
                    .AddChoices(_floors)
                    .MoreChoicesText("[grey](Use arrow keys, type to search)[/]")
                    .UseConverter(f => f.Name)
                    .PageSize(10));
            _model.SelectedFloorId = _selectedFloor.Id;

            // Load spaces
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Loading spaces...[/]", async _ =>
                {
                    _spaces = await _apiClient.GetSpacesByFloorAsync(_selectedFloor.Id);
                });

            if (_spaces.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]No spaces found for this floor[/]");
                return false;
            }

            // Space
            _selectedSpace = AnsiConsole.Prompt(
                new SelectionPrompt<WizardSpace>()
                    .Title("Select [green]Space[/] (Located In):")
                    .AddChoices(_spaces)
                    .MoreChoicesText("[grey](Use arrow keys, type to search)[/]")
                    .UseConverter(s => s.Name)
                    .PageSize(10));
            _model.SelectedSpaceId = _selectedSpace.Id;
            _model.LocatedInReferenceId = _selectedSpace.ReferenceId;

            return true;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled[/]");
            return false;
        }
    }

    private async Task<bool> RunStep4Async()
    {
        DisplayStepHeader(4, "Point (Sensor)");

        try
        {
            if (_sensorClasses.Count == 0)
            {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("[yellow]Loading sensor types...[/]", async _ =>
                    {
                        _sensorClasses = await _apiClient.GetBrickClassesAsync("sensor");
                    });
            }

            if (_qudtUnits.Count == 0)
            {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("[yellow]Loading units...[/]", async _ =>
                    {
                        _qudtUnits = await _apiClient.GetQudtUnitsAsync();
                    });
            }

            // Sensor Type
            _selectedSensorClass = SearchablePrompt.SelectWithSearch(
                _sensorClasses,
                "Select [green]Point Type[/] (ExactType):",
                c => c.Label,
                pageSize: 10);
            _model.PointExactType = _selectedSensorClass.Uri;
            AnsiConsole.MarkupLine("Point [green]Type[/] (ExactType): [yellow]" + _selectedSensorClass.Label + "[/]");

            // Point Name
            var pointName = AnsiConsole.Ask<string>("Point [green]Name[/]:", _selectedSensorClass.Label);
            _model.PointName = pointName;
            _model.AutoGeneratePointReferenceId();

            // Point Reference ID
            var pointRefId = AnsiConsole.Ask($"Point [green]Reference ID[/] (auto-suggested: [yellow]{_model.PointReferenceId}[/]):", _model.PointReferenceId ?? "");
            _model.PointReferenceId = pointRefId;
            _model.PointReferenceIdManuallyEdited = true;

            // Unit
            _selectedQudtUnit = SearchablePrompt.SelectWithSearch(
                _qudtUnits,
                "Select [green]Unit[/] (UnitExactType):",
                u => u.Label,
                pageSize: 10);
            _model.UnitExactType = _selectedQudtUnit.Uri;
            AnsiConsole.MarkupLine("[green]Unit[/] (UnitExactType): [yellow]" + _selectedQudtUnit.Label + "[/]");

            // Is Writable
            var isWritable = AnsiConsole.Confirm("Is [green]Writable[/]?", false);
            _model.PointIsWritable = isWritable;

            // Data Type (optional)
            var dataType = AnsiConsole.Ask("Data Type (default: Double):", _model.PointDataType);
            _model.PointDataType = dataType;

            return true;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled[/]");
            return false;
        }
    }

    private async Task<bool> RunStep5Async()
    {
        DisplayStepHeader(5, "Review & Download");

        // Show summary
        var table = new Table();
        table.AddColumn("Field");
        table.AddColumn("Value");

        table.AddRow("Site", _model.SelectedSiteName ?? "");
        table.AddRow("Device Group", _model.SelectedDeviceGroupName ?? "");
        table.AddRow("Equipment Name", _model.EquipmentName ?? "");
        table.AddRow("Equipment Type", _selectedEquipmentClass?.Label ?? "");
        table.AddRow("Building", _selectedBuilding?.Name ?? "");
        table.AddRow("Floor", _selectedFloor?.Name ?? "");
        table.AddRow("Space", _selectedSpace?.Name ?? "");
        table.AddRow("Point Name", _model.PointName ?? "");
        table.AddRow("Point Type", _selectedSensorClass?.Label ?? "");
        table.AddRow("Unit", _selectedQudtUnit?.Label ?? "");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Menu
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices(new[] { "Download JSON", "Edit a step", "Exit without saving" }));

        if (choice == "Download JSON")
        {
            return await DownloadJsonAsync();
        }
        else if (choice == "Edit a step")
        {
            var stepChoice = AnsiConsole.Prompt(
                new SelectionPrompt<int>()
                    .Title("Which step would you like to edit?")
                    .AddChoices(new[] { 1, 2, 3, 4 })
                    .UseConverter(s => $"Step {s}"));

            return false; // Go back to edit
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Exiting without saving[/]");
            return true;
        }
    }

    private async Task<bool> DownloadJsonAsync()
    {
        var json = _model.ToJson();
        var filename = "equipment-config.json";
        var filepath = Path.Combine(Directory.GetCurrentDirectory(), filename);

        try
        {
            await File.WriteAllTextAsync(filepath, json);
            AnsiConsole.MarkupLine($"[green]✓ JSON saved to:[/] [yellow]{filepath}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine(json);
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error saving file: {ex.Message}[/]");
            return false;
        }
    }
}






