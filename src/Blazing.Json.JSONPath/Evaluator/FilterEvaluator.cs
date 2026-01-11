using System.Text.Json;
using Blazing.Json.JSONPath.Exceptions;
using Blazing.Json.JSONPath.Functions;
using Blazing.Json.JSONPath.Parser.Nodes;

namespace Blazing.Json.JSONPath.Evaluator;

/// <summary>
/// Evaluates filter expressions against JSON nodes.
/// Implements RFC 9535 filter expression semantics.
/// </summary>
public sealed class FilterEvaluator
{
    private readonly JsonPathEvaluator _queryEvaluator;
    private readonly FunctionRegistry _functionRegistry;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilterEvaluator"/> class.
    /// </summary>
    /// <param name="queryEvaluator">The evaluator used for executing queries within filter expressions.</param>
    /// <param name="functionRegistry">The function registry for function calls. If null, uses default registry.</param>
    public FilterEvaluator(JsonPathEvaluator queryEvaluator, FunctionRegistry? functionRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(queryEvaluator);
        _queryEvaluator = queryEvaluator;
        _functionRegistry = functionRegistry ?? FunctionRegistry.Default;
    }

    /// <summary>
    /// Evaluates a filter expression against a current node.
    /// </summary>
    /// <param name="expression">The filter expression to evaluate.</param>
    /// <param name="currentNode">The current node (@ in the expression).</param>
    /// <param name="rootNode">The root node ($ in the expression).</param>
    /// <returns>True if the node matches the filter, false otherwise.</returns>
    public bool Evaluate(FilterExpressionNode expression, JsonElement currentNode, JsonElement rootNode)
    {
        return expression switch
        {
            LogicalAndExpression and =>
                Evaluate(and.Left, currentNode, rootNode) &&
                Evaluate(and.Right, currentNode, rootNode),

            LogicalOrExpression or =>
                Evaluate(or.Left, currentNode, rootNode) ||
                Evaluate(or.Right, currentNode, rootNode),

            LogicalNotExpression not =>
                !Evaluate(not.Operand, currentNode, rootNode),

            ComparisonExpression comparison =>
                EvaluateComparison(comparison, currentNode, rootNode),

            ExistenceTest existence =>
                EvaluateExistence(existence, currentNode, rootNode),

            FunctionExpression function =>
                EvaluateFunction(function, currentNode, rootNode),

            _ => throw new NotSupportedException($"Unknown filter expression type: {expression.GetType().Name}")
        };
    }

    /// <summary>
    /// Evaluates a comparison expression.
    /// </summary>
    private bool EvaluateComparison(ComparisonExpression expression, JsonElement currentNode, JsonElement rootNode)
    {
        var leftValue = EvaluateComparable(expression.Left, currentNode, rootNode);
        var rightValue = EvaluateComparable(expression.Right, currentNode, rootNode);

        return ComparisonEngine.Compare(leftValue, expression.Operator, rightValue);
    }

