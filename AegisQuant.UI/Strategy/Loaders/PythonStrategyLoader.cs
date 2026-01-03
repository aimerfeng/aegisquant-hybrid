using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using AegisQuant.UI.Services;
using AegisQuant.UI.Strategy.Models;
using Python.Runtime;

namespace AegisQuant.UI.Strategy.Loaders;

/// <summary>
/// Loads strategies from Python script files.
/// </summary>
public class PythonStrategyLoader
{
    private readonly PythonRuntimeService _pythonRuntime;
    private bool _initialized;

    /// <summary>
    /// Blocked modules for sandbox security.
    /// </summary>
    private static readonly HashSet<string> BlockedModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "os", "sys", "subprocess", "socket", "shutil", "pathlib",
        "importlib", "builtins", "__builtins__", "ctypes", "multiprocessing",
        "threading", "asyncio", "signal", "resource", "pty", "tty",
        "fcntl", "termios", "msvcrt", "winreg", "_winapi"
    };

    public PythonStrategyLoader()
    {
        _pythonRuntime = PythonRuntimeService.Instance;
    }

    /// <summary>
    /// Initializes the Python environment for strategy loading.
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
            return;

        try
        {
            _pythonRuntime.Initialize();
            
            // Add strategies directory to Python path
            var strategiesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "strategies");
            if (Directory.Exists(strategiesPath))
            {
                _pythonRuntime.AddToPath(strategiesPath);
            }

            _initialized = true;
        }
        catch (Exception ex)
        {
            throw new StrategyLoadException($"Failed to initialize Python runtime: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads a strategy from a Python file.
    /// </summary>
    /// <param name="filePath">Path to the Python file</param>
    /// <returns>Loaded strategy</returns>
    public IStrategy LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Strategy file not found: {filePath}");
        }

        var code = File.ReadAllText(filePath);
        return LoadFromCode(code, filePath);
    }

    /// <summary>
    /// Loads a strategy from Python code.
    /// </summary>
    /// <param name="code">Python code</param>
    /// <param name="sourcePath">Optional source path for error reporting</param>
    /// <returns>Loaded strategy</returns>
    public IStrategy LoadFromCode(string code, string? sourcePath = null)
    {
        // Validate code for security
        var validation = ValidateCode(code);
        if (!validation.IsValid)
        {
            throw new StrategyLoadException(
                $"Strategy validation failed: {string.Join("; ", validation.Errors.ConvertAll(e => e.Message))}");
        }

        Initialize();

        try
        {
            using (Py.GIL())
            {
                // Create isolated scope for the strategy
                var moduleName = sourcePath != null 
                    ? Path.GetFileNameWithoutExtension(sourcePath) 
                    : $"strategy_{Guid.NewGuid():N}";

                var scope = Py.CreateScope(moduleName);
                
                // Execute the strategy code
                scope.Exec(code);

                // Find the strategy class
                var strategyClass = FindStrategyClass(scope);
                if (strategyClass == null)
                {
                    throw new StrategyLoadException("No strategy class found. Define a class that inherits from Strategy.");
                }

                // Validate the strategy class has required methods
                ValidateStrategyClass(strategyClass);

                return new PythonScriptStrategy(scope, strategyClass, sourcePath);
            }
        }
        catch (PythonException ex)
        {
            var lineNumber = ExtractLineNumber(ex.Message);
            throw new StrategyLoadException($"Python error: {ex.Message}", lineNumber);
        }
        catch (StrategyLoadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new StrategyLoadException($"Failed to load Python strategy: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates Python code for security issues.
    /// </summary>
    public ValidationResult ValidateCode(string code)
    {
        var errors = new List<ValidationError>();

        // Check for blocked module imports
        var importPattern = @"(?:^|\n)\s*(?:import|from)\s+(\w+)";
        var matches = Regex.Matches(code, importPattern);
        
        foreach (Match match in matches)
        {
            var moduleName = match.Groups[1].Value;
            if (BlockedModules.Contains(moduleName))
            {
                errors.Add(new ValidationError
                {
                    Code = "BLOCKED_MODULE",
                    Message = $"Import of '{moduleName}' is not allowed for security reasons",
                    LineNumber = GetLineNumber(code, match.Index)
                });
            }
        }

        // Check for dangerous built-in functions
        var dangerousFunctions = new[] { "exec", "eval", "compile", "__import__", "open", "input" };
        foreach (var func in dangerousFunctions)
        {
            var funcPattern = $@"\b{func}\s*\(";
            if (Regex.IsMatch(code, funcPattern))
            {
                errors.Add(new ValidationError
                {
                    Code = "DANGEROUS_FUNCTION",
                    Message = $"Use of '{func}()' is not allowed for security reasons"
                });
            }
        }

        return errors.Count > 0
            ? new ValidationResult { IsValid = false, Errors = errors }
            : ValidationResult.Success();
    }

    /// <summary>
    /// Gets strategy info from a Python file without fully loading it.
    /// </summary>
    public StrategyInfo? GetStrategyInfo(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var code = File.ReadAllText(filePath);
        var name = Path.GetFileNameWithoutExtension(filePath);
        var description = ExtractDocstring(code) ?? "Python strategy";

        // Try to extract class name
        var classMatch = Regex.Match(code, @"class\s+(\w+)\s*\(.*Strategy.*\)");
        if (classMatch.Success)
        {
            name = classMatch.Groups[1].Value;
        }

        return new StrategyInfo
        {
            Name = name,
            Description = description,
            FilePath = filePath,
            Type = StrategyType.PythonScript
        };
    }

    private PyObject? FindStrategyClass(PyModule scope)
    {
        using (Py.GIL())
        {
            // Look for classes that inherit from Strategy
            foreach (PyObject nameObj in scope.Dir())
            {
                var name = nameObj.ToString();
                if (string.IsNullOrEmpty(name)) continue;
                
                var obj = scope.Get(name);
                if (obj == null) continue;

                // Check if it's a class
                if (!PyType.IsType(obj)) continue;

                // Check if it has on_tick method (duck typing)
                if (obj.HasAttr("on_tick"))
                {
                    return obj;
                }
            }
        }
        return null;
    }

    private void ValidateStrategyClass(PyObject strategyClass)
    {
        using (Py.GIL())
        {
            // Check for required on_tick method
            if (!strategyClass.HasAttr("on_tick"))
            {
                throw new StrategyLoadException("Strategy class must have an 'on_tick' method");
            }
        }
    }

    private static string? ExtractDocstring(string code)
    {
        // Try to extract module-level docstring
        var docstringMatch = Regex.Match(code, "^\\s*['\"]['\"]['\"](.+?)['\"]['\"]['\"]", RegexOptions.Singleline);
        if (docstringMatch.Success)
        {
            return docstringMatch.Groups[1].Value.Trim();
        }

        // Try to extract class docstring
        var classDocMatch = Regex.Match(code, "class\\s+\\w+.*?:\\s*['\"]['\"]['\"](.+?)['\"]['\"]['\"]", RegexOptions.Singleline);
        if (classDocMatch.Success)
        {
            return classDocMatch.Groups[1].Value.Trim();
        }

        return null;
    }

    private static int GetLineNumber(string code, int charIndex)
    {
        int line = 1;
        for (int i = 0; i < Math.Min(charIndex, code.Length); i++)
        {
            if (code[i] == '\n') line++;
        }
        return line;
    }

    private static int? ExtractLineNumber(string errorMessage)
    {
        var match = Regex.Match(errorMessage, @"line\s+(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var line))
        {
            return line;
        }
        return null;
    }
}
