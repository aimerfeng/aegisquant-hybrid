using System.Runtime.InteropServices;

namespace AegisQuant.Interop;

/// <summary>
/// Safe handle wrapper for the native Rust engine pointer.
/// Ensures proper cleanup via free_engine when disposed.
/// </summary>
/// <remarks>
/// This class inherits from SafeHandle to provide automatic resource cleanup.
/// The handle is released exactly once when the object is disposed or finalized.
/// </remarks>
public sealed class EngineHandle : SafeHandle
{
    /// <summary>
    /// Creates a new EngineHandle wrapping the given native pointer.
    /// </summary>
    /// <param name="handle">Native engine pointer from init_engine</param>
    /// <remarks>
    /// SAFETY: The handle must be a valid pointer returned by init_engine.
    /// </remarks>
    public EngineHandle(IntPtr handle) : base(IntPtr.Zero, ownsHandle: true)
    {
        SetHandle(handle);
    }

    /// <summary>
    /// Creates an invalid EngineHandle (for internal use).
    /// </summary>
    private EngineHandle() : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the handle is invalid.
    /// </summary>
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>
    /// Releases the native engine resources.
    /// </summary>
    /// <returns>True if the handle was released successfully.</returns>
    /// <remarks>
    /// SAFETY: This method calls free_engine exactly once.
    /// After this call, the handle is invalid and must not be used.
    /// </remarks>
    protected override bool ReleaseHandle()
    {
        if (handle != IntPtr.Zero)
        {
            // SAFETY: We own this handle and are releasing it exactly once.
            // The Rust side will deallocate the engine memory.
            NativeMethods.FreeEngine(handle);
            handle = IntPtr.Zero;
            return true;
        }
        return false;
    }
}