    /// <summary>
    /// Evaluates an existence test (query without comparison).
    /// Returns true if the query produces a truthy result per RFC 9535 Section 2.3.5.3.
    /// </summary>
    private bool EvaluateExistence(ExistenceTest test, JsonElement currentNode, JsonElement rootNode)
    {
        var nodelist = EvaluateQuery(test.Query, currentNode, rootNode);
        
        // RFC 9535: Test expression evaluates to logical true if:
        // - Nodelist has exactly one node AND that node is truthy
        // Otherwise evaluates to logical false
        if (nodelist.Count != 1)
        {
            return false;
        }

        // Single node - check truthiness
        var value = nodelist[0].Value;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => false,
            JsonValueKind.Number => value.GetDouble() != 0,
            JsonValueKind.String => value.GetString()!.Length > 0,
            JsonValueKind.Array => value.GetArrayLength() > 0,
            JsonValueKind.Object => true, // Objects are truthy
            _ => false
        };
    }

    /// <summary>
    /// Evaluates a function expression and converts result to logical value.
    /// </summary>
    private bool EvaluateFunction(FunctionExpression function, JsonElement currentNode, JsonElement rootNode)
    {
        var result = EvaluateFunctionCall(function.FunctionName, function.Arguments, currentNode, rootNode);
        return result.ToLogical();
    }

    /// <summary>
    /// Evaluates a comparable node to produce a comparable value.
    /// </summary>
    private ComparableValue EvaluateComparable(ComparableNode node, JsonElement currentNode, JsonElement rootNode)
    {
        return node switch
        {
            LiteralNode literal => ComparableValue.FromLiteral(literal.Value),
            QueryExpression query => ComparableValue.FromNodelist(EvaluateQuery(query, currentNode, rootNode)),
            FunctionCallNode funcCall => EvaluateFunctionCallToComparable(funcCall.Function.FunctionName, funcCall.Function.Arguments, currentNode, rootNode),
            _ => throw new NotSupportedException($"Unknown comparable node type: {node.GetType().Name}")
        };
    }

    /// <summary>
    /// Evaluates a query expression within a filter context.
    /// </summary>
    private Nodelist EvaluateQuery(QueryExpression query, JsonElement currentNode, JsonElement rootNode)
    {
        // Determine starting point based on query type
        var startNode = query.IsRelative ? currentNode : rootNode;

        // If query has no segments, return just the start node
        if (query.IsEmpty)
        {
            return Nodelist.FromRoot(startNode);
        }

        // Build a temporary query for evaluation
        var tempQuery = new JsonPathQuery(query.Segments);

        // Evaluate the query
        return _queryEvaluator.Evaluate(tempQuery, startNode);
    }

    /// <summary>
    /// Evaluates a function call and returns a comparable value.
    /// </summary>
    private ComparableValue EvaluateFunctionCallToComparable(string functionName, IReadOnlyList<Parser.Nodes.FunctionArgument> arguments, JsonElement currentNode, JsonElement rootNode)
    {
        var result = EvaluateFunctionCall(functionName, arguments, currentNode, rootNode);

        // Convert function result to comparable value
        return result.Type switch
        {
            FunctionType.ValueType => result.IsNothing ? ComparableValue.Nothing : ComparableValue.FromValue(result.Value!.Value),
            FunctionType.NodesType => ComparableValue.FromNodelist(result.Nodes!),
            FunctionType.LogicalType => ComparableValue.FromLiteral(result.Logical!.Value),
            _ => ComparableValue.Nothing
        };
    }

    /// <summary>
    /// Evaluates a function call and returns a function result.
    /// </summary>
    private FunctionResult EvaluateFunctionCall(string functionName, IReadOnlyList<Parser.Nodes.FunctionArgument> arguments, JsonElement currentNode, JsonElement rootNode)
    {
        // Get the function from the registry
        var func = _functionRegistry.GetFunction(functionName);

        // Evaluate arguments
        var evaluatedArgs = EvaluateArguments(arguments, currentNode, rootNode);

        // Validate argument types and convert if needed
        FunctionRegistry.ValidateArguments(func, evaluatedArgs);
        
        // Convert arguments to match expected parameter types
        var convertedArgs = ConvertArgumentsToParameterTypes(func, evaluatedArgs);

        // Create evaluation context
        var context = new EvaluationContext(currentNode, rootNode);

        // Execute the function
        return func.Execute(convertedArgs, context);
    }

    /// <summary>
    /// Converts function arguments to match expected parameter types.
    /// Implements RFC 9535 Section 2.4.2 type conversions.
    /// </summary>
    private IReadOnlyList<Functions.FunctionArgument> ConvertArgumentsToParameterTypes(
        IFunctionExtension function,
        IReadOnlyList<Functions.FunctionArgument> arguments)
    {
        var convertedArgs = new List<Functions.FunctionArgument>(arguments.Count);

        for (int i = 0; i < arguments.Count; i++)
        {
            var expectedType = function.ParameterTypes[i];
            var arg = arguments[i];

            // Convert NodesType to ValueType if needed (RFC 2.4.2)
            if (expectedType == FunctionType.ValueType && arg is Functions.NodesArgument nodesArg)
            {
                // value() semantics: single node ? value, otherwise Nothing
                if (nodesArg.Nodes.Count == 1)
                {
                    convertedArgs.Add(new Functions.ValueArgument(nodesArg.Nodes[0].Value));
                }
                else
                {
                    convertedArgs.Add(new Functions.ValueArgument(null));
                }
            }
            // Convert NodesType to LogicalType if needed (RFC 2.4.2)
            else if (expectedType == FunctionType.LogicalType && arg is Functions.NodesArgument nodesArgForLogical)
            {
                // LogicalType conversion: non-empty nodelist ? true, empty ? false
                bool logicalValue = nodesArgForLogical.Nodes.Count > 0;
                convertedArgs.Add(new Functions.LogicalArgument(logicalValue));
            }
            else
            {
                // No conversion needed
                convertedArgs.Add(arg);
            }
        }

        return convertedArgs;
    }

    /// <summary>
    /// Evaluates function arguments.
    /// </summary>
    private IReadOnlyList<Functions.FunctionArgument> EvaluateArguments(
        IReadOnlyList<Parser.Nodes.FunctionArgument> arguments,
        JsonElement currentNode,
        JsonElement rootNode)
    {
        var evaluatedArgs = new List<Functions.FunctionArgument>(arguments.Count);

        foreach (var arg in arguments)
        {
            switch (arg)
            {
                case LiteralArgument literal:
                    evaluatedArgs.Add(new Functions.ValueArgument(ConvertLiteralToJsonElement(literal.Value)));
                    break;

                case QueryArgument query:
                    var nodelist = EvaluateQuery(query.Query, currentNode, rootNode);
                    // Query arguments produce NodesType
                    // The function's parameter type check will convert NodesType to ValueType if needed
                    evaluatedArgs.Add(new Functions.NodesArgument(nodelist));
                    break;

                default:
                    throw new JsonPathEvaluationException($"Unknown argument type: {arg.GetType().Name}");
            }
        }

        return evaluatedArgs;
    }

    /// <summary>
    /// Converts a literal value to a JsonElement.
    /// </summary>
    private static JsonElement? ConvertLiteralToJsonElement(object? value)
    {
        // Handle null by creating a JSON null element
        if (value is null)
        {
            return JsonDocument.Parse("null").RootElement;
        }

        // Serialize the value to JSON and parse it back to get a JsonElement
        var json = JsonSerializer.Serialize(value);
        return JsonDocument.Parse(json).RootElement;
    }
}
