// Copyright (c) HeroMessaging Contributors. All rights reserved.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HeroMessaging.SourceGenerators;

/// <summary>
/// Generates saga state machines from declarative DSL.
/// </summary>
[Generator]
public class SagaDslGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Register attribute sources
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("GenerateSagaAttribute.g.cs", SourceText.From(AttributeSources, Encoding.UTF8));
        });

        // Find classes with [GenerateSaga] attribute
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidateNode(node),
                transform: static (ctx, _) => GetSemanticTarget(ctx))
            .Where(static saga => saga is not null);

        // Generate saga state machines
        context.RegisterSourceOutput(candidates, static (spc, saga) =>
        {
            if (saga is null) return;

            var source = GenerateSaga(saga);
            spc.AddSource($"{saga.ClassName}.Saga.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static bool IsCandidateNode(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDecl &&
               classDecl.AttributeLists.Count > 0 &&
               classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    }

    private static SagaInfo? GetSemanticTarget(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;

        if (symbol is null) return null;

        // Check for [GenerateSaga] attribute
        var hasAttribute = symbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name == "GenerateSagaAttribute" ||
                        attr.AttributeClass?.Name == "GenerateSaga");

        if (!hasAttribute) return null;

        // Parse saga states from nested classes
        var states = new List<SagaStateInfo>();
        string? initialState = null;

        foreach (var member in symbol.GetMembers().OfType<INamedTypeSymbol>())
        {
            var stateAttr = member.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == "SagaStateAttribute");

            if (stateAttr is null) continue;

            var stateName = stateAttr.ConstructorArguments.FirstOrDefault().Value?.ToString();
            if (string.IsNullOrEmpty(stateName)) continue;

            var isInitial = member.GetAttributes()
                .Any(attr => attr.AttributeClass?.Name == "InitialStateAttribute");

            if (isInitial)
                initialState = stateName;

            var stateInfo = new SagaStateInfo
            {
                StateName = stateName,
                ClassName = member.Name,
                IsInitial = isInitial,
                EventHandlers = ParseEventHandlers(member),
                CompensationMethod = ParseCompensation(member),
                TimeoutHandler = ParseTimeout(member)
            };

            states.Add(stateInfo);
        }

        // Extract saga data type from base class
        var baseType = symbol.BaseType;
        var dataType = "object";

        if (baseType?.IsGenericType == true && baseType.Name.Contains("SagaBase"))
        {
            dataType = baseType.TypeArguments.FirstOrDefault()?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "object";
        }

        return new SagaInfo
        {
            ClassName = symbol.Name,
            Namespace = symbol.ContainingNamespace.ToDisplayString(),
            States = states,
            InitialState = initialState ?? states.FirstOrDefault()?.StateName ?? "Initial",
            DataType = dataType
        };
    }

    private static List<EventHandlerInfo> ParseEventHandlers(INamedTypeSymbol stateClass)
    {
        var handlers = new List<EventHandlerInfo>();

        foreach (var method in stateClass.GetMembers().OfType<IMethodSymbol>())
        {
            foreach (var attr in method.GetAttributes())
            {
                if (attr.AttributeClass?.Name != "OnAttribute") continue;

                // Extract event type from generic attribute
                var eventType = attr.AttributeClass.TypeArguments.FirstOrDefault()
                    ?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                if (eventType is null) continue;

                handlers.Add(new EventHandlerInfo
                {
                    EventType = eventType,
                    MethodName = method.Name,
                    IsAsync = method.IsAsync
                });
            }
        }

        return handlers;
    }

    private static string? ParseCompensation(INamedTypeSymbol stateClass)
    {
        return stateClass.GetMembers().OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.GetAttributes()
                .Any(attr => attr.AttributeClass?.Name == "CompensateAttribute"))
            ?.Name;
    }

    private static TimeoutHandlerInfo? ParseTimeout(INamedTypeSymbol stateClass)
    {
        foreach (var method in stateClass.GetMembers().OfType<IMethodSymbol>())
        {
            var attr = method.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "OnTimeoutAttribute");

            if (attr is null) continue;

            // Extract timeout duration
            var seconds = 30; // Default
            if (attr.ConstructorArguments.Length > 0)
            {
                seconds = (int)(attr.ConstructorArguments[0].Value ?? 30);
            }

            return new TimeoutHandlerInfo
            {
                MethodName = method.Name,
                TimeoutSeconds = seconds
            };
        }

        return null;
    }

    private static string GenerateSaga(SagaInfo saga)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using HeroMessaging.Abstractions.Sagas;");
        sb.AppendLine();
        sb.AppendLine($"namespace {saga.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"/// Auto-generated saga state machine for {saga.ClassName}.");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"public partial class {saga.ClassName}");
        sb.AppendLine("{");

        // Generate state enum
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Saga states.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public enum States");
        sb.AppendLine("    {");
        foreach (var state in saga.States)
        {
            sb.AppendLine($"        {state.StateName},");
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // Generate ConfigureStateMachine method
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Configures the saga state machine.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    protected override void ConfigureStateMachine()");
        sb.AppendLine("    {");
        sb.AppendLine($"        Initially(\"{saga.InitialState}\");");
        sb.AppendLine();

        foreach (var state in saga.States)
        {
            sb.AppendLine($"        // State: {state.StateName}");
            sb.AppendLine($"        During(\"{state.StateName}\", state =>");
            sb.AppendLine("        {");

            // Event handlers
            foreach (var handler in state.EventHandlers)
            {
                var eventTypeName = GetShortTypeName(handler.EventType);
                sb.AppendLine($"            state.When<{handler.EventType}>()");
                sb.AppendLine($"                 .Then(async (evt, ct) => await new {state.ClassName}().{handler.MethodName}(evt))");
                if (handler.TransitionTo is not null)
                {
                    sb.AppendLine($"                 .TransitionTo(\"{handler.TransitionTo}\");");
                }
                else
                {
                    sb.AppendLine("                 .Execute();");
                }
            }

            // Compensation
            if (state.CompensationMethod is not null)
            {
                sb.AppendLine($"            state.OnCompensate(async () => await new {state.ClassName}().{state.CompensationMethod}());");
            }

            // Timeout
            if (state.TimeoutHandler is not null)
            {
                sb.AppendLine($"            state.WithTimeout(TimeSpan.FromSeconds({state.TimeoutHandler.TimeoutSeconds}),");
                sb.AppendLine($"                async () => await new {state.ClassName}().{state.TimeoutHandler.MethodName}());");
            }

            sb.AppendLine("        });");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine();

        // Generate helper methods
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Transitions to the specified state.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    protected void TransitionTo(string stateName)");
        sb.AppendLine("    {");
        sb.AppendLine("        State = stateName;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Marks the saga as completed.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    protected void Complete()");
        sb.AppendLine("    {");
        sb.AppendLine("        IsCompleted = true;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Marks the saga as failed.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    protected void Fail(string reason)");
        sb.AppendLine("    {");
        sb.AppendLine("        IsFailed = true;");
        sb.AppendLine("        FailureReason = reason;");
        sb.AppendLine("    }");

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetShortTypeName(string fullyQualifiedName)
    {
        var parts = fullyQualifiedName.Split('.');
        return parts[parts.Length - 1];
    }

    private class SagaInfo
    {
        public string ClassName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public List<SagaStateInfo> States { get; set; } = new();
        public string InitialState { get; set; } = string.Empty;
        public string DataType { get; set; } = "object";
    }

    private class SagaStateInfo
    {
        public string StateName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public bool IsInitial { get; set; }
        public List<EventHandlerInfo> EventHandlers { get; set; } = new();
        public string? CompensationMethod { get; set; }
        public TimeoutHandlerInfo? TimeoutHandler { get; set; }
    }

    private class EventHandlerInfo
    {
        public string EventType { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public bool IsAsync { get; set; }
        public string? TransitionTo { get; set; }
    }

    private class TimeoutHandlerInfo
    {
        public string MethodName { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; }
    }

    private const string AttributeSources = @"// <auto-generated/>
using System;

namespace HeroMessaging.SourceGeneration
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal sealed class GenerateSagaAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal sealed class SagaStateAttribute : Attribute
    {
        public string StateName { get; }
        public SagaStateAttribute(string stateName) => StateName = stateName;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    internal sealed class OnAttribute<TEvent> : Attribute where TEvent : class { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    internal sealed class CompensateAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    internal sealed class OnTimeoutAttribute : Attribute
    {
        public TimeSpan Timeout { get; }
        public OnTimeoutAttribute(int seconds) => Timeout = TimeSpan.FromSeconds(seconds);
        public OnTimeoutAttribute(int hours, int minutes, int seconds) => Timeout = new TimeSpan(hours, minutes, seconds);
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal sealed class InitialStateAttribute : Attribute { }
}";
}
