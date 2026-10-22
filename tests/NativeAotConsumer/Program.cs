using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage;
using Microsoft.Extensions.DependencyInjection;
using HeroMessaging.AotSmoke;

if (RuntimeFeature.IsDynamicCodeSupported || RuntimeFeature.IsDynamicCodeCompiled)
{
    Console.Error.WriteLine("NativeAOT execution required; JIT is not accepted.");
    return 1;
}
if (args.Length != 2 || Environment.Version.ToString() != args[1])
    throw new InvalidOperationException("Expected exact package identity and runtime arguments.");

var state = new ProbeState();
var clock = new ProbeClock();
IBasicMessageStorage storage = new InMemoryMessageStorage(clock);
var services = new ServiceCollection();
services.AddSingleton(state);
services.AddSingleton(storage);
services.AddGeneratedHandlers();

var registrations = services.Where(d => d.ServiceType == typeof(IEventHandler<ProbeEvent>)).ToArray();
if (registrations.Length != 1 || registrations[0].Lifetime != ServiceLifetime.Transient)
{
    Console.Error.WriteLine("Generated handler registration must be exactly one transient descriptor.");
    return 1;
}

await using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }))
{
    await using (var scope = provider.CreateAsyncScope())
    {
        var handlers = scope.ServiceProvider.GetServices<IEventHandler<ProbeEvent>>().ToArray();
        if (handlers.Length != 1 || handlers[0] is not ProbeHandler)
            throw new InvalidOperationException("Generated handler registration must resolve exactly the expected handler.");
        var message = new ProbeEvent { Value = 42 };
        await handlers[0].HandleAsync(message);
        var stored = await storage.RetrieveAsync<ProbeEvent>(state.StoredId!);
        if (state.Calls != 1 || stored?.MessageId != message.MessageId || stored.Value != 42)
            throw new InvalidOperationException("Typed handler did not store the expected message.");
        if ((await storage.QueryAsync<ProbeEvent>(new MessageQuery { Collection = "aot-probe" })).Single().Value != 42)
            throw new InvalidOperationException("Typed in-memory query did not preserve the payload.");
        if (!await storage.UpdateAsync(state.StoredId!, message with { Value = 84 }) ||
            (await storage.RetrieveAsync<ProbeEvent>(state.StoredId!))?.Value != 84)
            throw new InvalidOperationException("In-memory update failed.");
        if (!await storage.DeleteAsync(state.StoredId!) || await storage.RetrieveAsync<ProbeEvent>(state.StoredId!) is not null)
            throw new InvalidOperationException("In-memory deletion failed.");
        var expiring = await storage.StoreAsync(message, new MessageStorageOptions { Ttl = TimeSpan.FromSeconds(1) });
        clock.Advance(TimeSpan.FromSeconds(1));
        if (await storage.RetrieveAsync<ProbeEvent>(expiring) is not null || await storage.CountAsync() != 0)
            throw new InvalidOperationException("Exact-boundary expiry failed.");
        if (state.Disposals != 0) throw new InvalidOperationException("Handler was disposed before its scope ended.");
    }
    if (state.Disposals != 1) throw new InvalidOperationException("Scoped generated handler was not asynchronously disposed once.");
}
if (state.Disposals != 1) throw new InvalidOperationException("Provider disposal disposed the handler twice.");

var expectedIdentity = args[0];
foreach (var assembly in new[] { typeof(InMemoryMessageStorage).Assembly, typeof(IEvent).Assembly })
{
    var identity = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    if (identity != expectedIdentity) throw new InvalidOperationException("Package source identity mismatch: " + identity);
    Console.WriteLine($"PACKAGE_ASSEMBLY {assembly.GetName().Name} {identity}");
}
Console.WriteLine($"NATIVE_AOT runtime={Environment.Version} framework={RuntimeInformation.FrameworkDescription} rid={RuntimeInformation.RuntimeIdentifier} dynamicCodeSupported={RuntimeFeature.IsDynamicCodeSupported} dynamicCodeCompiled={RuntimeFeature.IsDynamicCodeCompiled}");
Console.WriteLine($"PASS generatedHandlers=1 handlerCalls={state.Calls} asyncDisposals={state.Disposals} storage=store/query/update/delete/expiry package={expectedIdentity}");
Console.WriteLine("SCOPE: typed handler and in-memory storage on the recorded SDK/runtime/RID only; no EventBus dynamic routing, external services, or performance acceptance.");

Console.WriteLine("NATIVE_AOT_PASS");
return 0;
