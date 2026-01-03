using System;
using System.IO;
using Python.Runtime;

namespace AegisQuant.UI.Services;

/// <summary>
/// Service for managing Python runtime initialization and lifecycle.
/// </summary>
public class PythonRuntimeService : IDisposable
{
    private static PythonRuntimeService? _instance;
    private static readonly object _lock = new();
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static PythonRuntimeService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new PythonRuntimeService();
                }
            }
            return _instance;
        }
    }

    private PythonRuntimeService() { }

    /// <summary>
    /// Gets whether Python runtime is initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Initializes the Python runtime.
    /// </summary>
    /// <param name="pythonHome">Optional Python home directory</param>
    /// <param name="pythonDll">Optional path to Python DLL</param>
    public void Initialize(string? pythonHome = null, string? pythonDll = null)
    {
        if (_initialized)
            return;

        lock (_lock)
        {
            if (_initialized)
                return;

            try
            {
                // Set Python home if provided
                if (!string.IsNullOrEmpty(pythonHome))
                {
                    Environment.SetEnvironmentVariable("PYTHONHOME", pythonHome);
                }

                // Set Python DLL path if provided
                if (!string.IsNullOrEmpty(pythonDll))
                {
                    Runtime.PythonDLL = pythonDll;
                }

                // Initialize Python engine
                PythonEngine.Initialize();
                _initialized = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize Python runtime: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Adds a path to Python's sys.path.
    /// </summary>
    /// <param name="path">Path to add</param>
    public void AddToPath(string path)
    {
        EnsureInitialized();

        using (Py.GIL())
        {
            dynamic sys = Py.Import("sys");
            sys.path.append(path);
        }
    }

    /// <summary>
    /// Executes Python code and returns the result.
    /// </summary>
    /// <param name="code">Python code to execute</param>
    public void Execute(string code)
    {
        EnsureInitialized();

        using (Py.GIL())
        {
            PythonEngine.Exec(code);
        }
    }

    /// <summary>
    /// Imports a Python module.
    /// </summary>
    /// <param name="moduleName">Module name</param>
    /// <returns>Imported module</returns>
    public PyObject ImportModule(string moduleName)
    {
        EnsureInitialized();

        using (Py.GIL())
        {
            return Py.Import(moduleName);
        }
    }

    /// <summary>
    /// Loads a Python script from file.
    /// </summary>
    /// <param name="filePath">Path to Python file</param>
    /// <returns>Module scope</returns>
    public PyObject LoadScript(string filePath)
    {
        EnsureInitialized();

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Python script not found: {filePath}");

        var code = File.ReadAllText(filePath);
        var moduleName = Path.GetFileNameWithoutExtension(filePath);

        using (Py.GIL())
        {
            // Create a new module scope
            var scope = Py.CreateScope(moduleName);
            scope.Exec(code);
            return scope;
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Python runtime is not initialized. Call Initialize() first.");
        }
    }

    /// <summary>
    /// Shuts down the Python runtime.
    /// </summary>
    public void Shutdown()
    {
        if (_initialized)
        {
            lock (_lock)
            {
                if (_initialized)
                {
                    PythonEngine.Shutdown();
                    _initialized = false;
                }
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Shutdown();
            _disposed = true;
        }
    }
}
