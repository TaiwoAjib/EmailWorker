using EmailWorkerService;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddHttpClient();

// Bind configuration section
builder.Services.Configure<EmailWorkerSettings>(builder.Configuration.GetSection("EmailWorkerSettings"));

// Register TokenService as a singleton so the token cache persists across requests
builder.Services.AddSingleton(sp =>
{
    var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailWorkerSettings>>().Value;
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<TokenService>>();
    return new TokenService(httpClientFactory, settings.Auth, logger);
});

var host = builder.Build();
host.Run();
