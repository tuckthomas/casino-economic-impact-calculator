using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using SaveNEIN.Client.Pages;

namespace SaveNEIN.Server.Tests;

public sealed class HomeHeroStartupTests
{
    [Fact]
    public async Task StartupHeroRendersCompleteStyledLayoutWithoutJavaScript()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJSRuntime, NoStartupJavaScript>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<HomeHero>(ParameterView.Empty);
            return result.ToHtmlString();
        });

        Assert.Contains("home-hero-gradient", html);
        Assert.Contains("hero-mobile-intro", html);
        Assert.Contains("hero-visual-stage", html);
        Assert.Contains("data-slot-machine", html);
        Assert.Contains("href=\"/get-involved\"", html);
        Assert.Contains("href=\"/fact-checks\"", html);
        Assert.Contains("btn-raised", html);
        Assert.DoesNotContain("startup-hero-content", html);
    }

    private sealed class NoStartupJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("Static hero rendering must not invoke JavaScript.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
