using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeelMatrix.ConfigGap.Probe;

internal static class BoundedKeyPropagation
{
    private const int MaxHops = 2;

    public static IReadOnlyList<StringResolution> Resolve(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (expression is not IdentifierNameSyntax identifier ||
            semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not IParameterSymbol parameter ||
            parameter.Type.SpecialType != SpecialType.System_String)
        {
            return [];
        }

        var resolutions = ResolveParameter(parameter, compilation, MaxHops, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default), cancellationToken);
        return resolutions.Count == 0
            ? [Unknown("dynamic-helper-unresolvable")]
            : resolutions;
    }

    private static List<StringResolution> ResolveParameter(
        IParameterSymbol parameter,
        Compilation compilation,
        int remainingHops,
        HashSet<IMethodSymbol> visited,
        CancellationToken cancellationToken)
    {
        if (remainingHops == 0 || parameter.ContainingSymbol is not IMethodSymbol method || !IsSourceHelper(method, compilation) || !visited.Add(method))
        {
            return [Unknown("dynamic-helper-depth")];
        }

        if (!TryGetForwardingUse(parameter, compilation, cancellationToken, out _))
        {
            return [Unknown("dynamic-helper-unsupported")];
        }

        var callSites = FindCallSites(method, compilation, cancellationToken);
        if (callSites.Count == 0)
        {
            return [Unknown("dynamic-helper-unresolved")];
        }

        var resolutions = new List<StringResolution>();
        foreach (var callSite in callSites)
        {
            var argument = GetArgumentForParameter(callSite, parameter.Ordinal, method);
            if (argument is null)
            {
                resolutions.Add(Unknown("dynamic-helper-argument"));
                continue;
            }

            var direct = KeyResolution.Resolve(argument.Expression, callSite.Model);
            if (IsSupportedCallSiteConstant(direct))
            {
                resolutions.Add(direct);
                continue;
            }

            if (argument.Expression is IdentifierNameSyntax identifier &&
                callSite.Model.GetSymbolInfo(identifier, cancellationToken).Symbol is IParameterSymbol callerParameter &&
                callerParameter.Type.SpecialType == SpecialType.System_String &&
                remainingHops > 1)
            {
                resolutions.AddRange(ResolveParameter(
                    callerParameter,
                    compilation,
                    remainingHops - 1,
                    new HashSet<IMethodSymbol>(visited, SymbolEqualityComparer.Default),
                    cancellationToken));
                continue;
            }

            resolutions.Add(Unknown(argument.Expression is IdentifierNameSyntax
                ? "dynamic-helper-argument"
                : "dynamic-helper-nonconstant"));
        }

        return resolutions
            .GroupBy(resolution => resolution.Value ?? "<unknown>", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static bool TryGetForwardingUse(
        IParameterSymbol parameter,
        Compilation compilation,
        CancellationToken cancellationToken,
        out IdentifierNameSyntax? forwardingUse)
    {
        forwardingUse = null;
        var declaration = parameter.ContainingSymbol.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax(cancellationToken);
        if (declaration is null || !IsSourceHelper(parameter.ContainingSymbol, compilation))
        {
            return false;
        }

        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        var body = declaration switch
        {
            MethodDeclarationSyntax methodDeclaration => methodDeclaration.Body ?? (SyntaxNode?)methodDeclaration.ExpressionBody,
            LocalFunctionStatementSyntax localFunction => localFunction.Body ?? (SyntaxNode?)localFunction.ExpressionBody,
            _ => null
        };
        if (body is null)
        {
            return false;
        }

        var uses = body.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(identifier => SymbolEqualityComparer.Default.Equals(
                model.GetSymbolInfo(identifier, cancellationToken).Symbol,
                parameter))
            .ToArray();
        var forwardingUses = uses
            .Where(identifier => IsDirectMethodBodyUse(identifier, parameter, model, compilation, cancellationToken))
            .ToArray();
        if (forwardingUses.Length != 1 ||
            uses.Any(identifier => !ReferenceEquals(identifier, forwardingUses[0]) && IsForbiddenParameterUse(identifier)))
        {
            return false;
        }

        forwardingUse = forwardingUses[0];
        return true;
    }

    private static bool IsDirectMethodBodyUse(
        IdentifierNameSyntax identifier,
        IParameterSymbol parameter,
        SemanticModel model,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (model.GetEnclosingSymbol(identifier.SpanStart, cancellationToken) is not IMethodSymbol enclosingMethod ||
            !SymbolEqualityComparer.Default.Equals(enclosingMethod, parameter.ContainingSymbol) ||
            identifier.Parent is not ArgumentSyntax argument ||
            argument.Expression != identifier)
        {
            return false;
        }

        if (argument.Parent?.Parent is ElementAccessExpressionSyntax elementAccess)
        {
            return IsConfigurationType(model.GetTypeInfo(elementAccess.Expression, cancellationToken).Type);
        }

        if (argument.Parent?.Parent?.Parent is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        var invocationSymbol = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            var helperParameter = invocationSymbol is null
                ? null
                : GetParameterForArgument(invocation, argument, invocationSymbol);
            return invocationSymbol is not null &&
                IsSourceHelper(invocationSymbol, compilation) &&
                helperParameter?.Type.SpecialType == SpecialType.System_String;
        }

        var methodName = memberAccess.Name.Identifier.Text;
        var receiver = memberAccess.Expression;
        if (!IsConfigurationType(model.GetTypeInfo(receiver, cancellationToken).Type))
        {
            if (methodName == "BindConfiguration" &&
                invocationSymbol is not null &&
                IsSupportedBindConfiguration(invocationSymbol, model.GetTypeInfo(receiver, cancellationToken).Type))
            {
                return true;
            }

            if (invocationSymbol is null || !IsSourceHelper(invocationSymbol, compilation))
            {
                return false;
            }

            var argumentParameter = GetParameterForArgument(invocation, argument, invocationSymbol);
            return argumentParameter?.Type.SpecialType == SpecialType.System_String;
        }

        return methodName is "GetValue" or "GetSection" or "GetRequiredSection";
    }

    private static bool IsForbiddenParameterUse(IdentifierNameSyntax identifier)
    {
        if (identifier.Parent is AssignmentExpressionSyntax assignment && assignment.Left.Span.Contains(identifier.Span) ||
            identifier.Parent is PrefixUnaryExpressionSyntax prefix && (prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression)) ||
            identifier.Parent is PostfixUnaryExpressionSyntax postfix && (postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression)) ||
            identifier.Parent is ArgumentSyntax argument && argument.Expression == identifier && argument.RefKindKeyword.RawKind != 0)
        {
            return true;
        }

        for (var ancestor = identifier.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is IfStatementSyntax conditional && conditional.Condition.Span.Contains(identifier.Span) ||
                ancestor is WhileStatementSyntax whileStatement && whileStatement.Condition.Span.Contains(identifier.Span) ||
                ancestor is DoStatementSyntax doStatement && doStatement.Condition.Span.Contains(identifier.Span) ||
                ancestor is ForStatementSyntax forStatement && forStatement.Condition?.Span.Contains(identifier.Span) == true ||
                ancestor is ConditionalExpressionSyntax conditionalExpression && conditionalExpression.Condition.Span.Contains(identifier.Span) ||
                ancestor is SwitchStatementSyntax switchStatement && switchStatement.Expression.Span.Contains(identifier.Span) ||
                ancestor is SwitchExpressionSyntax switchExpression && switchExpression.GoverningExpression.Span.Contains(identifier.Span) ||
                ancestor is WhenClauseSyntax whenClause && whenClause.Condition.Span.Contains(identifier.Span))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSupportedCallSiteConstant(StringResolution resolution) =>
        resolution.Value is not null && (resolution.Kind is "static-literal" or "static-const");

    private static IParameterSymbol? GetParameterForArgument(
        InvocationExpressionSyntax invocation,
        ArgumentSyntax argument,
        IMethodSymbol method)
    {
        if (argument.NameColon is not null)
        {
            return method.Parameters.FirstOrDefault(parameter =>
                parameter.Name.Equals(argument.NameColon.Name.Identifier.Text, StringComparison.Ordinal));
        }

        var index = invocation.ArgumentList.Arguments.IndexOf(argument);
        return index >= 0 && index < method.Parameters.Length ? method.Parameters[index] : null;
    }

    private static List<CallSite> FindCallSites(
        IMethodSymbol method,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var callSites = new List<CallSite>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var symbol = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                if (symbol is not null && SymbolEqualityComparer.Default.Equals(symbol, method))
                {
                    callSites.Add(new CallSite(invocation, model));
                }
            }
        }

        return callSites;
    }

    private static ArgumentSyntax? GetArgumentForParameter(
        CallSite callSite,
        int parameterOrdinal,
        IMethodSymbol method)
    {
        foreach (var argument in callSite.Invocation.ArgumentList.Arguments)
        {
            if (argument.RefKindKeyword.RawKind != 0)
            {
                continue;
            }

            if (argument.NameColon is not null)
            {
                if (method.Parameters[parameterOrdinal].Name.Equals(argument.NameColon.Name.Identifier.Text, StringComparison.Ordinal))
                {
                    return argument;
                }

                continue;
            }

            var positionalIndex = callSite.Invocation.ArgumentList.Arguments.IndexOf(argument);
            if (positionalIndex == parameterOrdinal)
            {
                return argument;
            }
        }

        return null;
    }

    private static bool IsSourceHelper(ISymbol symbol, Compilation compilation)
    {
        if (symbol is not IMethodSymbol method ||
            method.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction) ||
            method.IsAbstract ||
            method.IsOverride ||
            method.IsVirtual ||
            method.ContainingType?.TypeKind == TypeKind.Interface ||
            method.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }

        var syntaxTree = method.DeclaringSyntaxReferences[0].SyntaxTree;
        return compilation.SyntaxTrees.Any(tree => ReferenceEquals(tree, syntaxTree));
    }

    private static bool IsConfigurationType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        return type.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfiguration" ||
            type.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfigurationSection" ||
            type.AllInterfaces.Any(interfaceType => interfaceType.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfiguration");
    }

    private static bool IsSupportedBindConfiguration(IMethodSymbol symbol, ITypeSymbol? receiverType)
    {
        if (symbol.Name != "BindConfiguration" ||
            symbol.ContainingNamespace?.ToDisplayString() != "Microsoft.Extensions.DependencyInjection" ||
            symbol.ContainingType?.Name != "OptionsBuilderConfigurationExtensions")
        {
            return false;
        }

        var optionsBuilder = receiverType as INamedTypeSymbol;
        var originalDefinition = optionsBuilder?.OriginalDefinition;
        return originalDefinition is not null &&
            originalDefinition.Name == "OptionsBuilder" &&
            originalDefinition.Arity == 1 &&
            originalDefinition.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.Options";
    }

    private static StringResolution Unknown(string kind) => new(null, kind);

    private readonly record struct CallSite(InvocationExpressionSyntax Invocation, SemanticModel Model);
}
