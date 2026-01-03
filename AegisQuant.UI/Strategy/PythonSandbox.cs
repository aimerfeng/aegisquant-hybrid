using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Python.Runtime;

namespace AegisQuant.UI.Strategy;

/// <summary>
/// Provides sandbox security for Python strategy execution.
/// </summary>
public class PythonSandbox
{
    /// <summary>
    /// Modules that are blocked for security reasons.
    /// </summary>
    public static readonly HashSet<string> BlockedModules = new(StringComparer.OrdinalIgnoreCase)
    {
        // System access
        "os", "sys", "subprocess", "shutil", "pathlib",
        
        // Network access
        "socket", "http", "urllib", "requests", "httplib", "ftplib",
        "smtplib", "poplib", "imaplib", "telnetlib", "ssl",
        
        // Process/Thread control
        "multiprocessing", "threading", "asyncio", "concurrent",
        "signal", "resource", "_thread",
        
        // File system
        "io", "tempfile", "glob", "fnmatch",
        
        // Code execution
        "importlib", "builtins", "__builtins__", "code", "codeop",
        "compile", "exec", "eval",
        
        // Low-level access
        "ctypes", "cffi", "ffi",
        
        // Platform specific
        "pty", "tty", "fcntl", "termios", "msvcrt", "winreg", "_winapi",
        "posix", "nt", "pwd", "grp", "spwd",
        
        // Pickle (can execute arbitrary code)
        "pickle", "cPickle", "shelve", "marshal",
        
        // Other dangerous modules
        "gc", "inspect", "traceback", "dis", "symtable",
        "ast", "parser", "token", "tokenize"
    };

    /// <summary>
    /// Built-in functions that are blocked.
    /// </summary>
    public static readonly HashSet<string> BlockedBuiltins = new()
    {
        "exec", "eval", "compile", "__import__", "open",
        "input", "breakpoint", "help", "license", "credits",
        "exit", "quit"
    };

    /// <summary>
    /// Default execution timeout in milliseconds.
    /// </summary>
    public const int DefaultTimeoutMs = 100;

    /// <summary>
    /// Validates Python code for security issues.
    /// </summary>
    /// <param name="code">Python code to validate</param>
    /// <returns>List of security violations</returns>
    public static List<SecurityViolation> ValidateCode(string code)
    {
        var violations = new List<SecurityViolation>();

        // Check for blocked module imports
        CheckBlockedImports(code, violations);

        // Check for blocked built-in functions
        CheckBlockedBuiltins(code, violations);

        // Check for attribute access to dangerous modules
        CheckDangerousAttributeAccess(code, violations);

        // Check for string-based code execution
        CheckStringExecution(code, violations);

        return violations;
    }

