using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Handlers;
using MassTransit;
using MassTransit.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.CompetitiveBenchmarks;

internal sealed class ProviderHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope? _scope;

    private ProviderHarness(string name, ServiceProvider provider, IServiceScope? scope, IHeroMessaging? hero, IScopedMediator? mediator)
    {
        Name = name;
        _provider = provider;
        _scope = scope;
        Hero = hero;
        Mediator = mediator;
        Work = provider.GetRequiredService<BenchmarkWork>();
    }

    public string Name { get; }
    public IHeroMessaging? Hero { get; }
    public IScopedMediator? Mediator { get; }
    public BenchmarkWork Work { get; }

    public static ProviderHarness Create(string name)
    {
        ValidateName(name);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<BenchmarkWork>();
        if (name == "hero")
        {
            services.AddSingleton<ICommandHandler<CompetitiveCommand>, HeroCommandHandler>();
            services.AddHeroMessaging(builder => builder.WithMediator().WithEventBus());
            var provider = services.BuildServiceProvider();
            return new ProviderHarness(name, provider, null, provider.GetRequiredService<IHeroMessaging>(), null);
        }
        else
        {
            services.AddSingleton<MassTransitCommandConsumer>();
            services.AddMediator(configurator => configurator.AddConsumer<MassTransitCommandConsumer>());
            var provider = services.BuildServiceProvider();
            var scope = provider.CreateScope();
            return new ProviderHarness(name, provider, scope, null, scope.ServiceProvider.GetRequiredService<IScopedMediator>());
        }
    }

    public static void ValidateName(string name)
    {
        if (name is not ("hero" or "masstransit"))
            throw new ArgumentException("Provider must be hero or masstransit.", nameof(name));
    }

    public Task SendAsync(CompetitiveCommand command, CancellationToken cancellationToken) =>
        Hero is { } hero ? hero.SendAsync(command, cancellationToken) : Mediator!.Send(command, cancellationToken);

    public Task SendUnhandledAsync(CancellationToken cancellationToken) =>
        Hero is { } hero ? hero.SendAsync(new UnhandledCommand(), cancellationToken) : Mediator!.Send(new UnhandledCommand(), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_scope is IAsyncDisposable asyncScope)
            await asyncScope.DisposeAsync().ConfigureAwait(false);
        else
            _scope?.Dispose();

        await _provider.DisposeAsync().ConfigureAwait(false);
    }
}
