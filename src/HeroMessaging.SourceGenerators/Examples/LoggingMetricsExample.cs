// Copyright (c) HeroMessaging Contributors. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeroMessaging.SourceGenerators;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.SourceGenerators.Examples;

/// <summary>
/// Example demonstrating logging and metrics source generators.
/// Shows how to eliminate boilerplate while maintaining observability.
/// </summary>
[GenerateMetrics(MeterName = "HeroMessaging.Examples.OrderService")]
public partial class OrderService
{
    private readonly ILogger<OrderService> _logger;
    private readonly IOrderRepository _repository;
    private readonly IPaymentGateway _paymentGateway;

    public OrderService(
        ILogger<OrderService> logger,
        IOrderRepository repository,
        IPaymentGateway _paymentGateway)
    {
        _logger = logger;
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    // BEFORE: Manual logging and metrics (verbose!)
    /*
    public async Task<Order> CreateOrderManualAsync(string orderId, decimal amount)
    {
        using var activity = Activity.Current?.Source.StartActivity("CreateOrder");
        var stopwatch = Stopwatch.StartNew();

        var tags = new TagList
        {
            { "method", "CreateOrder" },
            { "class", "OrderService" }
        };

        _methodCallsCounter.Add(1, tags);
        _logger.LogInformation("Creating order {OrderId} with amount {Amount}", orderId, amount);

        try
        {
            var order = new Order { OrderId = orderId, Amount = amount };
            await _repository.SaveAsync(order);

            stopwatch.Stop();
            tags.Add("status", "success");
            _methodDurationHistogram.Record(stopwatch.ElapsedMilliseconds, tags);
            _logger.LogInformation("Created order {OrderId} in {DurationMs}ms",
                orderId, stopwatch.ElapsedMilliseconds);

            return order;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            tags.Add("status", "error");
            tags.Add("error_type", ex.GetType().Name);

            _methodErrorsCounter.Add(1, tags);
            _methodDurationHistogram.Record(stopwatch.ElapsedMilliseconds, tags);
            _logger.LogError(ex, "Failed creating order {OrderId} after {DurationMs}ms",
                orderId, stopwatch.ElapsedMilliseconds);

            throw;
        }
    }
    */

    // AFTER: Using source generators (concise!)
    /// <summary>
    /// Creates an order with automatic logging and metrics.
    /// Generated code handles: entry log, exit log, duration, errors, activity, and metrics.
    /// </summary>
    [LogMethod(LogLevel.Information)]
    [InstrumentMethod(InstrumentationType.Counter | InstrumentationType.Histogram)]
    public partial Task<Order> CreateOrderAsync(string orderId, decimal amount);

    // Implementation goes in Core method (generated pattern)
    private async partial Task<Order> CreateOrderCore(string orderId, decimal amount)
    {
        var order = new Order { OrderId = orderId, Amount = amount };
        await _repository.SaveAsync(order);
        return order;
    }

    /// <summary>
    /// Processes payment with sensitive data protection.
    /// Credit card number is excluded from logs using [NoLog].
    /// </summary>
    [LogMethod(LogLevel.Information)]
    [InstrumentMethod(InstrumentationType.Counter | InstrumentationType.Histogram,
        MetricName = "payments.processed")]
    public partial Task<PaymentResult> ProcessPaymentAsync(
        string orderId,
        decimal amount,
        [NoLog] string creditCardNumber, // Won't appear in logs
        [MetricTag] string paymentMethod); // Will appear as metric tag

    private async partial Task<PaymentResult> ProcessPaymentCore(
        string orderId,
        decimal amount,
        string creditCardNumber,
        string paymentMethod)
    {
        // Actual payment processing logic
        var result = await _paymentGateway.ChargeAsync(creditCardNumber, amount);
        return result;
    }

    /// <summary>
    /// Batch processing with custom metric tags for observability.
    /// Customer ID is tagged for customer-level metrics analysis.
    /// </summary>
    [LogMethod(LogLevel.Information, LogParameters = false)] // Don't log large batch
    [InstrumentMethod(InstrumentationType.Counter | InstrumentationType.Histogram,
        MetricName = "orders.batch_processed",
        TagParameters = true)]
    public partial Task<BatchResult> ProcessBatchAsync(
        [MetricTag] string customerId,
        List<Order> orders);

    private async partial Task<BatchResult> ProcessBatchCore(
        string customerId,
        List<Order> orders)
    {
        var processed = 0;
        var failed = 0;

        foreach (var order in orders)
        {
            try
            {
                await _repository.SaveAsync(order);
                processed++;
            }
            catch
            {
                failed++;
            }
        }

        return new BatchResult { Processed = processed, Failed = failed };
    }

    /// <summary>
    /// Cancellation with custom log messages.
    /// Shows how to override default entry/exit messages.
    /// </summary>
    [LogMethod(LogLevel.Warning,
        EntryMessage = "Cancelling order {orderId} due to customer request",
        ExitMessage = "Order {orderId} cancelled successfully in {DurationMs}ms")]
    [InstrumentMethod(InstrumentationType.Counter,
        MetricName = "orders.cancelled")]
    public partial Task CancelOrderAsync(string orderId);

    private async partial Task CancelOrderCore(string orderId)
    {
        var order = await _repository.GetByIdAsync(orderId);
        order.Status = OrderStatus.Cancelled;
        await _repository.UpdateAsync(order);
    }

    /// <summary>
    /// Query operation with trace-level logging (not info).
    /// Demonstrates different log levels for different operations.
    /// </summary>
    [LogMethod(LogLevel.Trace)] // Use Trace for queries to reduce noise
    [InstrumentMethod(InstrumentationType.Histogram,
        MetricName = "orders.query_duration")]
    public partial Task<Order?> GetOrderAsync(string orderId);

    private async partial Task<Order?> GetOrderCore(string orderId)
    {
        return await _repository.GetByIdAsync(orderId);
    }

    /// <summary>
    /// High-frequency operation with minimal logging.
    /// Metrics only, no distributed tracing to reduce overhead.
    /// </summary>
    [LogMethod(LogLevel.Trace, CreateActivity = false, LogDuration = false)]
    [InstrumentMethod(InstrumentationType.Counter)]
    public partial Task RecordViewAsync(string orderId);

    private partial Task RecordViewCore(string orderId)
    {
        // Just increment a counter - very fast
        return Task.CompletedTask;
    }
}

// Supporting types
public class Order
{
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Created;
}

public enum OrderStatus
{
    Created,
    Paid,
    Shipped,
    Delivered,
    Cancelled
}

public class PaymentResult
{
    public bool Success { get; set; }
    public string TransactionId { get; set; } = string.Empty;
}

public class BatchResult
{
    public int Processed { get; set; }
    public int Failed { get; set; }
}

public interface IOrderRepository
{
    Task SaveAsync(Order order);
    Task<Order?> GetByIdAsync(string orderId);
    Task UpdateAsync(Order order);
}

public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(string creditCardNumber, decimal amount);
}

