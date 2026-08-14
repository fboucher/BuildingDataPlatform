#:property TargetFramework=net10.0
#:property PublishAot=false
#:package DotNetEnv@3.2.0

using DotNetEnv;
using System.Net.Http.Headers;
using System.Text.Json;

const string TokenEnv = "BDP_API_TOKEN";

var resourceToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["organizations"] = "/api/Organizations",
    ["sites"] = "/api/Sites",
    ["buildings"] = "/api/Buildings",
};

if (!TryParseOptions(args, out Options? options, out string? parseError, out bool showHelp))
{
    if (!string.IsNullOrWhiteSpace(parseError))
    {
        Console.Error.WriteLine(parseError);
    }

    PrintUsage();
    return 2;
}

if (showHelp)
{
    PrintUsage();
    return 0;
}

if (options is null)
{
    Console.Error.WriteLine("invalid options");
    return 2;
}

if (!resourceToPath.ContainsKey(options.Resource))
{
    Console.Error.WriteLine("--resource must be one of: organizations, sites, buildings");
    return 2;
}

if (options.Take < 0 || options.Skip < 0)
{
    Console.Error.WriteLine("--take and --skip must be non-negative");
    return 2;
}

if (!string.Equals(options.ApiVersion, "2.0", StringComparison.Ordinal) &&
    !string.Equals(options.ApiVersion, "3.0", StringComparison.Ordinal))
{
    Console.Error.WriteLine("--api-version must be 2.0 or 3.0");
    return 2;
}

Env.Load();
string? token = Environment.GetEnvironmentVariable(TokenEnv);
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine($"set {TokenEnv} in your environment or add it to a .env file in this folder");
    return 2;
}

string url;
try
{
    url = BuildUrl(options.BaseUrl, options.Resource, options.SiteId, options.Take, options.Skip, resourceToPath);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

Console.WriteLine($"GET {url}");

try
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    request.Headers.Add("X-Api-Version", options.ApiVersion);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    using HttpResponseMessage response = await http.SendAsync(request);
    string body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        PrintError((int)response.StatusCode, body);
        return 1;
    }

    Console.WriteLine($"status: {(int)response.StatusCode}");

    if (options.Raw)
    {
        PrintRawJson(body);
    }
    else
    {
        SummarizeJson(body);
    }

    return 0;
}
catch (TaskCanceledException)
{
    Console.Error.WriteLine("network error: request timed out");
    return 1;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"network error: {ex.Message}");
    return 1;
}


void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --file ./call_rest_api.cs -- --resource sites --take 5");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --resource organizations|sites|buildings   (default: sites)");
    Console.WriteLine("  --site-id <GUID>                           required for buildings");
    Console.WriteLine("  --take <int>                               (default: 5)");
    Console.WriteLine("  --skip <int>                               (default: 0)");
    Console.WriteLine("  --base-url <url>                           (default: UAT host)");
    Console.WriteLine("  --api-version 2.0|3.0                      (default: 3.0)");
    Console.WriteLine("  --timeout <seconds>                        (default: 30)");
    Console.WriteLine("  --raw                                      print full JSON response");
}

bool TryParseOptions(string[] args, out Options? options, out string? error, out bool showHelp)
{
    options = new Options();
    error = null;
    showHelp = false;

    for (int i = 0; i < args.Length; i++)
    {
        string arg = args[i];
        if (!arg.StartsWith("--"))
        {
            error = $"invalid argument: {arg}";
            return false;
        }

        string name = arg[2..];
        if (name == "help") { showHelp = true; continue; }
        if (name == "raw") { options.Raw = true; continue; }

        if (i + 1 >= args.Length) { error = $"missing value for {arg}"; return false; }
        string value = args[++i];

        switch (name)
        {
            case "resource":
                options.Resource = value;
                break;
            case "site-id":
                options.SiteId = value;
                break;
            case "take":
                if (!int.TryParse(value, out int take)) { error = "--take must be an integer"; return false; }
                options.Take = take;
                break;
            case "skip":
                if (!int.TryParse(value, out int skip)) { error = "--skip must be an integer"; return false; }
                options.Skip = skip;
                break;
            case "base-url":
                options.BaseUrl = value;
                break;
            case "api-version":
                options.ApiVersion = value;
                break;
            case "timeout":
                if (!int.TryParse(value, out int timeout)) { error = "--timeout must be an integer"; return false; }
                options.TimeoutSeconds = timeout;
                break;
            default:
                error = $"unknown option: {arg}";
                return false;
        }
    }

    return true;
}

