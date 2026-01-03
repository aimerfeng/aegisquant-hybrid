# C# 回调生命周期管理

## 概述

跨语言回调是 FFI 中最容易出错的地方之一。C# 的 GC 可能在任何时候回收 delegate，而 Rust 仍持有函数指针，导致调用时崩溃。

## 问题分析

### 危险代码示例

```csharp
// ⚠️ 危险：delegate 可能被 GC 回收
public void SetLogCallback(Action<int, string> handler)
{
    LogCallback callback = (level, ptr) => {
        string msg = Marshal.PtrToStringUTF8(ptr) ?? "";
        handler(level, msg);
    };
    
    // callback 是局部变量，方法返回后可能被 GC 回收
    IntPtr ptr = Marshal.GetFunctionPointerForDelegate(callback);
    NativeMethods.set_log_callback(ptr);
    
    // 此时 Rust 持有 ptr，但 callback 可能已被回收
    // 当 Rust 调用回调时 → 访问已释放内存 → 崩溃！
}
```

**问题**:
1. **GC 不可见**: Rust 持有的是原始指针，GC 看不到
2. **随机崩溃**: GC 时机不确定，问题难以复现
3. **调试困难**: 崩溃发生在 native 代码中

## 解决方案

### 1. 保持 Delegate 引用

```csharp
public sealed class EngineWrapper : IDisposable
{
    private EngineHandle? _handle;
    private bool _disposed;
    
    /// <summary>
    /// CRITICAL: 保持 delegate 引用，防止 GC 回收
    /// </summary>
    private LogCallback? _logCallbackKeepAlive;
    private StringCallback? _stringCallbackKeepAlive;
    private Action<LogLevel, string>? _logHandler;
    
    /// <summary>
    /// 设置日志回调
    /// </summary>
    public void SetLogCallback(Action<LogLevel, string> handler)
    {
        ThrowIfDisposed();
        
        _logHandler = handler;
        
        // 创建 native callback 并保持引用
        _logCallbackKeepAlive = (level, messagePtr) =>
        {
            try
            {
                string message = Marshal.PtrToStringUTF8(messagePtr) ?? "";
                _logHandler?.Invoke((LogLevel)level, message);
            }
            catch
            {
                // 回调中的异常不能传播到 Rust
            }
        };
        
        // 获取函数指针
        IntPtr callbackPtr = Marshal.GetFunctionPointerForDelegate(_logCallbackKeepAlive);
        int result = NativeMethods.SetLogCallback(callbackPtr);
        ErrorHandler.CheckResult(result, "SetLogCallback");
    }
}
```

### 2. 正确的 Dispose 顺序

```csharp
public void Dispose()
{
    if (!_disposed)
    {
        // 步骤 1: 先清除 Rust 侧的回调引用
        if (_logCallbackKeepAlive != null)
        {
            NativeMethods.ClearLogCallback();
        }
        
        // 步骤 2: 释放 native handle
        _handle?.Dispose();
        _handle = null;
        
        // 步骤 3: 最后清除 delegate 引用
        _logCallbackKeepAlive = null;
        _stringCallbackKeepAlive = null;
        _logHandler = null;
        
        _disposed = true;
    }
}
```

**顺序很重要**:
1. 先告诉 Rust 不要再调用回调
2. 再释放 native 资源
3. 最后释放 delegate

### 3. 使用 GCHandle 额外保护 (可选)

```csharp
private GCHandle _logCallbackHandle;

public void SetLogCallback(Action<LogLevel, string> handler)
{
    ThrowIfDisposed();
    
    // 释放之前的 handle
    if (_logCallbackHandle.IsAllocated)
    {
        _logCallbackHandle.Free();
    }
    
    _logCallbackKeepAlive = (level, messagePtr) =>
    {
        string message = Marshal.PtrToStringUTF8(messagePtr) ?? "";
        handler((LogLevel)level, message);
    };
    
    // 使用 GCHandle 固定 delegate
    _logCallbackHandle = GCHandle.Alloc(_logCallbackKeepAlive);
    
    IntPtr callbackPtr = Marshal.GetFunctionPointerForDelegate(_logCallbackKeepAlive);
    NativeMethods.SetLogCallback(callbackPtr);
}

public void Dispose()
{
    if (!_disposed)
    {
        NativeMethods.ClearLogCallback();
        
        // 释放 GCHandle
        if (_logCallbackHandle.IsAllocated)
        {
            _logCallbackHandle.Free();
        }
        
        _logCallbackKeepAlive = null;
        _handle?.Dispose();
        _disposed = true;
    }
}
```