/*
 * GENERATED OUTPUT (example for CreateOrderAsync):
 *
 * public partial class OrderService
 * {
 *     // From MetricsInstrumentationGenerator
 *     private static readonly Meter _meter = new Meter("HeroMessaging.Examples.OrderService", "1.0.0");
 *     private static readonly Counter<long> _methodCallsCounter =
 *         _meter.CreateCounter<long>("method.calls", "count", "Total method calls");
 *     private static readonly Counter<long> _methodErrorsCounter =
 *         _meter.CreateCounter<long>("method.errors", "count", "Total method errors");
 *     private static readonly Histogram<double> _methodDurationHistogram =
 *         _meter.CreateHistogram<double>("method.duration", "ms", "Method execution duration");
 *
 *     // From MethodLoggingGenerator + MetricsInstrumentationGenerator
 *     public async partial Task<Order> CreateOrderAsync(string orderId, decimal amount)
 *     {
 *         // Logging instrumentation
 *         using var activity = Activity.Current?.Source.StartActivity("OrderService.CreateOrderAsync");
 *         var stopwatch = Stopwatch.StartNew();
 *
 *         // Metrics instrumentation
 *         var tags = new TagList
 *         {
 *             { "method", "CreateOrderAsync" },
 *             { "class", "OrderService" }
 *         };
 *         _methodCallsCounter.Add(1, tags);
 *
 *         _logger.LogInformation("Entering CreateOrderAsync with orderId={OrderId}, amount={Amount}",
 *             orderId, amount);
 *
 *         try
 *         {
 *             var result = await CreateOrderCore(orderId, amount);
 *
 *             stopwatch.Stop();
 *             tags.Add("status", "success");
 *             _methodDurationHistogram.Record(stopwatch.ElapsedMilliseconds, tags);
 *             _logger.LogInformation("Completed CreateOrderAsync in {DurationMs}ms",
 *                 stopwatch.ElapsedMilliseconds);
 *
 *             return result;
 *         }
 *         catch (Exception ex)
 *         {
 *             stopwatch.Stop();
 *             tags.Add("status", "error");
 *             tags.Add("error_type", ex.GetType().Name);
 *
 *             _methodErrorsCounter.Add(1, tags);
 *             _methodDurationHistogram.Record(stopwatch.ElapsedMilliseconds, tags);
 *             _logger.LogError(ex, "Failed CreateOrderAsync after {DurationMs}ms",
 *                 stopwatch.ElapsedMilliseconds);
 *
 *             throw;
 *         }
 *     }
 * }
 *
 * BENEFITS:
 * - 90% less boilerplate code
 * - Consistent logging/metrics across all methods
 * - Automatic distributed tracing spans
 * - No manual stopwatch/activity management
 * - Protected sensitive data with [NoLog]
 * - Controlled metric cardinality with [MetricTag]
 * - Performance: Zero runtime overhead, all code generated at compile-time
 * - Maintainability: Change logging/metrics patterns globally by updating generators
 */
