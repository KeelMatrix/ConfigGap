using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeelMatrix.ConfigGap.Probe;

internal sealed record StringResolution(string? Value, string Kind, Location? PrimaryLocation = null)
{
    public bool IsStatic => Value is not null;
}

public static class KeyNormalizer
{
    public static string Normalize(string key) => key.Replace("__", ":", StringComparison.Ordinal);
}

internal static class KeyResolution
{
    public static StringResolution Resolve(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var constant = semanticModel.GetConstantValue(expression);
        if (constant.HasValue && constant.Value is string value)
        {
            var kind = expression switch
            {
                LiteralExpressionSyntax => "static-literal",
                IdentifierNameSyntax => "static-const",
                _ => "static-constant"
            };
            return new StringResolution(value, kind);
        }

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return Resolve(parenthesized.Expression, semanticModel);
        }

        if (expression is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } binary)
        {
            var left = Resolve(binary.Left, semanticModel);
            var right = Resolve(binary.Right, semanticModel);
            if (left.IsStatic && right.IsStatic)
            {
                return new StringResolution(left.Value + right.Value, "static-concatenation");
            }

            return new StringResolution(null, "dynamic-computed");
        }

        if (expression is InterpolatedStringExpressionSyntax interpolated)
        {
            var builder = new StringBuilder();
            foreach (var content in interpolated.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                {
                    builder.Append(text.TextToken.ValueText);
                    continue;
                }

                if (content is InterpolationSyntax interpolation)
                {
                    var part = Resolve(interpolation.Expression, semanticModel);
                    if (!part.IsStatic)
                    {
                        return new StringResolution(null, "dynamic-interpolation");
                    }

                    builder.Append(part.Value);
                }
            }

            return new StringResolution(builder.ToString(), "static-interpolation");
        }

        if (expression is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol local &&
            local.IsConst && local.ConstantValue is string localValue)
        {
            return new StringResolution(localValue, "static-const");
        }

        return new StringResolution(null, "dynamic-unresolvable");
    }
}
