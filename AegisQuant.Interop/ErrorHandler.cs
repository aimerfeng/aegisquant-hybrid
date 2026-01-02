namespace AegisQuant.Interop;

/// <summary>
/// Error codes returned by the Rust engine.
/// These match the constants defined in Rust ffi.rs.
/// </summary>
public static class ErrorCodes
{
    /// <summary>Operation completed successfully</summary>
    public const int Success = 0;
    /// <summary>Null pointer was passed to function</summary>
    public const int NullPointer = -1;
    /// <summary>Invalid parameter value</summary>
    public const int InvalidParam = -2;
    /// <summary>Engine not initialized</summary>
    public const int EngineNotInit = -3;
    /// <summary>Order rejected by risk manager</summary>
    public const int RiskRejected = -4;
    /// <summary>Failed to load data file</summary>
    public const int DataLoadFailed = -5;
    /// <summary>Invalid data (e.g., negative price)</summary>
    public const int InvalidData = -6;
    /// <summary>Insufficient capital for order</summary>
    public const int InsufficientCapital = -7;
    /// <summary>Order rate throttle exceeded</summary>
    public const int ThrottleExceeded = -8;
    /// <summary>Position limit exceeded</summary>
    public const int PositionLimit = -9;
    /// <summary>File not found</summary>
    public const int FileNotFound = -10;
    /// <summary>Internal panic (should not happen)</summary>
    public const int InternalPanic = -99;
}

/// <summary>
/// Handles error codes from the Rust engine and converts them to exceptions.
/// </summary>
public static class ErrorHandler
{
    /// <summary>
    /// Checks the result code and throws an appropriate exception if it indicates an error.
    /// </summary>
    /// <param name="errorCode">Error code from Rust function</param>
    /// <param name="operation">Name of the operation for error messages</param>
    /// <exception cref="ArgumentNullException">Thrown for null pointer errors</exception>
    /// <exception cref="ArgumentException">Thrown for invalid parameter errors</exception>
    /// <exception cref="InvalidOperationException">Thrown for engine not initialized errors</exception>
    /// <exception cref="RiskRejectedException">Thrown for risk rejection errors</exception>
    /// <exception cref="DataLoadException">Thrown for data loading errors</exception>
    /// <exception cref="InvalidDataException">Thrown for invalid data errors</exception>
    /// <exception cref="InsufficientCapitalException">Thrown for insufficient capital errors</exception>
    /// <exception cref="ThrottleExceededException">Thrown for throttle exceeded errors</exception>
    /// <exception cref="PositionLimitException">Thrown for position limit errors</exception>
    /// <exception cref="FileNotFoundException">Thrown for file not found errors</exception>
    /// <exception cref="EngineException">Thrown for unknown or internal errors</exception>
    public static void CheckResult(int errorCode, string operation)
    {
        if (errorCode == ErrorCodes.Success)
        {
            return;
        }

        throw errorCode switch
        {
            ErrorCodes.NullPointer => new ArgumentNullException(operation,
                "Null pointer passed to native code"),

            ErrorCodes.InvalidParam => new ArgumentException(
                $"Invalid parameter in {operation}", operation),

            ErrorCodes.EngineNotInit => new InvalidOperationException(
                $"Engine not initialized for {operation}"),

            ErrorCodes.RiskRejected => new RiskRejectedException(
                $"Order rejected by risk manager during {operation}"),

            ErrorCodes.DataLoadFailed => new DataLoadException(
                $"Failed to load data during {operation}"),

            ErrorCodes.InvalidData => new InvalidDataException(
                $"Invalid data encountered during {operation}"),

            ErrorCodes.InsufficientCapital => new InsufficientCapitalException(
                $"Insufficient capital for {operation}"),

            ErrorCodes.ThrottleExceeded => new ThrottleExceededException(
                $"Order rate throttle exceeded during {operation}"),

            ErrorCodes.PositionLimit => new PositionLimitException(
                $"Position limit exceeded during {operation}"),

            ErrorCodes.FileNotFound => new FileNotFoundException(
                $"File not found during {operation}"),

            ErrorCodes.InternalPanic => new EngineException(
                $"Internal engine panic during {operation}. This is a bug."),

            _ => new EngineException(
                $"Unknown error {errorCode} during {operation}")
        };
    }

    /// <summary>
    /// Gets a human-readable description of an error code.
    /// </summary>
    /// <param name="errorCode">Error code from Rust function</param>
    /// <returns>Description of the error</returns>
    public static string GetErrorDescription(int errorCode)
    {
        return errorCode switch
        {
            ErrorCodes.Success => "Success",
            ErrorCodes.NullPointer => "Null pointer",
            ErrorCodes.InvalidParam => "Invalid parameter",
            ErrorCodes.EngineNotInit => "Engine not initialized",
            ErrorCodes.RiskRejected => "Risk rejected",
            ErrorCodes.DataLoadFailed => "Data load failed",
            ErrorCodes.InvalidData => "Invalid data",
            ErrorCodes.InsufficientCapital => "Insufficient capital",
            ErrorCodes.ThrottleExceeded => "Throttle exceeded",
            ErrorCodes.PositionLimit => "Position limit exceeded",
            ErrorCodes.FileNotFound => "File not found",
            ErrorCodes.InternalPanic => "Internal panic",
            _ => $"Unknown error ({errorCode})"
        };
    }
}
