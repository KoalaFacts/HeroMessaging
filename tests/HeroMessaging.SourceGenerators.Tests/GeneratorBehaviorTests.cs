using HeroMessaging.SourceGenerators.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HeroMessaging.SourceGenerators.Tests;

[Trait("Category", "Unit")]
public class GeneratorBehaviorTests
{
    private static readonly MetadataReference[] References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Select(path => MetadataReference.CreateFromFile(path))];

    public class Builder
    {
        [Fact]
        public void GeneratesFluentMethodsForWritableProperties()
        {
            var generated = Generate(new MessageBuilderGenerator(), """
                namespace Sample;
                [GenerateBuilder]
                public class Order
                {
                    public string Id { get; set; } = "";
                    public int Quantity { get; init; }
                    public static int Shared { get; set; }
                }
                public sealed class GenerateBuilderAttribute : System.Attribute { }
                """);

            Assert.Contains("public sealed class OrderBuilder", generated);
            Assert.Contains("WithId(", generated);
            Assert.Contains("WithQuantity(", generated);
            Assert.DoesNotContain("WithShared(", generated);
        }
    }

    public class Validator
    {
        [Fact]
        public void GeneratesValidationMethodsForAttributedType()
        {
            var generated = Generate(new MessageValidatorGenerator(), """
                namespace Sample;
                [GenerateValidator]
                public class Order
                {
                    public string Id { get; set; } = "";
                }
                public sealed class GenerateValidatorAttribute : System.Attribute { }
                """);

            Assert.Contains("public sealed class OrderValidator", generated);
            Assert.Contains("ValidateAndThrow(", generated);
            Assert.Contains("GetValidationErrors(", generated);
        }
    }

    public class IdempotencyKey
    {
        [Fact]
        public void UsesOnlyExistingRequestedProperties()
        {
            var generated = Generate(new IdempotencyKeyGenerator(), """
                namespace Sample;
                [GenerateIdempotencyKey("OrderId", "Missing")]
                public class Order
                {
                    public string OrderId { get; set; } = "";
                }
                public sealed class GenerateIdempotencyKeyAttribute : System.Attribute
                {
                    public GenerateIdempotencyKeyAttribute(params string[] properties) { }
                }
                """);

            Assert.Contains("OrderIdempotencyKeyGenerator", generated);
            Assert.Contains("sb.Append(message.OrderId)", generated);
            Assert.DoesNotContain("message.Missing", generated);
        }
    }

    public class Saga
    {
        [Fact]
        public void GeneratesInitialStateAndHandlers()
        {
            var generated = Generate(new SagaDslGenerator(), """
                namespace Sample;
                [GenerateSaga]
                public partial class PurchaseSaga
                {
                    [SagaState("Submitted"), InitialState]
                    public class Submitted
                    {
                        [On<Placed>]
                        public void HandlePlaced() { }

                        [Compensate]
                        public void Undo() { }

                        [OnTimeout(30)]
                        public void OnTimeout() { }
                    }
                }
                public class Placed { }
                public sealed class GenerateSagaAttribute : System.Attribute { }
                public sealed class SagaStateAttribute(string name) : System.Attribute { }
                public sealed class InitialStateAttribute : System.Attribute { }
                public sealed class OnAttribute<T> : System.Attribute { }
                public sealed class CompensateAttribute : System.Attribute { }
                public sealed class OnTimeoutAttribute(int seconds) : System.Attribute { }
                """);

            Assert.Contains("PurchaseSaga", generated);
            Assert.Contains("Submitted", generated);
            Assert.Contains("HandlePlaced", generated);
            Assert.Contains("Undo", generated);
        }
    }

    public class TestDataBuilder
    {
        [Fact]
        public void GeneratesRandomizedBuilderForAnnotatedProperties()
        {
            var generated = Generate(new TestDataBuilderGenerator(), """
                namespace Sample;
                [GenerateTestDataBuilder]
                public class Customer
                {
                    [RandomString(Length = 12, Prefix = "user-")]
                    public string Name { get; set; } = "";

                    [RandomInt(Min = 10, Max = 20)]
                    public int Age { get; set; }

                    [RandomEmail(Domain = "example.test")]
                    public string Email { get; set; } = "";
                }
                """);

            Assert.Contains("class CustomerBuilder", generated);
            Assert.Contains("WithRandomData()", generated);
            Assert.Contains("WithName(", generated);
            Assert.Contains("CreateMany(int count)", generated);
        }
    }

    public class Contract
    {
        [Fact]
        public void GeneratesSchemaAndSampleTests()
        {
            var generated = Generate(new ContractTestGenerator(), """
                namespace Sample;
                [GenerateContractTests(Version = "v2")]
                public class Order
                {
                    [ContractRequired]
                    public string Id { get; set; } = "";

                    [ContractDeprecated]
                    public string Legacy { get; set; } = "";

                    [ContractSample("baseline")]
                    public static Order Baseline() => new();
                }
                """);

            Assert.Contains("OrderContractTests", generated);
            Assert.Contains("SchemaSnapshot", generated);
            Assert.Contains("baseline", generated);
        }
    }

    public class HandlerRegistration
    {
        [Fact]
        public void RegistersConcreteHandlersOnly()
        {
            var generated = Generate(new HandlerRegistrationGenerator(), """
                namespace HeroMessaging.Sample;
                public interface IMessageHandler { }
                public sealed class OrderHandler : IMessageHandler { }
                public abstract class BaseHandler : IMessageHandler { }
                """);

            Assert.Contains("GeneratedHandlerRegistrationExtensions", generated);
            Assert.Contains("OrderHandler", generated);
            Assert.DoesNotContain("BaseHandler", generated);
        }
    }

    public class Logging
    {
        [Fact]
        public void OmitsSensitiveParametersFromEntryLog()
        {
            var generated = Generate(new MethodLoggingGenerator(), """
                namespace Sample;
                public partial class Worker
                {
                    [LogMethod]
                    public partial void Send(string queue, [NoLog] string secret);
                }
                """);

            Assert.Contains("Entering Send", generated);
            Assert.Contains("queue={queue}", generated);
            Assert.DoesNotContain("secret={secret}", generated);
        }
    }

    public class Metrics
    {
        [Fact]
        public void GeneratesMeterAndTaggedMethod()
        {
            var generated = Generate(new MetricsInstrumentationGenerator(), """
                namespace Sample;
                [GenerateMetrics]
                public partial class Worker
                {
                    [InstrumentMethod]
                    public partial void Send([MetricTag] string queue);
                }
                """);

            Assert.Contains("CreateCounter<long>", generated);
            Assert.Contains("CreateHistogram<double>", generated);
            Assert.Contains("queue", generated);
        }
    }

    private static string Generate(IIncrementalGenerator generator, string source)
    {
        var compilation = CSharpCompilation.Create(
            "GeneratorTest",
            [CSharpSyntaxTree.ParseText(source)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(generator.AsSourceGenerator()).RunGenerators(compilation);
        var result = driver.GetRunResult();

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return string.Join("\n", result.Results[0].GeneratedSources.Select(item => item.SourceText.ToString()));
    }
}
