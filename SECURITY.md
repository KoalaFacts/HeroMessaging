# Security Policy

## Supported Versions

We actively support the following versions of HeroMessaging with security updates:

| Version | Supported          | .NET Target Frameworks |
| ------- | ------------------ | ---------------------- |
| 1.x     | :white_check_mark: | netstandard2.0, net6.0, net7.0, net8.0, net9.0 |
| < 1.0   | :x:                | Pre-release versions not supported |

## Reporting a Vulnerability

We take security vulnerabilities seriously. If you discover a security issue in HeroMessaging, please report it responsibly.

### How to Report

**DO NOT** create a public GitHub issue for security vulnerabilities.

Instead, please report security issues by:

1. **Email**: Send details to the project maintainers (contact information available in the GitHub repository settings)
2. **GitHub Security Advisory**: Use [GitHub's private vulnerability reporting](https://github.com/KoalaFacts/HeroMessaging/security/advisories/new) (preferred)

### What to Include

When reporting a vulnerability, please include:

- **Description**: Clear description of the vulnerability
- **Impact**: What could an attacker accomplish?
- **Reproduction**: Step-by-step instructions to reproduce the issue
- **Environment**:
  - HeroMessaging version
  - .NET version
  - Operating system
  - Plugin packages in use
- **Proof of Concept**: Code or configuration demonstrating the issue (if applicable)
- **Suggested Fix**: If you have ideas for remediation (optional)

### Response Timeline

We aim to respond to security reports according to the following timeline:

- **Initial Response**: Within 48 hours
- **Severity Assessment**: Within 5 business days
- **Fix Development**: Varies by severity
  - Critical: 7-14 days
  - High: 30 days
  - Medium: 60 days
  - Low: Next minor release
- **Disclosure**: Coordinated with reporter after fix is available

### Severity Classification

We use the following severity levels:

- **Critical**: Remote code execution, SQL injection, authentication bypass
- **High**: Privilege escalation, data exposure, denial of service
- **Medium**: Information disclosure, limited data exposure
- **Low**: Minor issues with limited impact

## Security Best Practices

When using HeroMessaging, follow these security best practices:

### 1. Connection Strings

**Never** commit connection strings or credentials to source control:

```csharp
// ❌ BAD: Hardcoded credentials
builder.UseSqlServerInbox("Server=localhost;User=sa;Password=secret;...");

// ✅ GOOD: Use configuration
var connectionString = configuration.GetConnectionString("HeroMessaging");
builder.UseSqlServerInbox(connectionString);
```

Store credentials in:
- Azure Key Vault
- AWS Secrets Manager
- Environment variables
- User secrets (development only)

### 2. Message Validation

Always validate message contents before processing:

```csharp
public class OrderHandler : IMessageHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken)
    {
        // ✅ Validate inputs
        if (message.Amount <= 0)
            throw new ValidationException("Order amount must be positive");

        if (message.OrderId == Guid.Empty)
            throw new ValidationException("Order ID is required");

        // Process validated message...
    }
}
```

### 3. SQL Injection Prevention

HeroMessaging storage providers use parameterized queries, but when extending:

```csharp
// ✅ GOOD: Parameterized queries (already done in storage plugins)
cmd.CommandText = "SELECT * FROM Messages WHERE Id = @Id";
cmd.Parameters.AddWithValue("@Id", messageId);

// ❌ BAD: String concatenation (never do this)
cmd.CommandText = $"SELECT * FROM Messages WHERE Id = '{messageId}'";
```

### 4. Serialization Security

When using custom serialization:

```csharp
// ✅ Configure safe JSON options
var options = new JsonSerializerOptions
{
    // Prevent type confusion attacks
    TypeInfoResolver = JsonTypeInfoResolver.Combine(
        SerializerContext.Default),

    // Limit nesting depth
    MaxDepth = 64
};
```

### 5. Transport Security

When using RabbitMQ or other transports:

```csharp
// ✅ Use TLS for external transports
builder.UseRabbitMq(config =>
{
    config.HostName = "rabbitmq.example.com";
    config.UseSsl(ssl =>
    {
        ssl.Enabled = true;
        ssl.ServerName = "rabbitmq.example.com";
        ssl.Version = SslProtocols.Tls12 | SslProtocols.Tls13;
    });
});
```

### 6. Saga Timeout Handling

Configure appropriate timeouts to prevent resource exhaustion:

```csharp
builder.AddSagaTimeoutHandler<OrderSaga>(options =>
{
    options.CheckInterval = TimeSpan.FromMinutes(5);
    options.DefaultTimeout = TimeSpan.FromHours(24);
});
```

### 7. Dependency Management

Regularly update dependencies to get security patches:

```bash
# Check for outdated packages
dotnet list package --outdated

# Update to latest secure versions
dotnet add package HeroMessaging
```

### 8. Least Privilege

Run services with minimal required permissions:

```sql
-- ❌ BAD: db_owner
GRANT db_owner TO HeroMessagingUser;

-- ✅ GOOD: Specific permissions
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.InboxMessages TO HeroMessagingUser;
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.OutboxMessages TO HeroMessagingUser;
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Sagas TO HeroMessagingUser;
GRANT EXECUTE ON dbo.InitializeSchema TO HeroMessagingUser;
```

## Known Security Considerations

### In-Memory Storage

The in-memory implementations are designed for development and testing:

- **Not durable**: Messages lost on process restart
- **Not distributed**: Cannot be shared across processes
- **No encryption**: Messages stored in plain text in memory

For production, use persistent storage (SQL Server, PostgreSQL) with:
- Encryption at rest (TDE, encrypted volumes)
- Encryption in transit (TLS connections)
- Access control (database authentication)

### Message Compensation

Compensation actions execute in reverse order (LIFO) and may contain sensitive operations:

- **Audit compensation actions**: Log all compensating transactions
- **Idempotency**: Ensure compensation actions are idempotent
- **Validation**: Validate compensation can be safely executed

### Timeout Handling

Background timeout handlers have access to saga state:

- **Secure saga data**: Don't store sensitive data in saga state without encryption
- **Audit timeouts**: Log timeout events for security monitoring
- **Validate timeout actions**: Ensure timeout handlers can't be exploited

## Security Updates

Security updates are published as:

1. **GitHub Security Advisory**: For coordinated disclosure
2. **NuGet Package Update**: With version bump indicating severity
   - Major: Breaking security fix
   - Minor: Non-breaking security fix
   - Patch: Security backport to previous minor version
3. **Release Notes**: Detailed in [CHANGELOG.md](CHANGELOG.md)

Subscribe to:
- [GitHub Security Advisories](https://github.com/KoalaFacts/HeroMessaging/security/advisories)
- [GitHub Releases](https://github.com/KoalaFacts/HeroMessaging/releases)
- [NuGet package updates](https://www.nuget.org/packages/HeroMessaging)

## Disclosure Policy

We follow **coordinated disclosure**:

1. **Reporter** notifies maintainers privately
2. **Maintainers** confirm and assess severity
3. **Fix** is developed and tested privately
4. **Reporter** is given opportunity to review fix
5. **Release** is published with security advisory
6. **Public disclosure** occurs after fix is available (typically 90 days from initial report or when fix is released, whichever comes first)

We credit reporters in security advisories unless they prefer to remain anonymous.

## Security Hall of Fame

We recognize security researchers who responsibly disclose vulnerabilities:

<!-- This section will be populated as security reports are received and resolved -->

## Compliance

HeroMessaging is designed to support compliance with:

- **GDPR**: No personal data collected by framework (application responsibility)
- **SOC 2**: Audit logging, secure defaults
- **HIPAA**: Encryption support for data in transit and at rest (when configured)

Note: Compliance is ultimately the responsibility of applications using HeroMessaging. This framework provides the necessary tools and patterns to support compliance.

## Questions?

For security-related questions that are not vulnerabilities:
- [Open a discussion](https://github.com/KoalaFacts/HeroMessaging/discussions)
- Review [CONTRIBUTING.md](CONTRIBUTING.md) for general contribution guidelines

Thank you for helping keep HeroMessaging secure!