    private static void CheckBlockedImports(string code, List<SecurityViolation> violations)
    {
        // Match: import module, from module import, __import__('module')
        var patterns = new[]
        {
            @"(?:^|\n)\s*import\s+(\w+)",
            @"(?:^|\n)\s*from\s+(\w+)\s+import",
            @"__import__\s*\(\s*['""](\w+)['""]"
        };

        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(code, pattern);
            foreach (Match match in matches)
            {
                var moduleName = match.Groups[1].Value;
                if (BlockedModules.Contains(moduleName))
                {
                    violations.Add(new SecurityViolation
                    {
                        Type = ViolationType.BlockedModule,
                        Message = $"Import of '{moduleName}' is blocked for security reasons",
                        LineNumber = GetLineNumber(code, match.Index)
                    });
                }
            }
        }
    }

    private static void CheckBlockedBuiltins(string code, List<SecurityViolation> violations)
    {
        foreach (var builtin in BlockedBuiltins)
        {
            var pattern = $@"\b{Regex.Escape(builtin)}\s*\(";
            var matches = Regex.Matches(code, pattern);
            foreach (Match match in matches)
            {
                violations.Add(new SecurityViolation
                {
                    Type = ViolationType.BlockedBuiltin,
                    Message = $"Use of '{builtin}()' is blocked for security reasons",
                    LineNumber = GetLineNumber(code, match.Index)
                });
            }
        }
    }

    private static void CheckDangerousAttributeAccess(string code, List<SecurityViolation> violations)
    {
        // Check for accessing __class__, __bases__, __subclasses__, etc.
        var dangerousAttrs = new[] { "__class__", "__bases__", "__subclasses__", "__mro__", "__globals__", "__code__" };
        
        foreach (var attr in dangerousAttrs)
        {
            if (code.Contains(attr))
            {
                violations.Add(new SecurityViolation
                {
                    Type = ViolationType.DangerousAttribute,
                    Message = $"Access to '{attr}' is blocked for security reasons"
                });
            }
        }
    }

    private static void CheckStringExecution(string code, List<SecurityViolation> violations)
    {
        // Check for patterns that might execute strings as code
        var patterns = new[]
        {
            @"getattr\s*\([^)]*,\s*['""][^'""]*['""]",  // getattr with string
            @"setattr\s*\([^)]*,\s*['""][^'""]*['""]",  // setattr with string
        };

        // These are warnings, not hard blocks
        foreach (var pattern in patterns)
        {
            if (Regex.IsMatch(code, pattern))
            {
                // Just log, don't block - these have legitimate uses
            }
        }
    }

    /// <summary>
    /// Executes Python code with timeout protection.
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="action">Action to execute</param>
    /// <param name="timeoutMs">Timeout in milliseconds</param>
    /// <returns>Result of the action</returns>
    public static T ExecuteWithTimeout<T>(Func<T> action, int timeoutMs = DefaultTimeoutMs)
    {
        T result = default!;
        Exception? exception = null;
        var completed = false;

        var thread = new Thread(() =>
        {
            try
            {
                result = action();
                completed = true;
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(timeoutMs))
        {
            // Try to abort the thread (note: this is not always reliable)
            try
            {
                // Thread.Abort is not available in .NET Core
                // We rely on the thread being background and will be terminated when app exits
            }
            catch { }

            throw new TimeoutException($"Execution exceeded {timeoutMs}ms timeout");
        }

        if (exception != null)
        {
            throw exception;
        }

        if (!completed)
        {
            throw new TimeoutException($"Execution did not complete within {timeoutMs}ms");
        }

        return result;
    }

    /// <summary>
    /// Executes Python code with timeout protection (async version).
    /// </summary>
    public static async Task<T> ExecuteWithTimeoutAsync<T>(Func<T> action, int timeoutMs = DefaultTimeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        
        try
        {
            return await Task.Run(action, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Execution exceeded {timeoutMs}ms timeout");
        }
    }

    /// <summary>
    /// Sets up a restricted Python environment.
    /// </summary>
    public static void SetupRestrictedEnvironment()
    {
        using (Py.GIL())
        {
            // Remove dangerous modules from sys.modules
            dynamic sys = Py.Import("sys");
            
            foreach (var module in BlockedModules)
            {
                try
                {
                    if (sys.modules.Contains(module))
                    {
                        sys.modules.pop(module);
                    }
                }
                catch { }
            }

            // Restrict builtins
            dynamic builtins = Py.Import("builtins");
            foreach (var builtin in BlockedBuiltins)
            {
                try
                {
                    if (builtins.HasAttr(builtin))
                    {
                        builtins.SetAttr(builtin, null);
                    }
                }
                catch { }
            }
        }
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
}

/// <summary>
/// Represents a security violation in Python code.
/// </summary>
public class SecurityViolation
{
    public ViolationType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? LineNumber { get; set; }
}

/// <summary>
/// Types of security violations.
/// </summary>
public enum ViolationType
{
    BlockedModule,
    BlockedBuiltin,
    DangerousAttribute,
    StringExecution,
    Timeout
}
