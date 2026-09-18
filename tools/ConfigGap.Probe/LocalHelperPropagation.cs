using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeelMatrix.ConfigGap.Probe;

/// <summary>
/// Resolves the deliberately narrow local-helper forwarding shape used by the
/// Phase 0 probe. This is not general interprocedural dataflow: only a local
/// string parameter forwarded directly into one supported access is followed,
/// and only two helper hops are allowed.
/// </summary>
internal sealed class LocalHelperPropagation
{
    private const int MaximumHelperHops = 2;

    private readonly Compilation _compilation;
    private readonly Dictionary<IMethodSymbol, HelperInfo?> _helperCache = new(SymbolEqualityComparer.Default);
    private readonly HashSet<SyntaxNode> _forwardedNodes = [];

    public LocalHelperPropagation(Compilation compilation)
    {
        _compilation = compilation;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (GetLocalMethod(invocation, model) is not { } method || GetHelperInfo(method) is not { } helper)
                {
                    continue;
                }

                // Suppress the implementation-side observation. The call-site
                // observation below is where the literal/const value is known.
                _forwardedNodes.Add(helper.Candidate.Node);
            }
        }
    }

    public bool ShouldSkip(SyntaxNode node) => _forwardedNodes.Contains(node);

    public bool TryResolveInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel callerModel,
        out PropagatedAccess access)
    {
        access = default!;
        if (GetLocalMethod(invocation, callerModel) is not { } method ||
            GetHelperInfo(method) is not { } helper ||
            !TryGetArgument(invocation, helper.KeyParameter, callerModel, out var argument))
        {
            return false;
        }

        var rootResolution = ResolveCallSiteArgument(argument, callerModel);
        var resolved = ResolvePath(method, rootResolution, 1, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
        access = new PropagatedAccess(resolved.Kind, resolved.Resolution);
        return true;
    }

    private PropagatedResolution ResolvePath(
        IMethodSymbol method,
        StringResolution resolution,
        int hops,
        HashSet<IMethodSymbol> visiting)
    {
        if (!visiting.Add(method) || GetHelperInfo(method) is not { } helper)
        {
            return new PropagatedResolution("indexer", new StringResolution(null, "dynamic-helper-unresolvable"));
        }

        try
        {
            if (helper.Candidate.Kind is not null)
            {
                return new PropagatedResolution(
                    helper.Candidate.Kind,
                    resolution.IsStatic
                        ? new StringResolution(resolution.Value, "static-helper-propagation")
                        : new StringResolution(null, resolution.Kind));
            }

            if (hops >= MaximumHelperHops || helper.Candidate.Callee is not { } callee ||
                GetHelperInfo(callee) is not { } calleeInfo ||
                !TryGetArgument(helper.Candidate.Invocation!, calleeInfo.KeyParameter, GetSemanticModel(helper.Candidate.Invocation!), out var nestedArgument) ||
                !IsExactParameter(nestedArgument, GetSemanticModel(helper.Candidate.Invocation!), helper.KeyParameter))
            {
                return new PropagatedResolution(
                    FindTerminalKind(helper, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)),
                    new StringResolution(null, "dynamic-helper-depth"));
            }

            return ResolvePath(callee, resolution, hops + 1, visiting);
        }
        finally
        {
            visiting.Remove(method);
        }
    }

    private string FindTerminalKind(HelperInfo helper, HashSet<IMethodSymbol> visiting)
    {
        if (helper.Candidate.Kind is not null)
        {
            return helper.Candidate.Kind;
        }

        if (helper.Candidate.Callee is not { } callee || !visiting.Add(callee) || GetHelperInfo(callee) is not { } next)
        {
            return "indexer";
        }

        return FindTerminalKind(next, visiting);
    }

    private HelperInfo? GetHelperInfo(IMethodSymbol method)
    {
        if (_helperCache.TryGetValue(method, out var cached))
        {
            return cached;
        }

        // Break cycles before inspecting nested helper calls.
        _helperCache[method] = null;
        if (!IsLocalMethod(method) || method.DeclaringSyntaxReferences.Length != 1 ||
            method.DeclaringSyntaxReferences[0].GetSyntax() is not SyntaxNode declaration)
        {
            return null;
        }

        var model = GetSemanticModel(declaration);
        var candidates = new List<ForwardingCandidate>();
        foreach (var elementAccess in declaration.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
        {
            if (!IsEnclosedBy(elementAccess, method, model) ||
                elementAccess.ArgumentList.Arguments.FirstOrDefault()?.Expression is not { } argument ||
                !IsConfigurationType(model.GetTypeInfo(elementAccess.Expression).Type) ||
                !TryGetStringParameter(argument, model, out var parameter))
            {
                continue;
            }

            candidates.Add(ForwardingCandidate.Direct(elementAccess, "indexer", parameter));
        }

        foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!IsEnclosedBy(invocation, method, model) ||
                GetLocalMethod(invocation, model) is not { } callee ||
                invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not { } argument ||
                !TryGetStringParameter(argument, model, out var parameter))
            {
                continue;
            }

            candidates.Add(ForwardingCandidate.Nested(invocation, callee, parameter));
        }

        foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!IsEnclosedBy(invocation, method, model) ||
                invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name.Identifier.Text is not ("GetValue" or "GetSection" or "GetRequiredSection" or "BindConfiguration") ||
                GetLocalMethod(invocation, model) is not null)
            {
                continue;
            }

            var receiver = memberAccess.Expression;
            var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            var isSupported = memberAccess.Name.Identifier.Text is "GetValue" or "GetSection" or "GetRequiredSection"
                ? IsConfigurationType(model.GetTypeInfo(receiver).Type) && !IsReceiverOfKnownConsumer(invocation, model)
                : symbol is not null && IsSupportedBindConfiguration(symbol, model.GetTypeInfo(receiver).Type);
            if (!isSupported || invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not { } argument ||
                !TryGetStringParameter(argument, model, out var parameter))
            {
                continue;
            }

            candidates.Add(ForwardingCandidate.Direct(
                invocation,
                memberAccess.Name.Identifier.Text switch
                {
                    "GetValue" => "get-value",
                    "GetSection" => "section",
                    "GetRequiredSection" => "required-section",
                    _ => "options-bind-configuration"
                },
                parameter));
        }

        var distinctCandidates = candidates
            .GroupBy(candidate => candidate.Node, ReferenceEqualityComparer.Instance)
            .Select(group => group.First())
            .ToArray();
        if (distinctCandidates.Length != 1 || HasParameterReassignment(distinctCandidates[0].KeyParameter, declaration, model) ||
            HasKeyDependentBranch(distinctCandidates[0], declaration, model))
        {
            return null;
        }

        var helper = new HelperInfo(method, distinctCandidates[0].KeyParameter, distinctCandidates[0]);
        _helperCache[method] = helper;
        return helper;
    }

    private SemanticModel GetSemanticModel(SyntaxNode node) => _compilation.GetSemanticModel(node.SyntaxTree);

    private static StringResolution ResolveCallSiteArgument(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression is LiteralExpressionSyntax && model.GetConstantValue(expression) is { HasValue: true, Value: string literal })
        {
            return new StringResolution(literal, "static-helper-literal");
        }

        if (expression is IdentifierNameSyntax identifier &&
            model.GetSymbolInfo(identifier).Symbol is IFieldSymbol { IsConst: true, ConstantValue: string fieldValue })
        {
            return new StringResolution(fieldValue, "static-helper-const");
        }

        if (expression is IdentifierNameSyntax localIdentifier &&
            model.GetSymbolInfo(localIdentifier).Symbol is ILocalSymbol { IsConst: true, ConstantValue: string localValue })
        {
            return new StringResolution(localValue, "static-helper-const");
        }

        return new StringResolution(null, "dynamic-helper-argument");
    }

    private static bool TryGetStringParameter(ExpressionSyntax expression, SemanticModel model, out IParameterSymbol parameter)
    {
        parameter = null!;
        return expression is IdentifierNameSyntax identifier &&
            model.GetSymbolInfo(identifier).Symbol is IParameterSymbol candidate &&
            candidate.Type.SpecialType == SpecialType.System_String &&
            (parameter = candidate) is not null;
    }

    private static bool IsExactParameter(ExpressionSyntax expression, SemanticModel model, IParameterSymbol parameter) =>
        expression is IdentifierNameSyntax identifier &&
        model.GetSymbolInfo(identifier).Symbol is IParameterSymbol candidate &&
        SymbolEqualityComparer.Default.Equals(candidate, parameter);

    private static bool TryGetArgument(
        InvocationExpressionSyntax invocation,
        IParameterSymbol parameter,
        SemanticModel model,
        out ExpressionSyntax argument)
    {
        var parameterIndex = parameter.Ordinal;
        var arguments = invocation.ArgumentList.Arguments;
        var named = arguments.FirstOrDefault(item => item.NameColon?.Name.Identifier.ValueText == parameter.Name);
        if (named is not null)
        {
            argument = named.Expression;
            return true;
        }

        if (parameterIndex < arguments.Count)
        {
            argument = arguments[parameterIndex].Expression;
            return true;
        }

        argument = null!;
        return false;
    }

    private static bool IsEnclosedBy(SyntaxNode node, IMethodSymbol method, SemanticModel model) =>
        model.GetEnclosingSymbol(node.SpanStart) is IMethodSymbol enclosing &&
        SymbolEqualityComparer.Default.Equals(enclosing, method);

    private static bool HasParameterReassignment(IParameterSymbol parameter, SyntaxNode declaration, SemanticModel model)
    {
        foreach (var identifier in declaration.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (model.GetSymbolInfo(identifier).Symbol is not IParameterSymbol referenced ||
                !SymbolEqualityComparer.Default.Equals(referenced, parameter))
            {
                continue;
            }

            if (identifier.Parent is AssignmentExpressionSyntax assignment && assignment.Left.Span.Contains(identifier.Span) ||
                identifier.Parent is PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax ||
                identifier.Parent is ArgumentSyntax argument && argument.RefKindKeyword.RawKind != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasKeyDependentBranch(ForwardingCandidate candidate, SyntaxNode declaration, SemanticModel model)
    {
        foreach (var condition in declaration.DescendantNodes().Where(node => node is IfStatementSyntax or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or ConditionalExpressionSyntax or SwitchStatementSyntax or SwitchExpressionSyntax))
        {
            var expression = condition switch
            {
                IfStatementSyntax item => item.Condition,
                WhileStatementSyntax item => item.Condition,
                DoStatementSyntax item => item.Condition,
                ForStatementSyntax item => item.Condition,
                ConditionalExpressionSyntax item => item.Condition,
                SwitchStatementSyntax item => item.Expression,
                SwitchExpressionSyntax item => item.GoverningExpression,
                _ => null
            };
            if (expression is null)
            {
                continue;
            }

            foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                if (model.GetSymbolInfo(identifier).Symbol is IParameterSymbol parameter &&
                    SymbolEqualityComparer.Default.Equals(parameter, candidate.KeyParameter) &&
                    !candidate.Node.Span.Contains(identifier.Span))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private IMethodSymbol? GetLocalMethod(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method || !IsLocalMethod(method))
        {
            return null;
        }

        return method;
    }

    private bool IsLocalMethod(IMethodSymbol method) =>
        (method.MethodKind is MethodKind.Ordinary or MethodKind.LocalFunction) &&
        method.DeclaringSyntaxReferences.Length == 1 &&
        SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, _compilation.Assembly) &&
        !method.IsVirtual && !method.IsOverride && !method.IsAbstract &&
        method.ContainingType?.TypeKind != TypeKind.Interface;

    private static bool IsConfigurationType(ITypeSymbol? type) =>
        type is not null &&
        (type.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfiguration" ||
         type.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfigurationSection" ||
         type.AllInterfaces.Any(item => item.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfiguration"));

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

    private static bool IsReceiverOfKnownConsumer(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (invocation.Parent is not MemberAccessExpressionSyntax memberAccess || memberAccess.Expression != invocation ||
            memberAccess.Parent is not InvocationExpressionSyntax outer)
        {
            return false;
        }

        var symbol = model.GetSymbolInfo(outer).Symbol as IMethodSymbol;
        return symbol?.Name is "Bind" or "GetChildren";
    }

    internal sealed record PropagatedAccess(string Kind, StringResolution Resolution);

    private sealed record PropagatedResolution(string Kind, StringResolution Resolution);

    private sealed record HelperInfo(IMethodSymbol Method, IParameterSymbol KeyParameter, ForwardingCandidate Candidate);

    private sealed record ForwardingCandidate(
        SyntaxNode Node,
        string? Kind,
        IParameterSymbol KeyParameter,
        IMethodSymbol? Callee,
        InvocationExpressionSyntax? Invocation)
    {
        public static ForwardingCandidate Direct(SyntaxNode node, string kind, IParameterSymbol parameter) =>
            new(node, kind, parameter, null, null);

        public static ForwardingCandidate Nested(InvocationExpressionSyntax invocation, IMethodSymbol callee, IParameterSymbol parameter) =>
            new(invocation, null, parameter, callee, invocation);
    }
}
