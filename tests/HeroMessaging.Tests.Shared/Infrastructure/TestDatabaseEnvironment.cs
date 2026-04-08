namespace HeroMessaging.Tests.Shared.Infrastructure;

/// <summary>
/// Constants and helpers for test database configuration.
/// Centralizes environment variable names, Docker images, and connection resolution.
/// </summary>
public static class TestDatabaseEnvironment
{
    // Environment variable names (following .NET configuration binding convention)
    public const string PostgreSqlConnectionStringEnvVar = "PostgreSql__ConnectionString";
    public const string SqlServerConnectionStringEnvVar = "SqlServer__ConnectionString";
    public const string RabbitMqConnectionStringEnvVar = "RabbitMQ__ConnectionString";

    // Docker images
    public const string PostgreSqlImage = "postgres:17-alpine";
    public const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";
    public const string RabbitMqImage = "rabbitmq:3.13-management-alpine";

    // Default test passwords
    public const string PostgreSqlPassword = "postgres";
    public const string SqlServerPassword = "YourStrong@Passw0rd";

    // Default test schema
    public const string DefaultTestSchema = "test";

    /// <summary>
    /// Gets connection string from environment variable, or null if not set.
    /// Used by test fixtures to support both CI (env var) and local dev (Testcontainers).
    /// </summary>
    public static string? GetConnectionStringFromEnvironment(string envVarName)
    {
        var value = Environment.GetEnvironmentVariable(envVarName);
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
