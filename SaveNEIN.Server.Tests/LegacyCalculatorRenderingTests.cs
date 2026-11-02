using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using SaveNEIN.Client.Pages;

namespace SaveNEIN.Server.Tests;

public sealed class LegacyCalculatorRenderingTests
{
    [Fact]
    public async Task CalculatorRendersMapAndAllInputsWithoutRequestingServerModelData()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJSRuntime, StaticJavaScript>();
        services.AddSingleton(new HttpClient(new RejectModelRequests()));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<EconomicImpact>(ParameterView.Empty);
            return result.ToHtmlString();
        });

        Assert.Contains("id=\"impact-map\"", html);
        Assert.Contains("id=\"input-agr\"", html);
        Assert.Contains("id=\"input-revenue\"", html);
        Assert.Contains("id=\"input-cost-crime\"", html);
        Assert.Contains("id=\"input-rate\"", html);
        Assert.Contains("id=\"btn-generate-pdf\"", html);
        Assert.DoesNotContain("canonical-economic-model", html);
        Assert.DoesNotContain("Currently Under Renovation", html);
    }

    private sealed class RejectModelRequests : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Rendering the browser calculator must not load server-model data.");
    }

    private sealed class StaticJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
