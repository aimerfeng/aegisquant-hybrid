using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AegisQuant.UI.Strategy;

/// <summary>
/// Parser and evaluator for strategy condition expressions.
/// Supports: &lt;, &gt;, &lt;=, &gt;=, ==, !=, AND, OR, CROSS_ABOVE, CROSS_BELOW
/// </summary>
public class ConditionParser
{
    private readonly Dictionary<string, double> _variables = new();
    private readonly Dictionary<string, double> _previousValues = new();

    /// <summary>
    /// Sets a variable value for condition evaluation.
    /// </summary>
    public void SetVariable(string name, double value)
    {
        // Store previous value for crossover detection
        if (_variables.TryGetValue(name, out var prev))
        {
            _previousValues[name] = prev;
        }
        _variables[name] = value;
    }

    /// <summary>
    /// Sets multiple variables at once.
    /// </summary>
    public void SetVariables(Dictionary<string, double> variables)
    {
        foreach (var kvp in variables)
        {
            SetVariable(kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// Clears all variables.
    /// </summary>
    public void Clear()
    {
        _previousValues.Clear();
        foreach (var kvp in _variables)
        {
            _previousValues[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Evaluates a condition expression.
    /// </summary>
    /// <param name="expression">Condition expression (e.g., "$rsi &lt; 30 AND $ma_short &gt; $ma_long")</param>
    /// <returns>True if condition is met, false otherwise</returns>
    public bool Evaluate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        try
        {
            return EvaluateExpression(expression.Trim());
        }
        catch
        {
            return false;
        }
    }

    private bool EvaluateExpression(string expr)
    {
        // Handle OR (lowest precedence)
        var orParts = SplitByOperator(expr, " OR ");
        if (orParts.Count > 1)
        {
            foreach (var part in orParts)
            {
                if (EvaluateExpression(part.Trim()))
                    return true;
            }
            return false;
        }

        // Handle AND
        var andParts = SplitByOperator(expr, " AND ");
        if (andParts.Count > 1)
        {
            foreach (var part in andParts)
            {
                if (!EvaluateExpression(part.Trim()))
                    return false;
            }
            return true;
        }

        // Handle parentheses
        if (expr.StartsWith("(") && expr.EndsWith(")"))
        {
            return EvaluateExpression(expr.Substring(1, expr.Length - 2));
        }

        // Handle CROSS_ABOVE
        var crossAboveMatch = Regex.Match(expr, @"CROSS_ABOVE\s*\(\s*(.+?)\s*,\s*(.+?)\s*\)");
        if (crossAboveMatch.Success)
        {
            var left = ResolveValue(crossAboveMatch.Groups[1].Value.Trim());
            var right = ResolveValue(crossAboveMatch.Groups[2].Value.Trim());
            var prevLeft = GetPreviousValue(crossAboveMatch.Groups[1].Value.Trim());
            var prevRight = GetPreviousValue(crossAboveMatch.Groups[2].Value.Trim());

            if (prevLeft.HasValue && prevRight.HasValue)
            {
                return prevLeft.Value <= prevRight.Value && left > right;
            }
            return false;
        }

        // Handle CROSS_BELOW
        var crossBelowMatch = Regex.Match(expr, @"CROSS_BELOW\s*\(\s*(.+?)\s*,\s*(.+?)\s*\)");
        if (crossBelowMatch.Success)
        {
            var left = ResolveValue(crossBelowMatch.Groups[1].Value.Trim());
            var right = ResolveValue(crossBelowMatch.Groups[2].Value.Trim());
            var prevLeft = GetPreviousValue(crossBelowMatch.Groups[1].Value.Trim());
            var prevRight = GetPreviousValue(crossBelowMatch.Groups[2].Value.Trim());

            if (prevLeft.HasValue && prevRight.HasValue)
            {
                return prevLeft.Value >= prevRight.Value && left < right;
            }
            return false;
        }

        // Handle comparison operators
        return EvaluateComparison(expr);
    }

    private bool EvaluateComparison(string expr)
    {
        // Order matters: check longer operators first
        string[] operators = { "<=", ">=", "!=", "==", "<", ">" };

        foreach (var op in operators)
        {
            var idx = expr.IndexOf(op);
            if (idx > 0)
            {
                var left = ResolveValue(expr.Substring(0, idx).Trim());
                var right = ResolveValue(expr.Substring(idx + op.Length).Trim());

                return op switch
                {
                    "<" => left < right,
                    ">" => left > right,
                    "<=" => left <= right,
                    ">=" => left >= right,
                    "==" => Math.Abs(left - right) < 0.0001,
                    "!=" => Math.Abs(left - right) >= 0.0001,
                    _ => false
                };
            }
        }

        // If no operator found, try to evaluate as boolean (non-zero = true)
        var value = ResolveValue(expr);
        return Math.Abs(value) > 0.0001;
    }

    private double ResolveValue(string token)
    {
        token = token.Trim();

        // Variable reference ($name)
        if (token.StartsWith("$"))
        {
            var varName = token.Substring(1);
            if (_variables.TryGetValue(varName, out var value))
                return value;
            throw new ArgumentException($"Undefined variable: {token}");
        }

        // Numeric literal
        if (double.TryParse(token, out var numValue))
            return numValue;

        throw new ArgumentException($"Cannot resolve value: {token}");
    }

    private double? GetPreviousValue(string token)
    {
        token = token.Trim();

        if (token.StartsWith("$"))
        {
            var varName = token.Substring(1);
            if (_previousValues.TryGetValue(varName, out var value))
                return value;
        }

        // For numeric literals, previous value is the same
        if (double.TryParse(token, out var numValue))
            return numValue;

        return null;
    }

    private List<string> SplitByOperator(string expr, string op)
    {
        var result = new List<string>();
        int depth = 0;
        int lastSplit = 0;

        for (int i = 0; i < expr.Length; i++)
        {
            if (expr[i] == '(') depth++;
            else if (expr[i] == ')') depth--;
            else if (depth == 0 && i + op.Length <= expr.Length)
            {
                if (expr.Substring(i, op.Length).Equals(op, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(expr.Substring(lastSplit, i - lastSplit));
                    lastSplit = i + op.Length;
                    i += op.Length - 1;
                }
            }
        }

        result.Add(expr.Substring(lastSplit));
        return result;
    }

    /// <summary>
    /// Validates a condition expression syntax.
    /// </summary>
    /// <param name="expression">Expression to validate</param>
    /// <param name="availableVariables">List of available variable names</param>
    /// <returns>List of validation errors (empty if valid)</returns>
    public static List<string> ValidateSyntax(string expression, IEnumerable<string> availableVariables)
    {
        var errors = new List<string>();
        var varSet = new HashSet<string>(availableVariables);

        // Find all variable references
        var varMatches = Regex.Matches(expression, @"\$(\w+)");
        foreach (Match match in varMatches)
        {
            var varName = match.Groups[1].Value;
            if (!varSet.Contains(varName))
            {
                errors.Add($"Undefined variable: ${varName}");
            }
        }

        // Check for balanced parentheses
        int depth = 0;
        foreach (char c in expression)
        {
            if (c == '(') depth++;
            else if (c == ')') depth--;
            if (depth < 0)
            {
                errors.Add("Unbalanced parentheses: extra closing parenthesis");
                break;
            }
        }
        if (depth > 0)
        {
            errors.Add("Unbalanced parentheses: missing closing parenthesis");
        }

        return errors;
    }
}
