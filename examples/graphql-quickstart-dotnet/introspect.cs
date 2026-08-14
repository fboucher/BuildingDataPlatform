#:property TargetFramework=net10.0
#:property PublishAot=false
#:package DotNetEnv@3.2.0

using DotNetEnv;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

const string TokenEnv = "BDP_API_TOKEN";

// GraphQL names are letters, digits and underscores.
var graphQlName = new Regex(@"^[_A-Za-z][_0-9A-Za-z]*$");

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

if (options.Type is not null && !graphQlName.IsMatch(options.Type))
{
    Console.Error.WriteLine($"\"{options.Type}\" is not a GraphQL type name");
    return 2;
}

Env.Load();
string? token = Environment.GetEnvironmentVariable(TokenEnv);
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine($"set {TokenEnv} in your environment or add it to a .env file in this folder");
    return 2;
}

if (!options.SkipTokenCheck && !await CheckTokenAsync(options.Endpoint, token))
{
    return 1;
}

if (options.Type is not null)
{
    // First, get all types to find a case-insensitive match
    var allTypesData = await TryPostAsync(
        options.Endpoint,
        token,
        "{ __schema { types { name kind } } }",
        "types"
    );

    string? actualTypeName = null;
    if (allTypesData is not null &&
        allTypesData.RootElement.TryGetProperty("data", out var allData) &&
        allData.TryGetProperty("__schema", out var allSchema) &&
        allSchema.TryGetProperty("types", out var allTypes))
    {
        // Find a case-insensitive match
        foreach (var type in allTypes.EnumerateArray())
        {
            if (type.TryGetProperty("name", out var searchName) &&
                searchName.GetString()?.Equals(options.Type, StringComparison.OrdinalIgnoreCase) == true)
            {
                actualTypeName = searchName.GetString();
                break;
            }
        }
    }

    if (actualTypeName is null)
    {
        Console.Error.WriteLine($"no type found matching \"{options.Type}\"");
        return 1;
    }

    var result = await TryPostAsync(
        options.Endpoint,
        token,
        "query TypeDetail($name: String!) { __type(name: $name) { name kind fields { name } } }",
        "type detail",
        new Dictionary<string, object?> { { "name", actualTypeName } }
    );

    if (result is null)
    {
        Console.Error.WriteLine($"no detail available for \"{actualTypeName}\"");
        return 1;
    }

    if (!result.RootElement.TryGetProperty("data", out var typeData))
    {
        Console.Error.WriteLine($"invalid response for type \"{actualTypeName}\"");
        return 1;
    }

    if (!typeData.TryGetProperty("__type", out var node) || node.ValueKind == JsonValueKind.Null)
    {
        Console.Error.WriteLine($"no detail available for \"{actualTypeName}\"");
        return 1;
    }

    if (!node.TryGetProperty("kind", out var kind) || !node.TryGetProperty("name", out var typeName))
    {
        Console.Error.WriteLine($"incomplete type definition for \"{options.Type}\"");
        return 1;
    }

    Console.WriteLine($"{kind} {typeName}");
    if (!node.TryGetProperty("fields", out var typeFields))
    {
        Console.Error.WriteLine("no fields available for this type");
        return 1;
    }
    foreach (var field in typeFields.EnumerateArray())
    {
        if (field.TryGetProperty("name", out var fieldName))
        {
            Console.WriteLine($"  - {fieldName}");
        }
    }

    return 0;
}

var data = await TryPostAsync(
    options.Endpoint,
    token,
    "{ __schema { queryType { fields { name description } } } }",
    "root queries"
);

if (data is null)
{
    Console.Error.WriteLine("could not list the root queries; see the message above");
    return 1;
}

if (!data.RootElement.TryGetProperty("data", out var dataProperty))
{
    Console.Error.WriteLine("could not parse GraphQL response: missing 'data' property");
    return 1;
}

