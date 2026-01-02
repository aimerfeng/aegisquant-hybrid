namespace AegisQuant.Interop;

/// <summary>
/// Base exception for all engine-related errors.
/// </summary>
public class EngineException : Exception
{
    public EngineException() : base() { }
    public EngineException(string message) : base(message) { }
    public EngineException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when an order is rejected by the risk manager.
/// </summary>
public class RiskRejectedException : EngineException
{
    public RiskRejectedException() : base("Order rejected by risk manager") { }
    public RiskRejectedException(string message) : base(message) { }
    public RiskRejectedException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when data loading fails.
/// </summary>
public class DataLoadException : EngineException
{
    public DataLoadException() : base("Failed to load data") { }
    public DataLoadException(string message) : base(message) { }
    public DataLoadException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when invalid data is encountered.
/// </summary>
public class InvalidDataException : EngineException
{
    public InvalidDataException() : base("Invalid data encountered") { }
    public InvalidDataException(string message) : base(message) { }
    public InvalidDataException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when there is insufficient capital for an order.
/// </summary>
public class InsufficientCapitalException : RiskRejectedException
{
    public InsufficientCapitalException() : base("Insufficient capital for order") { }
    public InsufficientCapitalException(string message) : base(message) { }
    public InsufficientCapitalException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when the order rate throttle is exceeded.
/// </summary>
public class ThrottleExceededException : RiskRejectedException
{
    public ThrottleExceededException() : base("Order rate throttle exceeded") { }
    public ThrottleExceededException(string message) : base(message) { }
    public ThrottleExceededException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when the position limit is exceeded.
/// </summary>
public class PositionLimitException : RiskRejectedException
{
    public PositionLimitException() : base("Position limit exceeded") { }
    public PositionLimitException(string message) : base(message) { }
    public PositionLimitException(string message, Exception innerException) : base(message, innerException) { }
}
