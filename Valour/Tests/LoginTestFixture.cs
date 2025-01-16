using Microsoft.AspNetCore.Hosting;
using Valour.Sdk.Client;
using Valour.Server;

namespace Valour.Tests;

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class LoginTestFixture : IAsyncLifetime
{
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public ValourClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Create the underlying WebApplicationFactory
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("https_port", "5001");
                builder.UseUrls("http://localhost:5000");
            });

        // Create a client from the factory
        var httpClient = Factory.CreateClient();
        
        Client = new ValourClient("https://localhost:5001/", httpProvider: new TestHttpProvider(Factory));
        httpClient.BaseAddress = new Uri(Client.BaseAddress);
        Client.SetHttpClient(httpClient);

        // Sets up the primary node
        await Client.NodeService.SetupPrimaryNodeAsync();
        
        Console.WriteLine("Initialized LoginTestFixture");
    }

    public Task DisposeAsync()
    {
        // Clean up if needed
        Factory.Dispose();
        return Task.CompletedTask;
    }
}
