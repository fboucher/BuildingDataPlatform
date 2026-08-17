using frankbiz_demo.SyncConsole;

var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
Directory.CreateDirectory(dataDir);

using var httpClient = new HttpClient();
var client = new SyncApiClient(httpClient);
var running = true;

Console.WriteLine("Bricks & QUDT Reference Data Sync");
Console.WriteLine();

while (running)
{
    try
    {
        DisplayMenu();
        var choice = Console.ReadLine()?.Trim().ToUpperInvariant();

        switch (choice)
        {
            case "1":
                await SyncBricks(client, dataDir);
                break;
            case "2":
                await SyncQudt(client, dataDir);
                break;
            case "3":
                await SyncBoth(client, dataDir);
                break;
            case "Q":
                running = false;
                Console.WriteLine();
                Console.WriteLine("Goodbye!");
                break;
            default:
                Console.WriteLine();
                Console.WriteLine("Invalid choice. Please select 1, 2, 3, or Q.");
                Console.WriteLine();
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine($"Error: {ex.Message}");
        Console.WriteLine();
    }
}

static void DisplayMenu()
{
    Console.WriteLine("What would you like to do?");
    Console.WriteLine("──────────────────────────");
    Console.WriteLine("1) Sync BrickSchema Classes");
    Console.WriteLine("2) Sync QUDT Units");
    Console.WriteLine("3) Sync Both");
    Console.WriteLine("Q) Quit");
    Console.WriteLine();
    Console.Write("Select option: ");
}

static async Task SyncBricks(SyncApiClient client, string dataDir)
{
    Console.WriteLine();
    Console.WriteLine("Syncing BrickSchema Classes...");
    var startedAt = DateTime.UtcNow;

    var (result, json) = await client.SyncBrickClassesAsync();
    var filePath = Path.Combine(dataDir, "brick-classes.json");
    await File.WriteAllTextAsync(filePath, json);

    Console.WriteLine();
    Console.WriteLine("BrickSchema sync complete.");
    Console.WriteLine($"Equipment classes: {result.EquipmentCount}");
    Console.WriteLine($"Sensor classes: {result.SensorCount}");
    Console.WriteLine($"Saved to: {filePath}");
    Console.WriteLine($"Completed at: {startedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
    Console.WriteLine();
}

static async Task SyncQudt(SyncApiClient client, string dataDir)
{
    Console.WriteLine();
    Console.WriteLine("Syncing QUDT Units...");
    var startedAt = DateTime.UtcNow;

    var (result, json) = await client.SyncQudtUnitsAsync();
    var filePath = Path.Combine(dataDir, "qudt-units.json");
    await File.WriteAllTextAsync(filePath, json);

    Console.WriteLine();
    Console.WriteLine("QUDT sync complete.");
    Console.WriteLine($"Units: {result.UnitCount}");
    Console.WriteLine($"Saved to: {filePath}");
    Console.WriteLine($"Completed at: {startedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
    Console.WriteLine();
}

static async Task SyncBoth(SyncApiClient client, string dataDir)
{
    Console.WriteLine();
    Console.WriteLine("Syncing BrickSchema Classes and QUDT Units...");

    var (brickResult, brickJson) = await client.SyncBrickClassesAsync();
    var brickPath = Path.Combine(dataDir, "brick-classes.json");
    await File.WriteAllTextAsync(brickPath, brickJson);

    var (qudtResult, qudtJson) = await client.SyncQudtUnitsAsync();
    var qudtPath = Path.Combine(dataDir, "qudt-units.json");
    await File.WriteAllTextAsync(qudtPath, qudtJson);

    Console.WriteLine();
    Console.WriteLine("BrickSchema sync complete.");
    Console.WriteLine($"Equipment classes: {brickResult.EquipmentCount}");
    Console.WriteLine($"Sensor classes: {brickResult.SensorCount}");
    Console.WriteLine($"Saved to: {brickPath}");
    Console.WriteLine();
    Console.WriteLine("QUDT sync complete.");
    Console.WriteLine($"Units: {qudtResult.UnitCount}");
    Console.WriteLine($"Saved to: {qudtPath}");
    Console.WriteLine();
}
