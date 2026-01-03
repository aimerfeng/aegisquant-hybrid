using System;
using System.IO;
using System.Text;

namespace AegisQuant.UI.Strategy;

/// <summary>
/// Generates strategy template files.
/// </summary>
public static class StrategyTemplateGenerator
{
    /// <summary>
    /// Generates a JSON strategy template.
    /// </summary>
    public static string GenerateJsonTemplate(string name = "my_strategy", string description = "My custom strategy")
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"$schema\": \"../schemas/strategy-schema.json\",");
        sb.AppendLine($"  \"name\": \"{name}\",");
        sb.AppendLine($"  \"description\": \"{description}\",");
        sb.AppendLine("  \"version\": \"1.0\",");
        sb.AppendLine("  \"parameters\": {");
        sb.AppendLine("    \"fast_period\": {");
        sb.AppendLine("      \"type\": \"int\",");
        sb.AppendLine("      \"default\": 5,");
        sb.AppendLine("      \"min\": 2,");
        sb.AppendLine("      \"max\": 50,");
        sb.AppendLine("      \"description\": \"Fast moving average period\"");
        sb.AppendLine("    },");
        sb.AppendLine("    \"slow_period\": {");
        sb.AppendLine("      \"type\": \"int\",");
        sb.AppendLine("      \"default\": 20,");
        sb.AppendLine("      \"min\": 5,");
        sb.AppendLine("      \"max\": 200,");
        sb.AppendLine("      \"description\": \"Slow moving average period\"");
        sb.AppendLine("    },");
        sb.AppendLine("    \"threshold\": {");
        sb.AppendLine("      \"type\": \"double\",");
        sb.AppendLine("      \"default\": 0.0,");
        sb.AppendLine("      \"min\": -1.0,");
        sb.AppendLine("      \"max\": 1.0,");
        sb.AppendLine("      \"description\": \"Signal threshold\"");
        sb.AppendLine("    }");
        sb.AppendLine("  },");
        sb.AppendLine("  \"indicators\": {");
        sb.AppendLine("    \"fast_ma\": {");
        sb.AppendLine("      \"type\": \"SMA\",");
        sb.AppendLine("      \"period\": \"$fast_period\"");
        sb.AppendLine("    },");
        sb.AppendLine("    \"slow_ma\": {");
        sb.AppendLine("      \"type\": \"SMA\",");
        sb.AppendLine("      \"period\": \"$slow_period\"");
        sb.AppendLine("    }");
        sb.AppendLine("  },");
        sb.AppendLine("  \"rules\": {");
        sb.AppendLine("    \"buy\": {");
        sb.AppendLine("      \"conditions\": [");
        sb.AppendLine("        {");
        sb.AppendLine("          \"left\": \"fast_ma\",");
        sb.AppendLine("          \"operator\": \"CROSS_ABOVE\",");
        sb.AppendLine("          \"right\": \"slow_ma\"");
        sb.AppendLine("        }");
        sb.AppendLine("      ]");
        sb.AppendLine("    },");
        sb.AppendLine("    \"sell\": {");
        sb.AppendLine("      \"conditions\": [");
        sb.AppendLine("        {");
        sb.AppendLine("          \"left\": \"fast_ma\",");
        sb.AppendLine("          \"operator\": \"CROSS_BELOW\",");
        sb.AppendLine("          \"right\": \"slow_ma\"");
        sb.AppendLine("        }");
        sb.AppendLine("      ]");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.Append("}");
        return sb.ToString();
    }

    /// <summary>
    /// Generates a Python strategy template.
    /// </summary>
    public static string GeneratePythonTemplate(string className = "MyStrategy", string description = "My custom strategy")
    {
        var sb = new StringBuilder();
        sb.AppendLine("\"\"\"");
        sb.AppendLine(description);
        sb.AppendLine();
        sb.AppendLine("This is a template for creating custom Python strategies.");
        sb.AppendLine("Implement the on_tick method to generate trading signals.");
        sb.AppendLine("\"\"\"");
        sb.AppendLine();
        sb.AppendLine("from aegisquant import Strategy, Signal, Context");
        sb.AppendLine();
        sb.AppendLine($"class {className}(Strategy):");
        sb.AppendLine("    \"\"\"");
        sb.AppendLine($"    {description}");
        sb.AppendLine("    \"\"\"");
        sb.AppendLine();
        sb.AppendLine("    def __init__(self):");
        sb.AppendLine("        super().__init__()");
        sb.AppendLine($"        self.name = \"{className}\"");
        sb.AppendLine($"        self.description = \"{description}\"");
        sb.AppendLine();
        sb.AppendLine("        # Define parameters with defaults");
        sb.AppendLine("        self.parameters = {");
        sb.AppendLine("            \"fast_period\": 5,");
        sb.AppendLine("            \"slow_period\": 20,");
        sb.AppendLine("            \"threshold\": 0.0");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        # Internal state");
        sb.AppendLine("        self._prev_fast_ma = None");
        sb.AppendLine("        self._prev_slow_ma = None");
        sb.AppendLine();
        sb.AppendLine("    def on_tick(self, ctx: Context) -> Signal:");
        sb.AppendLine("        \"\"\"");
        sb.AppendLine("        Called on each tick to generate a trading signal.");
        sb.AppendLine();
        sb.AppendLine("        Args:");
        sb.AppendLine("            ctx: Strategy context with market data and indicators");
        sb.AppendLine();
        sb.AppendLine("        Returns:");
        sb.AppendLine("            Signal.BUY, Signal.SELL, or Signal.NONE");
        sb.AppendLine("        \"\"\"");
        sb.AppendLine("        # Get parameters");
        sb.AppendLine("        fast_period = self.parameters[\"fast_period\"]");
        sb.AppendLine("        slow_period = self.parameters[\"slow_period\"]");
        sb.AppendLine();
        sb.AppendLine("        # Calculate indicators");
        sb.AppendLine("        fast_ma = ctx.indicators.sma(fast_period)");
        sb.AppendLine("        slow_ma = ctx.indicators.sma(slow_period)");
        sb.AppendLine();
        sb.AppendLine("        # Need both indicators to generate signals");
        sb.AppendLine("        if fast_ma is None or slow_ma is None:");
        sb.AppendLine("            return Signal.NONE");
        sb.AppendLine();
        sb.AppendLine("        # Check for crossover");
        sb.AppendLine("        signal = Signal.NONE");
        sb.AppendLine();
        sb.AppendLine("        if self._prev_fast_ma is not None and self._prev_slow_ma is not None:");
        sb.AppendLine("            # Buy on golden cross (fast crosses above slow)");
        sb.AppendLine("            if self._prev_fast_ma <= self._prev_slow_ma and fast_ma > slow_ma:");
        sb.AppendLine("                signal = Signal.BUY");
        sb.AppendLine();
        sb.AppendLine("            # Sell on death cross (fast crosses below slow)");
        sb.AppendLine("            elif self._prev_fast_ma >= self._prev_slow_ma and fast_ma < slow_ma:");
        sb.AppendLine("                signal = Signal.SELL");
        sb.AppendLine();
        sb.AppendLine("        # Update previous values");
        sb.AppendLine("        self._prev_fast_ma = fast_ma");
        sb.AppendLine("        self._prev_slow_ma = slow_ma");
        sb.AppendLine();
        sb.AppendLine("        return signal");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("# Export the strategy class");
        sb.Append($"strategy = {className}()");
        return sb.ToString();
    }

    /// <summary>
    /// Saves a JSON template to a file.
    /// </summary>
    public static void SaveJsonTemplate(string filePath, string name = "my_strategy", string description = "My custom strategy")
    {
        var content = GenerateJsonTemplate(name, description);
        File.WriteAllText(filePath, content, Encoding.UTF8);
    }

    /// <summary>
    /// Saves a Python template to a file.
    /// </summary>
    public static void SavePythonTemplate(string filePath, string className = "MyStrategy", string description = "My custom strategy")
    {
        var content = GeneratePythonTemplate(className, description);
        File.WriteAllText(filePath, content, Encoding.UTF8);
    }
}
