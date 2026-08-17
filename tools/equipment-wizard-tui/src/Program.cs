using EquipmentWizardTui;
using EquipmentWizardTui.Services;
using Spectre.Console;

try
{
    // Load environment configuration
    var config = EnvironmentConfig.Load();

    // Create a delegating handler to inject the bearer token
    var tokenProvider = new TokenProvider(new HttpClient(), config.ApiOptions);
    
    var handler = new HttpClientHandler();
    var authHandler = new AuthDelegatingHandler(handler, tokenProvider);
    
    // Create HTTP client with auth handler
    var httpClient = new HttpClient(authHandler)
    {
        BaseAddress = new Uri(config.ApiOptions.BaseUrl)
    };

    // Create API client
    var apiClient = new EquipmentWizardApiClient(httpClient);

    // Run wizard
    var wizard = new EquipmentWizard(apiClient);
    await wizard.RunAsync();
}
catch (InvalidOperationException ex)
{
    AnsiConsole.MarkupLine($"[red]Configuration Error:[/] {ex.Message}");
    AnsiConsole.MarkupLine("[dim]Please check your .env file and ensure all required environment variables are set.[/]");
    Environment.Exit(1);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Fatal Error:[/] {ex.Message}");
    if (ex.InnerException != null)
    {
        AnsiConsole.MarkupLine($"[dim]{ex.InnerException.Message}[/]");
    }
    Environment.Exit(1);
}

/// <summary>
/// Custom delegating handler that injects bearer tokens into HTTP requests
/// </summary>
file sealed class AuthDelegatingHandler(HttpMessageHandler innerHandler, TokenProvider tokenProvider) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await tokenProvider.AddBearerTokenAsync(request, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}