if (!dataProperty.TryGetProperty("__schema", out var schema) ||
    !schema.TryGetProperty("queryType", out var queryType) ||
    queryType.ValueKind == JsonValueKind.Null ||
    !queryType.TryGetProperty("fields", out var rootFields))
{
    Console.Error.WriteLine("could not retrieve root query fields from schema");
    return 1;
}
Console.WriteLine($"endpoint: {options.Endpoint}\n");
Console.WriteLine($"{rootFields.GetArrayLength()} root queries:");
foreach (var field in rootFields.EnumerateArray())
{
    if (field.TryGetProperty("name", out var fieldName))
    {
        Console.WriteLine($"  {fieldName}");
        if (field.TryGetProperty("description", out var desc) && desc.ValueKind != JsonValueKind.Null)
        {
            Console.WriteLine($"      {desc}");
        }
    }
}

var typesData = await TryPostAsync(
    options.Endpoint,
    token,
    "{ __schema { types { name kind } } }",
    "types"
);

if (typesData is not null)
{
    if (!typesData.RootElement.TryGetProperty("data", out var typesDataProperty) ||
        !typesDataProperty.TryGetProperty("__schema", out var typeSchema) ||
        !typeSchema.TryGetProperty("types", out var types))
    {
        Console.Error.WriteLine("could not retrieve types from schema");
        return 1;
    }
    var visible = GetInspectableTypes(types);
    if (visible.Count > 0)
    {
        Console.WriteLine($"\n{visible.Count} object type(s) exposed. Inspect one with:");
        Console.WriteLine($"  dotnet introspect.cs --type {visible[0]}");
    }
}

Console.WriteLine("\nFor exploratory querying use the 'Try it' playground on the Schneider");
Console.WriteLine("Electric Exchange portal. A gateway sits in front of this endpoint and");
Console.WriteLine("refuses a request it does not like with a 403 and an HTML page - a large");
Console.WriteLine("introspection query, for instance. That is not a credentials problem: the");
Console.WriteLine("request never reached the API.");

return 0;


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
        if (name == "skip-token-check") { options.SkipTokenCheck = true; continue; }

        if (i + 1 >= args.Length) { error = $"missing value for {arg}"; return false; }
        string value = args[++i];

        switch (name)
        {
            case "endpoint":
                options.Endpoint = value;
                break;
            case "type":
                options.Type = value;
                break;
            default:
                error = $"unknown option: {arg}";
                return false;
        }
    }

    return true;
}

void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet introspect.cs");
    Console.WriteLine("  dotnet introspect.cs --type Site");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --endpoint <url>           (default: UAT endpoint)");
    Console.WriteLine("  --type <name>              show one type's field names (case-insensitive)");
    Console.WriteLine("  --skip-token-check         do not verify the token with REST API first");
    Console.WriteLine("  --help                     show this help");
}

async Task<bool> CheckTokenAsync(string endpoint, string token)
{
    // The GraphQL schema is served without authentication, so a successful introspection
    // proves nothing about the token. The REST API on the same host does enforce it and
    // answers 401 when it is expired or malformed, which is the question you actually have.
    string probe = endpoint.Replace("/graphql", "/api/Sites", StringComparison.Ordinal);

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    using var request = new HttpRequestMessage(HttpMethod.Get, probe);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    request.Headers.Add("X-Api-Version", "3.0");

    try
    {
        using var response = await http.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            Console.Error.WriteLine(
                "token rejected: HTTP 401 from the REST API.\n" +
                "  Copy a fresh one from BDP Portal > Credentials > System > API Token.\n" +
                "  Tokens are short-lived by design.");
            return false;
        }

        Console.WriteLine("token accepted (REST /api/Sites answered 200)\n");
        return true;
    }
    catch (TaskCanceledException)
    {
        Console.Error.WriteLine("could not verify the token: request timed out\n");
        return true;
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"could not verify the token: {ex.Message}\n");
        return true;
    }
}

