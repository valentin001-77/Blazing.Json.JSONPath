using System.Collections.Immutable;
using Blazing.Json.JSONPath.Exceptions;
using Blazing.Json.JSONPath.Functions;
using Blazing.Json.JSONPath.Lexer;
using Blazing.Json.JSONPath.Parser.Nodes;
using Blazing.Json.JSONPath.Utilities;
using ParserFunctionArgument = Blazing.Json.JSONPath.Parser.Nodes.FunctionArgument;

namespace Blazing.Json.JSONPath.Parser;

/// <summary>
/// Recursive descent parser for JSONPath queries following RFC 9535 ABNF grammar.
/// Converts a sequence of tokens into an Abstract Syntax Tree (AST).
/// </summary>
public sealed class JsonPathParser
{
    /// <summary>
    /// Parses a JSONPath query string into an AST.
    /// </summary>
    /// <param name="query">The JSONPath query string to parse.</param>
    /// <returns>The parsed query as an AST.</returns>
    /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
    /// <exception cref="JsonPathSyntaxException">Thrown when the query has invalid syntax.</exception>
    public static JsonPathQuery Parse(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tokens = JsonPathLexer.Tokenize(query);
        return Parse(tokens);
    }

    /// <summary>
    /// Parses a sequence of tokens into an AST.
    /// </summary>
    /// <param name="tokens">The tokens to parse.</param>
    /// <returns>The parsed query as an AST.</returns>
    /// <exception cref="JsonPathSyntaxException">Thrown when the tokens represent invalid syntax.</exception>
    public static JsonPathQuery Parse(IReadOnlyList<JsonPathToken> tokens)
    {
        var context = new ParserContext(tokens);
        return ParseQuery(context);
    }

    /// <summary>
    /// Parses a complete JSONPath query.
    /// Grammar: jsonpath-query = root-identifier segments
    /// </summary>
    private static JsonPathQuery ParseQuery(ParserContext context)
    {
        // Consume root identifier ($)
        context.Consume(TokenType.RootIdentifier, "JSONPath query must start with '$'");

        // Parse segments
        var segments = ParseSegments(context);

        // Ensure we've consumed all tokens
        if (!context.IsAtEnd)
        {
            throw context.Error($"Unexpected token '{context.Current.Type}' after query");
        }

        return new JsonPathQuery(segments.ToImmutableArray());
    }

    /// <summary>
    /// Parses a sequence of segments.
    /// Grammar: segments = *( S segment )
    /// </summary>
    private static List<SegmentNode> ParseSegments(ParserContext context)
    {
        var segments = new List<SegmentNode>();

        while (!context.IsAtEnd)
        {
            // Check for segment start
            if (context.Check(TokenType.Dot, TokenType.DoubleDot, TokenType.LeftBracket))
            {
                segments.Add(ParseSegment(context));
            }
            else
            {
                // No more segments
                break;
            }
        }

        return segments;
    }

    /// <summary>
    /// Parses a single segment (child or descendant).
    /// Grammar: segment = child-segment / descendant-segment
    /// </summary>
    private static SegmentNode ParseSegment(ParserContext context)
    {
        if (context.Match(TokenType.DoubleDot))
        {
            // Descendant segment
            return ParseDescendantSegment(context);
        }
        else
        {
            // Child segment
            return ParseChildSegment(context);
        }
    }

    /// <summary>
    /// Parses a child segment.
    /// Grammar: child-segment = bracketed-selection / ("." (wildcard-selector / member-name-shorthand))
    /// </summary>
    private static SegmentNode ParseChildSegment(ParserContext context)
    {
        if (context.Check(TokenType.LeftBracket))
        {
            // Bracketed selection: [selector1, selector2, ...]
            return ParseBracketedSelection(context, isDescendant: false);
        }
        else if (context.Match(TokenType.Dot))
        {
            // Dot notation
            if (context.Check(TokenType.Wildcard))
            {
                context.Advance();
                return ChildSegment.Create(WildcardSelector.Instance);
            }
            else if (context.Check(TokenType.MemberName))
            {
                var name = context.Advance().Value;
                return ChildSegment.Create(new NameSelector(name));
            }
            else
            {
                throw context.Error("Expected member name or wildcard after '.'");
            }
        }
        else
        {
            throw context.Error("Expected '.' or '[' for child segment");
        }
    }

