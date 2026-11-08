using HeroMessaging.Abstractions;
using HeroMessaging.Utilities;
using Xunit;

namespace HeroMessaging.Tests.Unit.Utilities;

[Trait("Category", "Unit")]
public sealed class OutboxAddressValidatorTests
{
    public static TheoryData<OutboxOptions> InvalidOptions() =>
    [
        new OutboxOptions { Destination = "orders", Topic = "order-events" },
        new OutboxOptions { Destination = "orders", RoutingKey = "orders.created" },
        new OutboxOptions { RoutingKey = "orders.created" },
        new OutboxOptions { Topic = " " },
        new OutboxOptions { Destination = "" }
    ];

    public static TheoryData<OutboxOptions, bool> ValidOptions() => new()
    {
        { new OutboxOptions(), false },
        { new OutboxOptions { Destination = "orders" }, true },
        { new OutboxOptions { Topic = "order-events" }, true },
        { new OutboxOptions { Topic = "order-events", RoutingKey = "orders.created" }, true }
    };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Validate_InvalidAddress_ThrowsArgumentException(OutboxOptions options)
    {
        var error = Assert.Throws<ArgumentException>(() => OutboxAddressValidator.Validate(options));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [MemberData(nameof(ValidOptions))]
    public void Validate_ValidAddress_DoesNotThrowAndReportsWhetherExternal(OutboxOptions options, bool external)
    {
        OutboxAddressValidator.Validate(options);

        Assert.Equal(external, OutboxAddressValidator.IsExternal(options));
    }

    [Fact]
    public void ValidateAndIsExternal_NullOptions_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => OutboxAddressValidator.Validate(null!));
        Assert.Throws<ArgumentNullException>(() => OutboxAddressValidator.IsExternal(null!));
    }
}
