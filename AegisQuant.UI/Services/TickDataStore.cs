using AegisQuant.Interop;

namespace AegisQuant.UI.Services;

/// <summary>
/// Structure of Arrays (SoA) layout for memory-efficient tick storage.
/// Reduces GC pressure and improves cache locality.
/// 
/// Memory Layout:
/// - Timestamps: long[] (8 bytes per tick)
/// - Prices: double[] (8 bytes per tick)
/// - Volumes: double[] (8 bytes per tick)
/// Total: 24 bytes per tick (vs 40+ bytes for class-based AoS)
/// 
/// Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6
/// </summary>
public class TickDataStore
{
    /// <summary>
    /// Unix timestamps in milliseconds.
    /// Using long[] instead of DateTime[] to avoid boxing and reduce memory.
    /// </summary>
    public long[] Timestamps { get; private set; } = Array.Empty<long>();
    
    /// <summary>
    /// Price values stored as primitive double[].
    /// </summary>
    public double[] Prices { get; private set; } = Array.Empty<double>();
    
    /// <summary>
    /// Volume values stored as primitive double[].
    /// </summary>
    public double[] Volumes { get; private set; } = Array.Empty<double>();
    
    /// <summary>
    /// Current number of ticks stored.
    /// </summary>
    public int Count { get; private set; }
    
    /// <summary>
    /// Gets the allocated capacity.
    /// </summary>
    public int Capacity => Timestamps.Length;
    
    /// <summary>
    /// Pre-allocates arrays for the specified capacity.
    /// This avoids repeated array resizing and reduces GC pressure.
    /// </summary>
    /// <param name="capacity">Number of ticks to allocate space for</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if capacity is negative</exception>
    public void Allocate(int capacity)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity cannot be negative");
        
        Timestamps = new long[capacity];
        Prices = new double[capacity];
        Volumes = new double[capacity];
        Count = 0;
    }
    
    /// <summary>
    /// Adds a tick to the store.
    /// </summary>
    /// <param name="timestamp">Unix timestamp in milliseconds</param>
    /// <param name="price">Price value</param>
    /// <param name="volume">Volume value</param>
    /// <exception cref="InvalidOperationException">Thrown if capacity is exceeded</exception>
    public void Add(long timestamp, double price, double volume)
    {
        if (Count >= Timestamps.Length)
            throw new InvalidOperationException("TickDataStore capacity exceeded. Call Allocate() with a larger capacity.");
        
        Timestamps[Count] = timestamp;
        Prices[Count] = price;
        Volumes[Count] = volume;
        Count++;
    }
    
    /// <summary>
    /// Gets a tick at the specified index.
    /// </summary>
    /// <param name="index">Zero-based index of the tick</param>
    /// <returns>Tick struct with timestamp, price, and volume</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if index is out of range</exception>
    public Tick GetTick(int index)
    {
        if (index < 0 || index >= Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is out of range. Valid range: 0 to {Count - 1}");
            
        return new Tick
        {
            Timestamp = Timestamps[index],
            Price = Prices[index],
            Volume = Volumes[index]
        };
    }
    
    /// <summary>
    /// Clears all stored ticks without deallocating arrays.
    /// This allows reuse of the allocated memory.
    /// </summary>
    public void Clear()
    {
        Count = 0;
        // Note: We don't clear the arrays themselves to allow memory reuse
        // The Count property ensures we don't read stale data
    }
    
    /// <summary>
    /// Calculates the approximate memory usage in bytes.
    /// Useful for monitoring memory consumption.
    /// </summary>
    /// <returns>Approximate memory usage in bytes</returns>
    public long GetMemoryUsageBytes()
    {
        // Each array: 8 bytes per element
        // long[] = 8 bytes * capacity
        // double[] = 8 bytes * capacity (x2 for prices and volumes)
        // Total = 24 bytes per tick capacity
        return Capacity * 24L;
    }
}