    /// <summary>
    /// Parses a descendant segment.
    /// Grammar: descendant-segment = ".." (bracketed-selection / wildcard-selector / member-name-shorthand)
    /// </summary>
    private static SegmentNode ParseDescendantSegment(ParserContext context)
    {
        if (context.Check(TokenType.LeftBracket))
        {
            // Bracketed selection: ..[selector1, selector2, ...]
            return ParseBracketedSelection(context, isDescendant: true);
        }
        else if (context.Check(TokenType.Wildcard))
        {
            context.Advance();
            return DescendantSegment.Create(WildcardSelector.Instance);
        }
        else if (context.Check(TokenType.MemberName))
        {
            var name = context.Advance().Value;
            return DescendantSegment.Create(new NameSelector(name));
        }
        else
        {
            throw context.Error("Expected member name, wildcard, or '[' after '..'");
        }
    }

    /// <summary>
    /// Parses a bracketed selection.
    /// Grammar: bracketed-selection = "[" S selector *(S "," S selector) S "]"
    /// </summary>
    private static SegmentNode ParseBracketedSelection(ParserContext context, bool isDescendant)
    {
        context.Consume(TokenType.LeftBracket, "Expected '['");

        var selectors = new List<SelectorNode>();

        // Parse first selector
        selectors.Add(ParseSelector(context));

        // Parse additional selectors separated by commas
        while (context.Match(TokenType.Comma))
        {
            selectors.Add(ParseSelector(context));
        }

        context.Consume(TokenType.RightBracket, "Expected ']' to close bracketed selection");

        return isDescendant
            ? DescendantSegment.Create(selectors)
            : ChildSegment.Create(selectors);
    }

    /// <summary>
    /// Parses a single selector.
    /// Grammar: selector = name-selector / wildcard-selector / slice-selector / index-selector / filter-selector
    /// </summary>
    private static SelectorNode ParseSelector(ParserContext context)
    {
        // Filter selector: ?expression
        if (context.Check(TokenType.Question))
        {
            return ParseFilterSelector(context);
        }

        // Wildcard: *
        if (context.Match(TokenType.Wildcard))
        {
            return WildcardSelector.Instance;
        }

        // String literal (name selector): 'name' or "name"
        if (context.Check(TokenType.StringLiteral))
        {
            var token = context.Advance();
            var name = StringEscaping.Unescape(token.Value);
            return new NameSelector(name);
        }

        // Integer or slice
        if (context.Check(TokenType.Integer))
        {
            return ParseIndexOrSlice(context);
        }

        // Slice starting with colon: :end or :end:step
        if (context.Check(TokenType.Colon))
        {
            return ParseSliceFromColon(context);
        }

        throw context.Error($"Unexpected token '{context.Current.Type}' in selector");
    }

    /// <summary>
    /// Parses an index or slice selector starting with an integer.
    /// </summary>
    private static SelectorNode ParseIndexOrSlice(ParserContext context)
    {
        var startToken = context.Consume(TokenType.Integer, "Expected integer");
        var start = int.Parse(startToken.Value);

        // Check if this is a slice
        if (context.Check(TokenType.Colon))
        {
            context.Advance(); // Consume ':'

            // Parse end (optional)
            int? end = null;
            if (context.Check(TokenType.Integer))
            {
                end = int.Parse(context.Advance().Value);
            }

            // Parse step (optional)
            int step = 1;
            if (context.Match(TokenType.Colon))
            {
                if (context.Check(TokenType.Integer))
                {
                    step = int.Parse(context.Advance().Value);
                }
            }

            return new SliceSelector(start, end, step);
        }

        // Just an index
        return new IndexSelector(start);
    }

