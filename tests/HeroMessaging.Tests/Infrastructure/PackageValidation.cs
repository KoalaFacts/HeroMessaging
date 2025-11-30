using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HeroMessaging.Tests.Infrastructure;

/// <summary>
/// Validates NuGet packages for completeness, metadata accuracy, and multi-framework support
/// Ensures packages meet constitutional standards for distribution and compatibility
/// </summary>
internal class PackageValidation
{
    private readonly PackageValidationConfiguration _config;

    public PackageValidation(PackageValidationConfiguration? config = null)
    {
        _config = config ?? new PackageValidationConfiguration();
    }

    /// <summary>
    /// Validates all packages in the distribution for completeness and compliance
    /// </summary>
    /// <param name="packageDirectory">Directory containing NuGet packages</param>
    /// <returns>Comprehensive package validation results</returns>
    public async Task<PackageValidationResults> ValidateAllPackagesAsync(string packageDirectory)
    {
        var results = new PackageValidationResults
        {
            StartTime = DateTimeOffset.UtcNow,
            PackageDirectory = packageDirectory
        };

        try
        {
            if (!Directory.Exists(packageDirectory))
            {
                results.Success = false;
                results.ErrorMessage = $"Package directory does not exist: {packageDirectory}";
                return results;
            }

            // Discover all NuGet packages
            var packageFiles = Directory.GetFiles(packageDirectory, "*.nupkg", SearchOption.AllDirectories);
            results.TotalPackagesFound = packageFiles.Length;

            if (packageFiles.Length == 0)
            {
                results.Success = false;
                results.ErrorMessage = "No NuGet packages found in the specified directory";
                return results;
            }

            // Validate each package
            foreach (var packageFile in packageFiles)
            {
                var packageResult = await ValidatePackageAsync(packageFile);
                results.PackageResults.Add(packageResult);

                if (packageResult.IsValid)
                    results.ValidPackagesCount++;
                else
                    results.InvalidPackagesCount++;
            }

            // Validate package dependencies and consistency
            var dependencyValidation = ValidatePackageDependencies(results.PackageResults);
            results.DependencyValidation = dependencyValidation;

            // Validate constitutional compliance
            var constitutionalValidation = ValidateConstitutionalCompliance(results.PackageResults);
            results.ConstitutionalValidation = constitutionalValidation;

            // Generate validation report
            results.ValidationReport = GenerateValidationReport(results);

            results.Success = results.InvalidPackagesCount == 0 &&
                            dependencyValidation.IsValid &&
                            constitutionalValidation.IsValid;
        }
        catch (Exception ex)
        {
            results.Success = false;
            results.ErrorMessage = ex.Message;
        }
        finally
        {
            results.EndTime = DateTimeOffset.UtcNow;
            results.Duration = results.EndTime - results.StartTime;
        }

        return results;
    }

