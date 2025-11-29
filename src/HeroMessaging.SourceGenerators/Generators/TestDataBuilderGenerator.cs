// Copyright (c) HeroMessaging Contributors. All rights reserved.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;

namespace HeroMessaging.SourceGenerators.Generators;

/// <summary>
/// Generates sophisticated test data builders with auto-randomization, object mothers, and collections.
/// More advanced than basic MessageBuilderGenerator.
/// </summary>
[Generator]
public class TestDataBuilderGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find classes/records with [GenerateTestDataBuilder]
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsTestDataBuilderCandidate(node),
                transform: static (ctx, _) => GetTestDataBuilderInfo(ctx))
            .Where(static info => info is not null);

        // Generate test data builders
        context.RegisterSourceOutput(classDeclarations, (spc, builderInfo) =>
        {
            if (builderInfo is null) return;

            var source = GenerateTestDataBuilder(builderInfo.Value);
            spc.AddSource($"TestData.{builderInfo.Value.TypeName}.g.cs",
                SourceText.From(source, Encoding.UTF8));
        });
    }

    private static bool IsTestDataBuilderCandidate(SyntaxNode node)
    {
        return (node is ClassDeclarationSyntax || node is RecordDeclarationSyntax) &&
               ((TypeDeclarationSyntax)node).AttributeLists.Count > 0;
    }

    private static TestDataBuilderInfo? GetTestDataBuilderInfo(GeneratorSyntaxContext context)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;

        // Check for [GenerateTestDataBuilder] attribute
        var hasAttribute = typeDecl.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a => a.Name.ToString().Contains("GenerateTestDataBuilder"));

        if (!hasAttribute) return null;

        var namespaceDecl = typeDecl.FirstAncestorOrSelf<NamespaceDeclarationSyntax>();
        var namespaceName = namespaceDecl?.Name.ToString() ?? "Global";

        var properties = new List<PropertyInfo>();

        foreach (var member in typeDecl.Members)
        {
            if (member is PropertyDeclarationSyntax propDecl)
            {
                var randomAttr = GetRandomAttribute(propDecl);

                properties.Add(new PropertyInfo
                {
                    Name = propDecl.Identifier.Text,
                    Type = propDecl.Type.ToString(),
                    RandomAttribute = randomAttr
                });
            }
        }

        return new TestDataBuilderInfo
        {
            Namespace = namespaceName,
            TypeName = typeDecl.Identifier.Text,
            Properties = properties,
            IsRecord = typeDecl is RecordDeclarationSyntax
        };
    }

    private static RandomAttributeInfo? GetRandomAttribute(PropertyDeclarationSyntax property)
    {
        foreach (var attrList in property.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var attrName = attr.Name.ToString();

                if (attrName.Contains("RandomString"))
                {
                    return new RandomAttributeInfo
                    {
                        Type = RandomAttributeType.String,
                        Prefix = GetAttributeArgument(attr, "Prefix"),
                        Suffix = GetAttributeArgument(attr, "Suffix"),
                        Length = GetAttributeIntArgument(attr, "Length", 10)
                    };
                }
                else if (attrName.Contains("RandomEmail"))
                {
                    return new RandomAttributeInfo
                    {
                        Type = RandomAttributeType.Email,
                        Domain = GetAttributeArgument(attr, "Domain") ?? "example.com"
                    };
                }
                else if (attrName.Contains("RandomInt"))
                {
                    return new RandomAttributeInfo
                    {
                        Type = RandomAttributeType.Int,
                        Min = GetAttributeIntArgument(attr, "Min", 1),
                        Max = GetAttributeIntArgument(attr, "Max", 100)
                    };
                }
                else if (attrName.Contains("RandomDecimal"))
                {
                    return new RandomAttributeInfo
                    {
                        Type = RandomAttributeType.Decimal,
                        MinDecimal = GetAttributeDoubleArgument(attr, "Min", 0.01),
                        MaxDecimal = GetAttributeDoubleArgument(attr, "Max", 1000.00),
                        DecimalPlaces = GetAttributeIntArgument(attr, "DecimalPlaces", 2)
                    };
                }
                else if (attrName.Contains("RandomDateTime"))
                {
                    return new RandomAttributeInfo
                    {
                        Type = RandomAttributeType.DateTime,
                        DaysFromNow = GetAttributeIntArgument(attr, "DaysFromNow", -365),
                        DaysToNow = GetAttributeIntArgument(attr, "DaysToNow", 0)
                    };
                }
                else if (attrName.Contains("RandomGuid"))
                {
                    return new RandomAttributeInfo
                    {
                        Type = RandomAttributeType.Guid,
                        GuidFormat = GetAttributeArgument(attr, "Format") ?? "D"
                    };
                }
                else if (attrName.Contains("RandomEnum"))
                {
                    return new RandomAttributeInfo
                    {
                        Type = RandomAttributeType.Enum
                    };
                }
                else if (attrName.Contains("RandomBool"))
                {
                    return new RandomAttributeInfo
                    {
                        Type = RandomAttributeType.Bool
                    };
                }
                else if (attrName.Contains("RandomCollection"))
                {
                    return new RandomAttributeInfo
                    {
                        Type = RandomAttributeType.Collection,
                        MinCount = GetAttributeIntArgument(attr, "MinCount", 1),
                        MaxCount = GetAttributeIntArgument(attr, "MaxCount", 5)
                    };
                }
            }
        }

        return null;
    }

    private static string? GetAttributeArgument(AttributeSyntax attr, string argName)
    {
        if (attr.ArgumentList == null) return null;

        foreach (var arg in attr.ArgumentList.Arguments)
        {
            if (arg.NameEquals?.Name.Identifier.Text == argName)
            {
                return arg.Expression.ToString().Trim('"');
            }
        }

        return null;
    }

    private static int GetAttributeIntArgument(AttributeSyntax attr, string argName, int defaultValue)
    {
        var value = GetAttributeArgument(attr, argName);
        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    private static double GetAttributeDoubleArgument(AttributeSyntax attr, string argName, double defaultValue)
    {
        var value = GetAttributeArgument(attr, argName);
        return double.TryParse(value, out var result) ? result : defaultValue;
    }

    private static string GenerateTestDataBuilder(TestDataBuilderInfo info)
    {
        var sb = new StringBuilder();

        sb.AppendLine($@"// <auto-generated/>
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace {info.Namespace};

/// <summary>
/// Test data builder for {info.TypeName} with auto-randomization and object mother patterns.
/// </summary>
public static partial class TestData
{{
    private static readonly Random _random = Random.Shared; // Thread-safe

    /// <summary>
    /// Creates a new builder for {info.TypeName}.
    /// </summary>
    public static {info.TypeName}Builder {info.TypeName}()
    {{
        return new {info.TypeName}Builder();
    }}

    public class {info.TypeName}Builder
    {{
        private static int _sequence; // Thread-safe via Interlocked");

        // Generate fields
        foreach (var prop in info.Properties)
        {
            sb.AppendLine($"        private {prop.Type} _{ToCamelCase(prop.Name)};");
        }

        sb.AppendLine();
        sb.AppendLine($"        public {info.TypeName}Builder()");
        sb.AppendLine("        {");
        sb.AppendLine("            Interlocked.Increment(ref _sequence);");

        // Initialize with defaults
        foreach (var prop in info.Properties)
        {
            sb.AppendLine($"            _{ToCamelCase(prop.Name)} = default({prop.Type})!;");
        }

        sb.AppendLine("        }");
        sb.AppendLine();

        // Generate With methods
        foreach (var prop in info.Properties)
        {
            sb.AppendLine($@"        public {info.TypeName}Builder With{prop.Name}({prop.Type} value)
        {{
            _{ToCamelCase(prop.Name)} = value;
            return this;
        }}
");
        }

        // Generate WithRandomData method
        sb.AppendLine($@"        /// <summary>
        /// Fills all properties with random data based on their types and attributes.
        /// </summary>
        public {info.TypeName}Builder WithRandomData()
        {{");

        foreach (var prop in info.Properties)
        {
            sb.AppendLine($"            _{ToCamelCase(prop.Name)} = {GenerateRandomValue(prop)};");
        }

        sb.AppendLine("            return this;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Generate Build method
        sb.AppendLine($@"        public {info.TypeName} Build()
        {{
            return new {info.TypeName}
            {{");

        foreach (var prop in info.Properties)
        {
                sb.AppendLine($"                {prop.Name} = _{ToCamelCase(prop.Name)},");
        }

        sb.AppendLine($@"            }};
        }}

        /// <summary>
        /// Creates multiple instances with random data.
        /// </summary>
        public List<{info.TypeName}> CreateMany(int count)
        {{
            var items = new List<{info.TypeName}>();
            for (int i = 0; i < count; i++)
            {{
                items.Add(new {info.TypeName}Builder().WithRandomData().Build());
            }}
            return items;
        }}
    }}
}}");

        return sb.ToString();
    }

    private static string GenerateRandomValue(PropertyInfo prop)
    {
        if (prop.RandomAttribute != null)
        {
            return GenerateRandomValueFromAttribute(prop);
        }

        // Infer from type
        var type = prop.Type.TrimEnd('?'); // Remove nullable marker

        if (type == "string")
            return $"\"value-\" + _random.Next(1000, 9999)";
        else if (type == "int" || type == "Int32")
            return "_random.Next(1, 100)";
        else if (type == "long" || type == "Int64")
            return "_random.Next(1, 100)";
        else if (type == "decimal")
            return "Math.Round((decimal)(_random.NextDouble() * 1000), 2)";
        else if (type == "double")
            return "_random.NextDouble() * 1000";
        else if (type == "bool" || type == "Boolean")
            return "_random.Next(0, 2) == 1";
        else if (type == "DateTime")
            return "DateTimeOffset.UtcNow.AddDays(-_random.Next(0, 365))";
        else if (type == "DateTimeOffset")
            return "DateTimeOffset.UtcNow.AddDays(-_random.Next(0, 365))";
        else if (type == "Guid")
            return "Guid.NewGuid().ToString()";
        else if (type.StartsWith("List<") || type.Contains("[]"))
            return $"new {type}()";
        else
            return $"default({prop.Type})!";
    }

    private static string GenerateRandomValueFromAttribute(PropertyInfo prop)
    {
        // Explicitly declare as non-nullable to satisfy nullable reference type checking
        RandomAttributeInfo attr = prop.RandomAttribute!.Value;

        return attr.Type switch
        {
            RandomAttributeType.String => GenerateRandomString(attr),
            RandomAttributeType.Email => GenerateRandomEmail(attr),
            RandomAttributeType.Int => $"_random.Next({attr.Min}, {attr.Max} + 1)",
            RandomAttributeType.Decimal => $"Math.Round((decimal)(_random.NextDouble() * ({attr.MaxDecimal} - {attr.MinDecimal}) + {attr.MinDecimal}), {attr.DecimalPlaces})",
            RandomAttributeType.DateTime => $"DateTimeOffset.UtcNow.AddDays(_random.Next({attr.DaysFromNow}, {attr.DaysToNow} + 1))",
            RandomAttributeType.Guid => $"Guid.NewGuid().ToString(\"{attr.GuidFormat}\")",
            RandomAttributeType.Bool => "_random.Next(0, 2) == 1",
            RandomAttributeType.Enum => $"({prop.Type})_random.Next(0, Enum.GetValues(typeof({prop.Type})).Length)",
            RandomAttributeType.Collection => $"new {prop.Type}()",
            _ => $"default({prop.Type})!"
        };
    }

    private static string GenerateRandomString(RandomAttributeInfo attr)
    {
        var prefix = string.IsNullOrEmpty(attr.Prefix) ? "" : $"\"{attr.Prefix}\" + ";
        var suffix = string.IsNullOrEmpty(attr.Suffix) ? "" : $" + \"{attr.Suffix}\"";
        var randomPart = $"GenerateRandomString({attr.Length})";

        return $"{prefix}{randomPart}{suffix}";
    }

    private static string GenerateRandomEmail(RandomAttributeInfo attr)
    {
        return $"\"user\" + _random.Next(1000, 9999) + \"@{attr.Domain}\"";
    }

    private static string ToCamelCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return char.ToLowerInvariant(text[0]) + text.Substring(1);
    }

    private struct TestDataBuilderInfo
    {
        public string Namespace { get; set; }
        public string TypeName { get; set; }
        public List<PropertyInfo> Properties { get; set; }
        public bool IsRecord { get; set; }
    }

    private struct PropertyInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public RandomAttributeInfo? RandomAttribute { get; set; }
    }

    private struct RandomAttributeInfo
    {
        public RandomAttributeType Type { get; set; }
        public string? Prefix { get; set; }
        public string? Suffix { get; set; }
        public int Length { get; set; }
        public string? Domain { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public double MinDecimal { get; set; }
        public double MaxDecimal { get; set; }
        public int DecimalPlaces { get; set; }
        public int DaysFromNow { get; set; }
        public int DaysToNow { get; set; }
        public string? GuidFormat { get; set; }
        public int MinCount { get; set; }
        public int MaxCount { get; set; }
    }

    private enum RandomAttributeType
    {
        String,
        Email,
        Int,
        Decimal,
        DateTime,
        Guid,
        Bool,
        Enum,
        Collection
    }
}