    /// <summary>
    /// Parses a slice selector starting with a colon.
    /// Example: :5 or :5:2 or ::2
    /// </summary>
    private static SelectorNode ParseSliceFromColon(ParserContext context)
    {
        context.Consume(TokenType.Colon, "Expected ':'");

        // Parse end (optional)
        int? end = null;
        if (context.Check(TokenType.Integer))
        {
            end = int.Parse(context.Advance().Value);
        }

        // Parse step (optional)
        int step = 1;
        if (context.Match(TokenType.Colon))
        {
            if (context.Check(TokenType.Integer))
            {
                step = int.Parse(context.Advance().Value);
            }
        }

        return new SliceSelector(null, end, step);
    }

    /// <summary>
    /// Parses a filter selector.
    /// Grammar: filter-selector = "?" S logical-expr
    /// </summary>
    private static FilterSelector ParseFilterSelector(ParserContext context)
    {
        context.Consume(TokenType.Question, "Expected '?'");

        var expression = ParseLogicalExpression(context);

        return new FilterSelector(expression);
    }

    /// <summary>
    /// Parses a logical expression (handles || operator).
    /// </summary>
    private static FilterExpressionNode ParseLogicalExpression(ParserContext context)
    {
        var left = ParseLogicalAndExpression(context);

        while (context.Match(TokenType.Or))
        {
            var right = ParseLogicalAndExpression(context);
            left = new LogicalOrExpression(left, right);
        }

        return left;
    }

    /// <summary>
    /// Parses a logical AND expression (handles &amp;&amp; operator).
    /// </summary>
    private static FilterExpressionNode ParseLogicalAndExpression(ParserContext context)
    {
        var left = ParseLogicalNotExpression(context);

        while (context.Match(TokenType.And))
        {
            var right = ParseLogicalNotExpression(context);
            left = new LogicalAndExpression(left, right);
        }

        return left;
    }

    /// <summary>
    /// Parses a logical NOT expression (handles ! operator).
    /// </summary>
    private static FilterExpressionNode ParseLogicalNotExpression(ParserContext context)
    {
        if (context.Match(TokenType.Not))
        {
            var operand = ParseLogicalNotExpression(context); // Right-associative
            return new LogicalNotExpression(operand);
        }

        return ParseComparisonOrExistence(context);
    }

    /// <summary>
    /// Parses a comparison expression or existence test or parenthesized expression.
    /// RFC 9535 Section 2.3.5.1: test-expr can be a function-expr returning LogicalType or NodesType.
    /// </summary>
    private static FilterExpressionNode ParseComparisonOrExistence(ParserContext context)
    {
        // Parenthesized expression
        if (context.Match(TokenType.LeftParen))
        {
            var expr = ParseLogicalExpression(context);
            context.Consume(TokenType.RightParen, "Expected ')' after expression");
            return expr;
        }

        // Parse left side (could be a query, function, or literal)
        var left = ParseComparable(context);

        // Check for comparison operator
        if (TryParseComparisonOperator(context, out var op))
        {
            var right = ParseComparable(context);
            return new ComparisonExpression(left, op, right);
        }

        // RFC 9535 Section 2.3.5.1: Functions returning LogicalType or NodesType can be test expressions
        // If it's a function call, check if it returns LogicalType or NodesType
        if (left is FunctionCallNode funcCall)
        {
            var function = FunctionRegistry.Default.GetFunction(funcCall.Function.FunctionName);
            
            // LogicalType and NodesType functions can be used as test expressions without comparison
            if (function.ResultType == FunctionType.LogicalType || function.ResultType == FunctionType.NodesType)
            {
                return funcCall.Function;
            }
            
            // ValueType functions require a comparison operator
            throw context.Error($"Function '{funcCall.Function.FunctionName}' returns ValueType and requires a comparison operator");
        }

        // If it's a query and no comparison operator, it's an existence test
        if (left is QueryExpression query)
        {
            return new ExistenceTest(query);
        }

        throw context.Error("Expected comparison operator or existence test");
    }

