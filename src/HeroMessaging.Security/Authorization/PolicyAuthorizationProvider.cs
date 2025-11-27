using System;
using System.Security.Claims;
using HeroMessaging.Abstractions.Security;

namespace HeroMessaging.Security.Authorization;

/// <summary>
/// Provides policy-based authorization for message operations
/// </summary>
public sealed class PolicyAuthorizationProvider : IAuthorizationProvider
{
    private readonly Dictionary<string, AuthorizationPolicy> _policies;
    private readonly bool _requireAuthenticatedUser;

    /// <summary>
    /// Creates a new policy-based authorization provider
    /// </summary>
    /// <param name="requireAuthenticatedUser">Whether to require authenticated users for all operations</param>
    public PolicyAuthorizationProvider(bool requireAuthenticatedUser = true)
    {
        _requireAuthenticatedUser = requireAuthenticatedUser;
        _policies = new Dictionary<string, AuthorizationPolicy>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Registers an authorization policy
    /// </summary>
    public void AddPolicy(string name, AuthorizationPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Policy name cannot be empty", nameof(name));

        if (policy == null)
            throw new ArgumentNullException(nameof(policy));

        _policies[name] = policy;
    }

    /// <summary>
    /// Registers a role-based policy
    /// </summary>
    public void RequireRole(string messageType, string operation, params ReadOnlySpan<string> roles)
    {
        var policyName = GetPolicyName(messageType, operation);
        var policy = new AuthorizationPolicy(policyName)
            .RequireAuthenticatedUser()
            .RequireRole(roles);

        _policies[policyName] = policy;
    }

    /// <summary>
    /// Registers a claim-based policy
    /// </summary>
    public void RequireClaim(string messageType, string operation, string claimType, params ReadOnlySpan<string> allowedValues)
    {
        var policyName = GetPolicyName(messageType, operation);
        var policy = new AuthorizationPolicy(policyName)
            .RequireAuthenticatedUser()
            .RequireClaim(claimType, allowedValues);

        _policies[policyName] = policy;
    }

    /// <summary>
    /// Allows anonymous access for a specific message type and operation
    /// </summary>
    public void AllowAnonymous(string messageType, string operation)
    {
        var policyName = GetPolicyName(messageType, operation);
        var policy = new AuthorizationPolicy(policyName)
            .AllowAnonymous();

        _policies[policyName] = policy;
    }

    public Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal principal,
        string messageType,
        string operation,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageType))
            throw new ArgumentException("Message type cannot be empty", nameof(messageType));

        if (string.IsNullOrWhiteSpace(operation))
            throw new ArgumentException("Operation cannot be empty", nameof(operation));

        // Check for specific policy
        var policyName = GetPolicyName(messageType, operation);
        if (_policies.TryGetValue(policyName, out var policy))
        {
            return Task.FromResult(policy.Evaluate(principal));
        }

        // Check for wildcard message type policy
        var wildcardPolicyName = GetPolicyName("*", operation);
        if (_policies.TryGetValue(wildcardPolicyName, out var wildcardPolicy))
        {
            return Task.FromResult(wildcardPolicy.Evaluate(principal));
        }

        // Default behavior: require authenticated user if configured
        if (_requireAuthenticatedUser)
        {
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return Task.FromResult(AuthorizationResult.Failure(
                    "Authentication required for message operation",
                    "Unauthenticated"));
            }
        }

        return Task.FromResult(AuthorizationResult.Success());
    }

    public Task<bool> HasPermissionAsync(
        ClaimsPrincipal principal,
        string permission,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(permission))
            throw new ArgumentException("Permission cannot be empty", nameof(permission));

        if (principal == null)
            return Task.FromResult(false);

        // Check for permission claim
        var hasPermission = principal.HasClaim(c =>
            c.Type == "permission" &&
            c.Value.Equals(permission, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(hasPermission);
    }

    private static string GetPolicyName(string messageType, string operation)
    {
        return $"{messageType}:{operation}";
    }
}

/// <summary>
/// Represents an authorization policy with requirements
/// </summary>
public sealed class AuthorizationPolicy
{
    private readonly string _name;
    private bool _requireAuthentication = true;
    private bool _allowAnonymous;
    private readonly HashSet<string> _requiredRoles;
    private readonly Dictionary<string, HashSet<string>> _requiredClaims;
    private readonly List<Func<ClaimsPrincipal, bool>> _customRequirements;

    public string Name => _name;