string BuildUrl(
    string baseUrl,
    string resource,
    string? siteId,
    int take,
    int skip,
    Dictionary<string, string> lookup)
{
    if (!lookup.TryGetValue(resource, out string? path))
    {
        throw new ArgumentException("unsupported resource");
    }

    if (string.Equals(resource, "buildings", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrWhiteSpace(siteId))
    {
        throw new ArgumentException("--site-id is required when --resource buildings is used");
    }

    var query = new List<string>
    {
        $"take={Uri.EscapeDataString(take.ToString())}",
        $"skip={Uri.EscapeDataString(skip.ToString())}",
    };

    if (string.Equals(resource, "buildings", StringComparison.OrdinalIgnoreCase))
    {
        query.Add($"siteId={Uri.EscapeDataString(siteId!)}");
    }

    return $"{baseUrl.TrimEnd('/')}{path}?{string.Join("&", query)}";
}

void SummarizeJson(string body)
{
    if (string.IsNullOrWhiteSpace(body))
    {
        Console.WriteLine("items: 0");
        return;
    }

    using JsonDocument document = JsonDocument.Parse(body);
    JsonElement root = document.RootElement;

    if (root.ValueKind == JsonValueKind.Array)
    {
        Console.WriteLine($"items: {root.GetArrayLength()}");
        foreach (JsonElement item in root.EnumerateArray().Take(10))
        {
            PrintItemLine(item);
        }

        return;
    }

    if (root.ValueKind == JsonValueKind.Object)
    {
        if (root.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
        {
            Console.WriteLine($"items: {items.GetArrayLength()}");
            foreach (JsonElement item in items.EnumerateArray().Take(10))
            {
                PrintItemLine(item);
            }

            return;
        }

        string fields = string.Join(", ", root.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        Console.WriteLine($"top-level fields: {fields}");
        return;
    }

    Console.WriteLine(root.ValueKind.ToString());
}

void PrintItemLine(JsonElement item)
{
    if (item.ValueKind != JsonValueKind.Object)
    {
        Console.WriteLine($"  - {item}");
        return;
    }

    string id = item.TryGetProperty("id", out JsonElement idElement)
        ? idElement.ToString() ?? "-"
        : "-";
    string name = item.TryGetProperty("name", out JsonElement nameElement)
        ? nameElement.ToString() ?? "-"
        : "-";

    Console.WriteLine($"  - {id} | {name}");
}

void PrintRawJson(string body)
{
    if (string.IsNullOrWhiteSpace(body))
    {
        Console.WriteLine("{}");
        return;
    }

    using JsonDocument document = JsonDocument.Parse(body);
    string pretty = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
    {
        WriteIndented = true,
    });
    Console.WriteLine(pretty);
}

void PrintError(int code, string body)
{
    string snippet = body.Length <= 400
        ? body.Trim().Replace("\n", " ", StringComparison.Ordinal)
        : body[..400].Trim().Replace("\n", " ", StringComparison.Ordinal);

    if (code == 401)
    {
        Console.Error.WriteLine("HTTP 401: token expired or malformed. Copy a fresh token from the portal.");
        return;
    }

    if (code == 403 && body.Contains("Application-Gateway", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("HTTP 403: refused before the API. The request did not reach the API.");
        return;
    }

    if (code == 400 && body.Contains("Unsupported API Version", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("HTTP 400: unsupported API version. Use 2.0 or 3.0.");
        return;
    }

    Console.Error.WriteLine($"HTTP {code}: {snippet}");
}

sealed class Options
{
    public string Resource { get; set; } = "sites";
    public string? SiteId { get; set; }
    public int Take { get; set; } = 5;
    public int Skip { get; set; } = 0;
    public string BaseUrl { get; set; } = "https://ecostruxure-building-platform-api-uat.se.app";
    public string ApiVersion { get; set; } = "3.0";
    public int TimeoutSeconds { get; set; } = 30;
    public bool Raw { get; set; }
}
