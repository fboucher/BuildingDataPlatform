using dotenv.net;
using dotenv.net.Utilities;

namespace EquipmentWizardTui.Services;

public sealed class EnvironmentConfig
{
    public EcoStruxureApiOptions ApiOptions { get; }

    private EnvironmentConfig(EcoStruxureApiOptions apiOptions)
    {
        ApiOptions = apiOptions;
    }

    public static EnvironmentConfig Load()
    {
        var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
        
        if (File.Exists(envPath))
        {
            DotEnv.Load(new DotEnvOptions(envFilePaths: [envPath]));
        }

        var apiOptions = new EcoStruxureApiOptions
        {
            BaseUrl = NormalizeApiBaseUrl(GetEnvOrThrow("API_BASE_URL")),
            TenantId = GetEnvOrThrow("TENANT_ID"),
            ClientId = GetEnvOrThrow("CLIENT_ID"),
            ClientSecret = GetEnvOrThrow("CLIENT_SECRET"),
            Scope = GetEnvOrThrow("CREDS_SCOPES")
        };

        return new EnvironmentConfig(apiOptions);
    }

    private static string GetEnvOrThrow(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required environment variable: {key}");
        }
        return value;
    }

    private static string NormalizeApiBaseUrl(string value)
    {
        var uri = new Uri(value.TrimEnd('/') + "/", UriKind.Absolute);
        var path = uri.AbsolutePath.TrimEnd('/');
        var builder = new UriBuilder(uri);

        if (!path.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = path + "/api/";
        }
        else
        {
            builder.Path = path + "/";
        }

        return builder.Uri.ToString();
    }

    private static string GetEnvOrDefault(string key, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }
}