    public AuthorizationPolicy(string name)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _requiredRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _requiredClaims = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        _customRequirements = new List<Func<ClaimsPrincipal, bool>>();
    }

    /// <summary>
    /// Requires an authenticated user
    /// </summary>
    public AuthorizationPolicy RequireAuthenticatedUser()
    {
        _requireAuthentication = true;
        _allowAnonymous = false;
        return this;
    }

    /// <summary>
    /// Allows anonymous access
    /// </summary>
    public AuthorizationPolicy AllowAnonymous()
    {
        _allowAnonymous = true;
        _requireAuthentication = false;
        return this;
    }

    /// <summary>
    /// Requires the user to be in one of the specified roles
    /// </summary>
    public AuthorizationPolicy RequireRole(params ReadOnlySpan<string> roles)
    {
        if (roles.Length == 0)
            throw new ArgumentException("At least one role is required", nameof(roles));

        foreach (var role in roles)
        {
            if (!string.IsNullOrWhiteSpace(role))
                _requiredRoles.Add(role);
        }

        return this;
    }

    /// <summary>
    /// Requires the user to have a specific claim
    /// </summary>
    public AuthorizationPolicy RequireClaim(string claimType, params ReadOnlySpan<string> allowedValues)
    {
        if (string.IsNullOrWhiteSpace(claimType))
            throw new ArgumentException("Claim type cannot be empty", nameof(claimType));

        if (!_requiredClaims.ContainsKey(claimType))
        {
            _requiredClaims[claimType] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        if (allowedValues.Length > 0)
        {
            foreach (var value in allowedValues)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    _requiredClaims[claimType].Add(value);
            }
        }

        return this;
    }

    /// <summary>
    /// Adds a custom authorization requirement
    /// </summary>
    public AuthorizationPolicy RequireAssertion(Func<ClaimsPrincipal, bool> requirement)
    {
        if (requirement == null)
            throw new ArgumentNullException(nameof(requirement));

        _customRequirements.Add(requirement);
        return this;
    }

    /// <summary>
    /// Evaluates the policy against a principal
    /// </summary>
    public AuthorizationResult Evaluate(ClaimsPrincipal principal)
    {
        // Allow anonymous if configured
        if (_allowAnonymous)
            return AuthorizationResult.Success();

        // Check authentication requirement (any identity being authenticated is sufficient)
        if (_requireAuthentication)
        {
            var isAnyAuthenticated = principal?.Identities?.Any(i => i.IsAuthenticated) == true;
            if (!isAnyAuthenticated)
            {
                return AuthorizationResult.Failure(
                    $"Policy '{_name}' requires an authenticated user",
                    "Unauthenticated");
            }
        }

        // Check role requirements (case-insensitive)
        if (_requiredRoles.Count > 0)
        {
            var userRoles = principal.Claims
                .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
                .Select(c => c.Value);
            var hasRequiredRole = _requiredRoles.Any(required =>
                userRoles.Any(userRole => string.Equals(userRole, required, StringComparison.OrdinalIgnoreCase)));
            if (!hasRequiredRole)
            {
                return AuthorizationResult.InsufficientPermissions(
                    $"Required roles: {string.Join(", ", _requiredRoles)}");
            }
        }

        // Check claim requirements
        foreach (var claimRequirement in _requiredClaims)
        {
            var claimType = claimRequirement.Key;
            var allowedValues = claimRequirement.Value;

            if (allowedValues.Count == 0)
            {
                // Just require the claim exists
                if (!principal.HasClaim(c => c.Type == claimType))
                {
                    return AuthorizationResult.InsufficientPermissions($"Required claim: {claimType}");
                }
            }
            else
            {
                // Require specific claim values
                var hasValidClaim = principal.Claims.Any(c =>
                    c.Type == claimType &&
                    allowedValues.Contains(c.Value));

                if (!hasValidClaim)
                {
                    return AuthorizationResult.InsufficientPermissions(
                        $"Required claim: {claimType} with values: {string.Join(", ", allowedValues)}");
                }
            }
        }

        // Check custom requirements
        foreach (var requirement in _customRequirements)
        {
            try
            {
                if (!requirement(principal))
                {
                    return AuthorizationResult.Failure(
                        $"Custom authorization requirement failed for policy '{_name}'",
                        "CustomRequirementFailed");
                }
            }
            catch (Exception ex)
            {
                return AuthorizationResult.Failure(
                    $"Custom authorization requirement threw exception: {ex.Message}",
                    "CustomRequirementException");
            }
        }

        return AuthorizationResult.Success();
    }
}
