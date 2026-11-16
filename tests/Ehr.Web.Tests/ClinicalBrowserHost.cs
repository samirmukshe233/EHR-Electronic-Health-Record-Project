using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ehr.Web.Tests;

// Exposes the real in-memory app to Chromium over loopback HTTPS. No alternate endpoints or production auth bypass.
// The normal test fixture's synthetic accounts and owner-checked deterministic model stub are used unchanged.
sealed class ClinicalBrowserHost : IAsyncDisposable
{
    readonly WebApplication host;
    readonly HttpClient client;
    readonly X509Certificate2 certificate;
    readonly RSA key;
    public string Url { get; private set; } = "";

    ClinicalBrowserHost(WebApps apps)
    {
        key = RSA.Create(2048);
        certificate = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        host = builder.Build();
        client = apps.OnPrem.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = Browser.Base, AllowAutoRedirect = false, HandleCookies = false });
        host.Run(ForwardAsync);
    }

    public static async Task<ClinicalBrowserHost> StartAsync(WebApps apps)
    {
        var server = new ClinicalBrowserHost(apps);
        await server.host.StartAsync();
        var address = server.host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        server.Url = address.Replace("127.0.0.1", "localhost", StringComparison.Ordinal);
        return server;
    }

    async Task ForwardAsync(HttpContext context)
    {
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
        if (context.Request.ContentLength > 0)
        {
            using var bytes = new MemoryStream();
            await context.Request.Body.CopyToAsync(bytes);
            request.Content = new ByteArrayContent(bytes.ToArray());
        }
        foreach (var header in context.Request.Headers)
        {
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
        using var response = await client.SendAsync(request);
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers.Concat(response.Content.Headers)) context.Response.Headers[header.Key] = header.Value.ToArray();
        context.Response.Headers.Remove("transfer-encoding");
        await response.Content.CopyToAsync(context.Response.Body);
    }

    public async ValueTask DisposeAsync()
    {
        await host.StopAsync();
        await host.DisposeAsync();
        client.Dispose();
        certificate.Dispose();
        key.Dispose();
    }
}