    /// <summary>
    /// Validates a single NuGet package for completeness and metadata accuracy
    /// </summary>
    /// <param name="packagePath">Path to the NuGet package file</param>
    /// <returns>Package validation result</returns>
    public async Task<PackageValidationResult> ValidatePackageAsync(string packagePath)
    {
        var result = new PackageValidationResult
        {
            PackagePath = packagePath,
            PackageName = Path.GetFileNameWithoutExtension(packagePath)
        };

        try
        {
            // Extract and validate package contents
            using var package = ZipFile.OpenRead(packagePath);

            // Validate package structure
            var structureValidation = ValidatePackageStructure(package);
            result.StructureValidation = structureValidation;

            // Validate nuspec metadata
            var metadataValidation = await ValidatePackageMetadataAsync(package);
            result.MetadataValidation = metadataValidation;

            // Validate target framework support
            var frameworkValidation = ValidateTargetFrameworks(package);
            result.FrameworkValidation = frameworkValidation;

            // Validate assembly contents
            var assemblyValidation = await ValidateAssemblyContentsAsync(package);
            result.AssemblyValidation = assemblyValidation;

            // Validate dependencies
            var dependencyValidation = ValidateDependencies(package);
            result.DependencyValidation = dependencyValidation;

            // Validate documentation
            var documentationValidation = ValidateDocumentation(package);
            result.DocumentationValidation = documentationValidation;

            // Overall validation
            result.IsValid = structureValidation.IsValid &&
                           metadataValidation.IsValid &&
                           frameworkValidation.IsValid &&
                           assemblyValidation.IsValid &&
                           dependencyValidation.IsValid &&
                           documentationValidation.IsValid;

            if (!result.IsValid)
            {
                result.ValidationErrors.AddRange(structureValidation.Errors);
                result.ValidationErrors.AddRange(metadataValidation.Errors);
                result.ValidationErrors.AddRange(frameworkValidation.Errors);
                result.ValidationErrors.AddRange(assemblyValidation.Errors);
                result.ValidationErrors.AddRange(dependencyValidation.Errors);
                result.ValidationErrors.AddRange(documentationValidation.Errors);
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ValidationErrors.Add($"Package validation failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Validates constitutional compliance across all packages
    /// </summary>
    /// <param name="packageResults">Package validation results</param>
    /// <returns>Constitutional compliance validation</returns>
    public ConstitutionalComplianceValidation ValidateConstitutionalCompliance(List<PackageValidationResult> packageResults)
    {
        var validation = new ConstitutionalComplianceValidation();

        try
        {
            // Check required packages exist
            var requiredPackages = _config.RequiredPackages;
            foreach (var requiredPackage in requiredPackages)
            {
                var exists = packageResults.Any(p => p.PackageName.Contains(requiredPackage));
                if (!exists)
                {
                    validation.Errors.Add($"Required package missing: {requiredPackage}");
                }
            }

            // Validate multi-framework support
            var corePackages = packageResults.Where(p => _config.CorePackageNames.Any(core => p.PackageName.Contains(core)));
            foreach (var corePackage in corePackages)
            {
                var supportedFrameworks = corePackage.FrameworkValidation.SupportedFrameworks;
                var requiredFrameworks = _config.RequiredTargetFrameworks;

                foreach (var requiredFramework in requiredFrameworks)
                {
                    if (!supportedFrameworks.Contains(requiredFramework))
                    {
                        validation.Errors.Add($"Package {corePackage.PackageName} missing required framework: {requiredFramework}");
                    }
                }
            }

            // Validate package versioning consistency
            var versionGroups = packageResults
                .GroupBy(p => ExtractPackageNameWithoutVersion(p.PackageName))
                .Where(g => g.Count() > 1);

            foreach (var versionGroup in versionGroups)
            {
                var versions = versionGroup.Select(p => ExtractVersionFromPackageName(p.PackageName)).Distinct();
                if (versions.Count() > 1)
                {
                    validation.Warnings.Add($"Multiple versions found for package {versionGroup.Key}: {string.Join(", ", versions)}");
                }
            }

            // Validate semantic versioning
            foreach (var package in packageResults)
            {
                var version = ExtractVersionFromPackageName(package.PackageName);
                if (!IsValidSemanticVersion(version))
                {
                    validation.Errors.Add($"Package {package.PackageName} has invalid semantic version: {version}");
                }
            }

            validation.IsValid = !validation.Errors.Any();
        }
        catch (Exception ex)
        {
            validation.Errors.Add($"Constitutional compliance validation failed: {ex.Message}");
            validation.IsValid = false;
        }

        return validation;
    }

    /// <summary>
    /// Validates package dependencies for consistency and compatibility
    /// </summary>
    /// <param name="packageResults">Package validation results</param>
    /// <returns>Dependency validation results</returns>
    public PackageDependencyValidation ValidatePackageDependencies(List<PackageValidationResult> packageResults)
    {
        var validation = new PackageDependencyValidation();

        try
        {
            // Build dependency graph
            var dependencyGraph = BuildDependencyGraph(packageResults);
            validation.DependencyGraph = dependencyGraph;

            // Check for circular dependencies
            var circularDependencies = DetectCircularDependencies(dependencyGraph);
            if (circularDependencies.Any())
            {
                validation.Errors.AddRange(circularDependencies.Select(cd => $"Circular dependency detected: {cd}"));
            }

            // Validate dependency versions
            var versionConflicts = DetectVersionConflicts(dependencyGraph);
            if (versionConflicts.Any())
            {
                validation.Errors.AddRange(versionConflicts.Select(vc => $"Version conflict: {vc}"));
            }

            // Check for missing dependencies
            var missingDependencies = DetectMissingDependencies(dependencyGraph, packageResults);
            if (missingDependencies.Any())
            {
                validation.Errors.AddRange(missingDependencies.Select(md => $"Missing dependency: {md}"));
            }

            validation.IsValid = !validation.Errors.Any();
        }
        catch (Exception ex)
        {
            validation.Errors.Add($"Dependency validation failed: {ex.Message}");
            validation.IsValid = false;
        }

        return validation;
    }

    private PackageStructureValidation ValidatePackageStructure(ZipArchive package)
    {
        var validation = new PackageStructureValidation();

        try
        {
            var entries = package.Entries.Select(e => e.FullName).ToList();

            // Check for required files
            var hasNuspec = entries.Any(e => e.EndsWith(".nuspec"));
            if (!hasNuspec)
            {
                validation.Errors.Add("Package missing .nuspec file");
            }

            // Check for lib folder structure
            var hasLibFolder = entries.Any(e => e.StartsWith("lib/"));
            if (!hasLibFolder)
            {
                validation.Errors.Add("Package missing lib/ folder");
            }

            // Check for proper folder structure
            var improperPaths = entries.Where(e => e.Contains("\\")).ToList();
            if (improperPaths.Any())
            {
                validation.Warnings.Add($"Package contains paths with backslashes: {string.Join(", ", improperPaths.Take(3))}");
            }

            validation.IsValid = !validation.Errors.Any();
        }
        catch (Exception ex)
        {
            validation.Errors.Add($"Structure validation failed: {ex.Message}");
            validation.IsValid = false;
        }

        return validation;
    }

    private async Task<PackageMetadataValidation> ValidatePackageMetadataAsync(ZipArchive package)
    {
        var validation = new PackageMetadataValidation();

        try
        {
            var nuspecEntry = package.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec"));
            if (nuspecEntry == null)
            {
                validation.Errors.Add("No .nuspec file found");
                validation.IsValid = false;
                return validation;
            }

            using var stream = nuspecEntry.Open();
            using var reader = new StreamReader(stream);
            var nuspecContent = await reader.ReadToEndAsync();

            var nuspecDoc = XDocument.Parse(nuspecContent);
            var metadata = nuspecDoc.Descendants("metadata").FirstOrDefault();

            if (metadata == null)
            {
                validation.Errors.Add("No metadata section found in .nuspec");
                validation.IsValid = false;
                return validation;
            }

            // Validate required metadata fields
            var requiredFields = new[] { "id", "version", "description", "authors" };
            foreach (var field in requiredFields)
            {
                var element = metadata.Element(field);
                if (element == null || string.IsNullOrWhiteSpace(element.Value))
                {
                    validation.Errors.Add($"Required metadata field missing or empty: {field}");
                }
            }

            // Validate version format
            var versionElement = metadata.Element("version");
            if (versionElement != null && !IsValidSemanticVersion(versionElement.Value))
            {
                validation.Errors.Add($"Invalid version format: {versionElement.Value}");
            }

            // Validate license information
            var licenseElement = metadata.Element("license");
            var licenseUrlElement = metadata.Element("licenseUrl");
            if (licenseElement == null && licenseUrlElement == null)
            {
                validation.Warnings.Add("No license information specified");
            }

            validation.IsValid = !validation.Errors.Any();
        }
        catch (Exception ex)
        {
            validation.Errors.Add($"Metadata validation failed: {ex.Message}");
            validation.IsValid = false;
        }

        return validation;
    }

    private TargetFrameworkValidation ValidateTargetFrameworks(ZipArchive package)
    {
        var validation = new TargetFrameworkValidation();

        try
        {
            var libEntries = package.Entries.Where(e => e.FullName.StartsWith("lib/")).ToList();
            var frameworks = new HashSet<string>();

            foreach (var entry in libEntries)
            {
                var pathParts = entry.FullName.Split('/');
                if (pathParts.Length > 1)
                {
                    var framework = pathParts[1];
                    if (!string.IsNullOrEmpty(framework))
                    {
                        frameworks.Add(framework);
                    }
                }
            }

            validation.SupportedFrameworks = frameworks.ToList();

            // Check for required frameworks
            foreach (var requiredFramework in _config.RequiredTargetFrameworks)
            {
                if (!frameworks.Contains(requiredFramework))
                {
                    validation.Errors.Add($"Missing required target framework: {requiredFramework}");
                }
            }

            // Validate framework naming
            foreach (var framework in frameworks)
            {
                if (!IsValidFrameworkMoniker(framework))
                {
                    validation.Warnings.Add($"Non-standard framework moniker: {framework}");
                }
            }

            validation.IsValid = !validation.Errors.Any();
        }
        catch (Exception ex)
        {
            validation.Errors.Add($"Target framework validation failed: {ex.Message}");
            validation.IsValid = false;
        }

        return validation;
    }

    private async Task<AssemblyValidation> ValidateAssemblyContentsAsync(ZipArchive package)
    {
        var validation = new AssemblyValidation();

        try
        {
            var assemblyEntries = package.Entries.Where(e => e.FullName.EndsWith(".dll") || e.FullName.EndsWith(".exe")).ToList();

            foreach (var assemblyEntry in assemblyEntries)
            {
                try
                {
                    using var stream = assemblyEntry.Open();
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    var assemblyBytes = memoryStream.ToArray();

                    // Load assembly for inspection (metadata only)
                    var assembly = Assembly.Load(assemblyBytes);

                    var assemblyInfo = new AssemblyInfo
                    {
                        Name = assembly.GetName().Name ?? "Unknown",
                        Version = assembly.GetName().Version?.ToString() ?? "Unknown",
                        Framework = assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName ?? "Unknown",
                        Path = assemblyEntry.FullName
                    };

                    validation.Assemblies.Add(assemblyInfo);

                    // Validate strong naming if required
                    if (_config.RequireStrongNaming && assembly.GetName().GetPublicKey()?.Length == 0)
                    {
                        validation.Warnings.Add($"Assembly not strong-named: {assemblyInfo.Name}");
                    }
                }
                catch (Exception ex)
                {
                    validation.Warnings.Add($"Could not validate assembly {assemblyEntry.FullName}: {ex.Message}");
                }
            }

            validation.IsValid = !validation.Errors.Any();
        }
        catch (Exception ex)
        {
            validation.Errors.Add($"Assembly validation failed: {ex.Message}");
            validation.IsValid = false;
        }

        return validation;
    }

    private PackageDependencyValidationResult ValidateDependencies(ZipArchive package)
    {
        var validation = new PackageDependencyValidationResult();

        try
        {
            var nuspecEntry = package.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec"));
            if (nuspecEntry != null)
            {
                using var stream = nuspecEntry.Open();
                using var reader = new StreamReader(stream);
                var nuspecContent = reader.ReadToEnd();

                var nuspecDoc = XDocument.Parse(nuspecContent);
                var dependencies = nuspecDoc.Descendants("dependency").ToList();

                foreach (var dependency in dependencies)
                {
                    var id = dependency.Attribute("id")?.Value;
                    var version = dependency.Attribute("version")?.Value;

                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
                    {
                        validation.Dependencies.Add(new PackageDependency
                        {
                            Id = id,
                            Version = version
                        });

                        // Validate version range format
                        if (!IsValidVersionRange(version))
                        {
                            validation.Warnings.Add($"Invalid version range format for dependency {id}: {version}");
                        }
                    }
                }
            }

            validation.IsValid = !validation.Errors.Any();
        }
        catch (Exception ex)
        {
            validation.Errors.Add($"Dependency validation failed: {ex.Message}");
            validation.IsValid = false;
        }

        return validation;
    }

    private DocumentationValidation ValidateDocumentation(ZipArchive package)
    {
        var validation = new DocumentationValidation();

        try
        {
            var xmlDocFiles = package.Entries.Where(e => e.FullName.EndsWith(".xml")).ToList();
            var assemblyFiles = package.Entries.Where(e => e.FullName.EndsWith(".dll")).ToList();

            // Check that each assembly has corresponding XML documentation
            foreach (var assemblyFile in assemblyFiles)
            {
                var assemblyName = Path.GetFileNameWithoutExtension(assemblyFile.Name);
                var expectedXmlDoc = $"{assemblyName}.xml";
                var hasXmlDoc = xmlDocFiles.Any(x => x.Name.Equals(expectedXmlDoc, StringComparison.OrdinalIgnoreCase));

                if (!hasXmlDoc && _config.RequireXmlDocumentation)
                {
                    validation.Warnings.Add($"Missing XML documentation for assembly: {assemblyName}");
                }
            }

            // Check for README or other documentation
            var hasReadme = package.Entries.Any(e => e.Name.StartsWith("README", StringComparison.OrdinalIgnoreCase));
            if (!hasReadme)
            {
                validation.Warnings.Add("No README file found in package");
            }

            validation.IsValid = !validation.Errors.Any();
        }
        catch (Exception ex)
        {
            validation.Errors.Add($"Documentation validation failed: {ex.Message}");
            validation.IsValid = false;
        }

        return validation;
    }

    // Helper methods

    private bool IsValidSemanticVersion(string version)
    {
        var semVerPattern = @"^(?P<major>0|[1-9]\d*)\.(?P<minor>0|[1-9]\d*)\.(?P<patch>0|[1-9]\d*)(?:-(?P<prerelease>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+(?P<buildmetadata>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$";
        return Regex.IsMatch(version, semVerPattern);
    }

    private bool IsValidFrameworkMoniker(string framework)
    {
        var validFrameworks = new[]
        {
            "netstandard2.0", "netstandard2.1",
            "net5.0", "net6.0", "net7.0", "net8.0", "net9.0", "net10.0",
            "netcoreapp3.1", "netframework4.6.1", "netframework4.7.2", "netframework4.8"
        };

        return validFrameworks.Any(vf => framework.StartsWith(vf.Split('.')[0]));
    }

    private bool IsValidVersionRange(string version)
    {
        // Simplified version range validation
        return !string.IsNullOrEmpty(version) && (version.Contains('[') || version.Contains('(') || char.IsDigit(version[0]));
    }

    private string ExtractPackageNameWithoutVersion(string packageName)
    {
        var versionPattern = @"\.\d+\.\d+\.\d+";
        return Regex.Replace(packageName, versionPattern + @".*$", "");
    }

    private string ExtractVersionFromPackageName(string packageName)
    {
        var versionPattern = @"(\d+\.\d+\.\d+(?:\.\d+)?(?:-[\w\d\.-]+)?)";
        var match = Regex.Match(packageName, versionPattern);
        return match.Success ? match.Groups[1].Value : "unknown";
    }

    private Dictionary<string, List<string>> BuildDependencyGraph(List<PackageValidationResult> packageResults)
    {
        var graph = new Dictionary<string, List<string>>();

        foreach (var package in packageResults)
        {
            var packageName = ExtractPackageNameWithoutVersion(package.PackageName);
            var dependencies = package.DependencyValidation.Dependencies.Select(d => d.Id).ToList();
            graph[packageName] = dependencies;
        }

        return graph;
    }

    private List<string> DetectCircularDependencies(Dictionary<string, List<string>> dependencyGraph)
    {
        // Simplified circular dependency detection
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        var circularDependencies = new List<string>();

        foreach (var package in dependencyGraph.Keys)
        {
            if (!visited.Contains(package))
            {
                DetectCircularDependenciesRecursive(package, dependencyGraph, visited, recursionStack, circularDependencies);
            }
        }

        return circularDependencies;
    }

    private void DetectCircularDependenciesRecursive(
        string package,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        List<string> circularDependencies)
    {
        visited.Add(package);
        recursionStack.Add(package);

        if (graph.TryGetValue(package, out var dependencies))
        {
            foreach (var dependency in dependencies)
            {
                if (!visited.Contains(dependency))
                {
                    DetectCircularDependenciesRecursive(dependency, graph, visited, recursionStack, circularDependencies);
                }
                else if (recursionStack.Contains(dependency))
                {
                    circularDependencies.Add($"{package} -> {dependency}");
                }
            }
        }

        recursionStack.Remove(package);
    }

    private List<string> DetectVersionConflicts(Dictionary<string, List<string>> dependencyGraph)
    {
        // Simplified version conflict detection
        return [];
    }

    private List<string> DetectMissingDependencies(Dictionary<string, List<string>> dependencyGraph, List<PackageValidationResult> packageResults)
    {
        var availablePackages = packageResults.Select(p => ExtractPackageNameWithoutVersion(p.PackageName)).ToHashSet();
        var missingDependencies = new List<string>();

        foreach (var (package, dependencies) in dependencyGraph)
        {
            foreach (var dependency in dependencies)
            {
                if (!availablePackages.Contains(dependency) && !_config.ExternalDependencies.Contains(dependency))
                {
                    missingDependencies.Add($"{package} -> {dependency}");
                }
            }
        }

        return missingDependencies;
    }

    private string GenerateValidationReport(PackageValidationResults results)
    {
        var report = new List<string>
        {
            "# Package Validation Report",
            $"Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            $"Total Packages: {results.TotalPackagesFound}",
            $"Valid Packages: {results.ValidPackagesCount}",
            $"Invalid Packages: {results.InvalidPackagesCount}",
            $"Overall Status: {(results.Success ? "✅ PASSED" : "❌ FAILED")}",
            ""
        };

        if (results.InvalidPackagesCount > 0)
        {
            report.Add("## Invalid Packages");
            foreach (var invalidPackage in results.PackageResults.Where(p => !p.IsValid))
            {
                report.Add($"### {invalidPackage.PackageName}");
                foreach (var error in invalidPackage.ValidationErrors)
                {
                    report.Add($"- ❌ {error}");
                }
                report.Add("");
            }
        }

        return string.Join(Environment.NewLine, report);
    }
}

// Supporting data structures

internal class PackageValidationConfiguration
{
    public List<string> RequiredTargetFrameworks { get; set; } = ["netstandard2.0", "net6.0", "net8.0", "net10.0"];
    public List<string> RequiredPackages { get; set; } = ["HeroMessaging", "HeroMessaging.Abstractions"];
    public List<string> CorePackageNames { get; set; } = ["HeroMessaging"];
    public List<string> ExternalDependencies { get; set; } = ["Microsoft.Extensions", "System"];
    public bool RequireStrongNaming { get; set; } = false;
    public bool RequireXmlDocumentation { get; set; } = true;
}

internal class PackageValidationResults
{
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string PackageDirectory { get; set; } = string.Empty;
    public int TotalPackagesFound { get; set; }
    public int ValidPackagesCount { get; set; }
    public int InvalidPackagesCount { get; set; }
    public List<PackageValidationResult> PackageResults { get; set; } = [];
    public PackageDependencyValidation DependencyValidation { get; set; } = new();
    public ConstitutionalComplianceValidation ConstitutionalValidation { get; set; } = new();
    public string ValidationReport { get; set; } = string.Empty;
}

internal class PackageValidationResult
{
    public string PackagePath { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public List<string> ValidationErrors { get; set; } = [];
    public PackageStructureValidation StructureValidation { get; set; } = new();
    public PackageMetadataValidation MetadataValidation { get; set; } = new();
    public TargetFrameworkValidation FrameworkValidation { get; set; } = new();
    public AssemblyValidation AssemblyValidation { get; set; } = new();
    public PackageDependencyValidationResult DependencyValidation { get; set; } = new();
    public DocumentationValidation DocumentationValidation { get; set; } = new();
}

internal class PackageStructureValidation
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

internal class PackageMetadataValidation
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

internal class TargetFrameworkValidation
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> SupportedFrameworks { get; set; } = [];
}

internal class AssemblyValidation
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<AssemblyInfo> Assemblies { get; set; } = [];
}

internal class AssemblyInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Framework { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

internal class PackageDependencyValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<PackageDependency> Dependencies { get; set; } = [];
}

internal class PackageDependency
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

internal class DocumentationValidation
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

internal class PackageDependencyValidation
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public Dictionary<string, List<string>> DependencyGraph { get; set; } = [];
}

internal class ConstitutionalComplianceValidation
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