    /// <summary>
    /// Parses a comparable value (literal, query, or function).
    /// </summary>
    private static ComparableNode ParseComparable(ParserContext context)
    {
        // Function call
        if (context.Check(TokenType.FunctionName))
        {
            return new FunctionCallNode(ParseFunctionExpression(context));
        }

        // Query (@ or $)
        if (context.Check(TokenType.CurrentIdentifier, TokenType.RootIdentifier))
        {
            return ParseQueryExpression(context);
        }

        // Literals
        if (context.Check(TokenType.StringLiteral))
        {
            var token = context.Advance();
            var value = StringEscaping.Unescape(token.Value);
            return new LiteralNode(value, TokenType.StringLiteral);
        }

        if (context.Check(TokenType.Integer))
        {
            var token = context.Advance();
            // Handle both integers and floating-point numbers
            if (token.Value.Contains('.') || token.Value.Contains('e') || token.Value.Contains('E'))
            {
                var value = double.Parse(token.Value, System.Globalization.CultureInfo.InvariantCulture);
                return new LiteralNode(value, TokenType.Integer);
            }
            else
            {
                var value = int.Parse(token.Value);
                return new LiteralNode(value, TokenType.Integer);
            }
        }

        if (context.Match(TokenType.True))
        {
            return new LiteralNode(true, TokenType.True);
        }

        if (context.Match(TokenType.False))
        {
            return new LiteralNode(false, TokenType.False);
        }

        if (context.Match(TokenType.Null))
        {
            return new LiteralNode(null, TokenType.Null);
        }

        throw context.Error($"Expected comparable value, but found '{context.Current.Type}'");
    }

    /// <summary>
    /// Parses a query expression (@ or $ with segments).
    /// </summary>
    private static QueryExpression ParseQueryExpression(ParserContext context)
    {
        bool isRelative;

        if (context.Match(TokenType.CurrentIdentifier))
        {
            isRelative = true;
        }
        else if (context.Match(TokenType.RootIdentifier))
        {
            isRelative = false;
        }
        else
        {
            throw context.Error("Expected '@' or '$'");
        }

        var segments = ParseSegments(context);

        return new QueryExpression(isRelative, segments.ToImmutableArray());
    }

    /// <summary>
    /// Parses a function expression.
    /// </summary>
    private static FunctionExpression ParseFunctionExpression(ParserContext context)
    {
        var nameToken = context.Consume(TokenType.FunctionName, "Expected function name");
        var functionName = nameToken.Value;

        context.Consume(TokenType.LeftParen, "Expected '(' after function name");

        var arguments = new List<ParserFunctionArgument>();

        // Parse arguments
        if (!context.Check(TokenType.RightParen))
        {
            arguments.Add(ParseFunctionArgument(context));

            while (context.Match(TokenType.Comma))
            {
                arguments.Add(ParseFunctionArgument(context));
            }
        }

        context.Consume(TokenType.RightParen, "Expected ')' after function arguments");

        return new FunctionExpression(functionName, arguments.ToArray());
    }

    /// <summary>
    /// Parses a function argument.
    /// </summary>
    private static ParserFunctionArgument ParseFunctionArgument(ParserContext context)
    {
        // Query argument
        if (context.Check(TokenType.CurrentIdentifier, TokenType.RootIdentifier))
        {
            return new QueryArgument(ParseQueryExpression(context));
        }

        // Literal argument
        var comparable = ParseComparable(context);
        if (comparable is LiteralNode literal)
        {
            return new LiteralArgument(literal.Value);
        }

        throw context.Error("Expected query or literal as function argument");
    }

    /// <summary>
    /// Tries to parse a comparison operator.
    /// </summary>
    private static bool TryParseComparisonOperator(ParserContext context, out ComparisonOperator op)
    {
        if (context.Match(TokenType.Equal))
        {
            op = ComparisonOperator.Equal;
            return true;
        }
        if (context.Match(TokenType.NotEqual))
        {
            op = ComparisonOperator.NotEqual;
            return true;
        }
        if (context.Match(TokenType.LessEqual))
        {
            op = ComparisonOperator.LessEqual;
            return true;
        }
        if (context.Match(TokenType.GreaterEqual))
        {
            op = ComparisonOperator.GreaterEqual;
            return true;
        }
        if (context.Match(TokenType.Less))
        {
            op = ComparisonOperator.Less;
            return true;
        }
        if (context.Match(TokenType.Greater))
        {
            op = ComparisonOperator.Greater;
            return true;
        }

        op = default;
        return false;
    }
}