async Task<JsonDocument?> TryPostAsync(
    string endpoint,
    string token,
    string query,
    string label,
    Dictionary<string, object?>? variables = null)
{
    try
    {
        return await PostAsync(endpoint, token, query, variables);
    }
    catch (GatewayRefused ex)
    {
        Console.Error.WriteLine($"  ({label}: {ex.Message})");
        return null;
    }
    catch (GraphQLError ex)
    {
        Console.Error.WriteLine($"  ({label}: {ex.Message})");
        return null;
    }
    catch (TransportError ex)
    {
        Console.Error.WriteLine($"  ({label}: {ex.Message})");
        return null;
    }
}

async Task<JsonDocument> PostAsync(
    string endpoint,
    string token,
    string query,
    Dictionary<string, object?>? variables = null)
{
    var document = new Dictionary<string, object?>
    {
        { "query", query },
    };
    if (variables is not null)
    {
        document["variables"] = variables;
    }

    var json = JsonSerializer.Serialize(document);
    using var http = new HttpClient();
    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

    try
    {
        using var response = await http.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            if (body.Contains("Application-Gateway", StringComparison.OrdinalIgnoreCase))
            {
                throw new GatewayRefused(
                    $"HTTP {(int)response.StatusCode} from the gateway — the request never reached the API"
                );
            }

            try
            {
                using var payloadDoc = JsonDocument.Parse(body);
                if (payloadDoc.RootElement.TryGetProperty("errors", out var errors) && 
                    errors.ValueKind == JsonValueKind.Array)
                {
                    throw new GraphQLError($"GraphQL errors:\n{JsonSerializer.Serialize(errors, new JsonSerializerOptions { WriteIndented = true })}");
                }
            }
            catch (JsonException)
            {
                // Not JSON
            }

            string snippet = body.Length <= 300 ? body : body[..300];
            throw new TransportError($"HTTP {(int)response.StatusCode}: {snippet}");
        }

        using var resultDoc = JsonDocument.Parse(body);
        if (resultDoc.RootElement.TryGetProperty("errors", out var errs) && 
            errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
        {
            throw new GraphQLError($"GraphQL errors:\n{JsonSerializer.Serialize(errs, new JsonSerializerOptions { WriteIndented = true })}");
        }

        return JsonDocument.Parse(body);
    }
    catch (TaskCanceledException ex)
    {
        throw new TransportError($"could not reach {endpoint}: request timed out", ex);
    }
    catch (HttpRequestException ex)
    {
        throw new TransportError($"could not reach {endpoint}: {ex.Message}", ex);
    }
}

List<string> GetInspectableTypes(JsonElement types)
{
    // Object types have fields. Every GraphQL schema carries Boolean, Float, ID, Int
    // and String, and none of them sorts far from the front, so suggesting the first name
    // from an unfiltered list sends the reader to a command that prints two words.
    var result = new List<string>();
    foreach (var type in types.EnumerateArray())
    {
        if (type.TryGetProperty("kind", out var kind) && 
            kind.GetString() == "OBJECT" &&
            type.TryGetProperty("name", out var name) &&
            !name.GetString()!.StartsWith("__"))
        {
            result.Add(name.GetString()!);
        }
    }
    result.Sort();
    return result;
}

sealed class Options
{
    public const string UatEndpoint = "https://ecostruxure-building-platform-api-uat.se.app/graphql";
    
    public string Endpoint { get; set; } = UatEndpoint;
    public string? Type { get; set; }
    public bool SkipTokenCheck { get; set; }
}

class GatewayRefused : Exception
{
    public GatewayRefused(string message) : base(message) { }
}

class GraphQLError : Exception
{
    public GraphQLError(string message) : base(message) { }
}

class TransportError : Exception
{
    public TransportError(string message, Exception? innerException = null) : base(message, innerException) { }
}


