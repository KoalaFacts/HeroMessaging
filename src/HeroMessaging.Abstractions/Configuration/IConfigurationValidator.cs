namespace HeroMessaging.Abstractions.Configuration;

/// <summary>
/// Validates HeroMessaging configuration
/// </summary>
public interface IConfigurationValidator
{
    /// <summary>
    /// Validates the configuration and returns a validation report
    /// </summary>
    IValidationReport Validate();
}

/// <summary>
/// Validation report interface
/// </summary>
public interface IValidationReport
{
    /// <summary>
    /// Indicates if the configuration is valid
    /// </summary>
    bool IsValid { get; }

    /// <summary>
    /// Indicates if there are any warnings
    /// </summary>
    bool HasWarnings { get; }
}