### 4. 回调中的异常处理

```csharp
_logCallbackKeepAlive = (level, messagePtr) =>
{
    try
    {
        string message = Marshal.PtrToStringUTF8(messagePtr) ?? "";
        _logHandler?.Invoke((LogLevel)level, message);
    }
    catch (Exception ex)
    {
        // CRITICAL: 异常不能传播到 Rust
        // Rust 不知道如何处理 C# 异常
        Debug.WriteLine($"Exception in log callback: {ex}");
    }
};
```

### 5. Rust 侧的配合

```rust
// logger.rs
use std::sync::atomic::{AtomicPtr, Ordering};

static LOG_CALLBACK: AtomicPtr<()> = AtomicPtr::new(std::ptr::null_mut());

pub fn set_callback(callback: LogCallback) {
    LOG_CALLBACK.store(callback as *mut (), Ordering::SeqCst);
}

pub fn clear_callback() {
    LOG_CALLBACK.store(std::ptr::null_mut(), Ordering::SeqCst);
}

pub fn log(level: LogLevel, message: &str) {
    let ptr = LOG_CALLBACK.load(Ordering::SeqCst);
    
    // 检查回调是否有效
    if ptr.is_null() {
        return;  // 静默忽略
    }
    
    let callback: LogCallback = unsafe { std::mem::transmute(ptr) };
    if let Ok(c_msg) = CString::new(message) {
        callback(level as i32, c_msg.as_ptr());
    }
}
```

## 完整示例

```csharp
public sealed class EngineWrapper : IDisposable
{
    private EngineHandle? _handle;
    private bool _disposed;
    
    // 回调引用 - 防止 GC 回收
    private LogCallback? _logCallbackKeepAlive;
    private EventCallback? _eventCallbackKeepAlive;
    
    public EngineWrapper(StrategyParams parameters, RiskConfig riskConfig)
    {
        unsafe
        {
            IntPtr ptr = NativeMethods.InitEngine(&parameters, &riskConfig);
            if (ptr == IntPtr.Zero)
                throw new EngineException("Failed to initialize engine");
            _handle = new EngineHandle(ptr);
        }
    }
    
    public void SetLogCallback(Action<LogLevel, string> handler)
    {
        ThrowIfDisposed();
        
        _logCallbackKeepAlive = (level, messagePtr) =>
        {
            try
            {
                string message = Marshal.PtrToStringUTF8(messagePtr) ?? "";
                handler((LogLevel)level, message);
            }
            catch { }
        };
        
        IntPtr ptr = Marshal.GetFunctionPointerForDelegate(_logCallbackKeepAlive);
        NativeMethods.SetLogCallback(ptr);
    }
    
    public void ClearLogCallback()
    {
        ThrowIfDisposed();
        NativeMethods.ClearLogCallback();
        _logCallbackKeepAlive = null;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            // 1. 清除 Rust 侧回调
            if (_logCallbackKeepAlive != null)
                NativeMethods.ClearLogCallback();
            if (_eventCallbackKeepAlive != null)
                NativeMethods.ClearEventCallback();
            
            // 2. 释放 native handle
            _handle?.Dispose();
            _handle = null;
            
            // 3. 清除 delegate 引用
            _logCallbackKeepAlive = null;
            _eventCallbackKeepAlive = null;
            
            _disposed = true;
        }
    }
    
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EngineWrapper));
    }
}
```

## 面试话术

### Q: 为什么 delegate 会被 GC 回收？

**A**: C# 的 GC 只看托管堆的引用。当我们把 delegate 转换为函数指针传给 Rust：
1. Rust 持有的是原始指针，GC 看不到
2. 如果 C# 侧没有其他引用，GC 认为 delegate 可以回收
3. 回收后，Rust 调用这个指针就是访问已释放内存

### Q: 如何防止 delegate 被回收？

**A**: 两种方法：
1. **字段引用**: 将 delegate 存储为类的字段
2. **GCHandle**: 使用 `GCHandle.Alloc()` 固定 delegate

我推荐字段引用，因为更简单且足够安全。GCHandle 是额外保护。

### Q: Dispose 顺序为什么重要？

**A**: 必须先清除 Rust 侧的回调，再释放 delegate：
1. 如果先释放 delegate，Rust 可能还在调用
2. 调用已释放的 delegate → 崩溃

正确顺序：清除 Rust 回调 → 释放 handle → 清除 delegate 引用
