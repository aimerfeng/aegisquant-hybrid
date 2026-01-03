# 线程安全的日志回调系统

## 概述

跨语言日志回调需要处理线程安全和生命周期问题。本文档说明如何使用 `AtomicPtr` 实现零内存泄漏、零 Panic 的日志系统。

## 问题分析

### 原始代码 (不安全)

```rust
// logger.rs - 使用 static mut (危险！)
static mut LOG_CALLBACK: Option<extern "C" fn(i32, *const c_char)> = None;

pub fn log(level: i32, message: &str) {
    unsafe {
        if let Some(callback) = LOG_CALLBACK {  // ⚠️ 数据竞争！
            let c_msg = CString::new(message).unwrap();
            callback(level, c_msg.as_ptr());
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn set_log_callback(
    callback: extern "C" fn(i32, *const c_char)
) {
    LOG_CALLBACK = Some(callback);  // ⚠️ 数据竞争！
}
```

**问题**:
1. `static mut` 在多线程环境下是未定义行为
2. 读写 `LOG_CALLBACK` 没有同步机制
3. 如果 C# 侧的 delegate 被 GC 回收，回调时会崩溃

## 解决方案

### 1. 使用 AtomicPtr

```rust
// logger.rs - 线程安全版本
use std::sync::atomic::{AtomicPtr, Ordering};
use std::ffi::{c_char, CString};

/// 日志回调函数类型
pub type LogCallback = extern "C" fn(level: i32, message: *const c_char);

/// 全局日志回调指针 (线程安全)
static LOG_CALLBACK: AtomicPtr<()> = AtomicPtr::new(std::ptr::null_mut());

/// 日志级别
#[repr(i32)]
pub enum LogLevel {
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
}

/// 设置日志回调
/// 
/// # Safety
/// callback 必须是有效的函数指针，且在调用期间保持有效
pub fn set_callback(callback: LogCallback) {
    LOG_CALLBACK.store(callback as *mut (), Ordering::SeqCst);
}

/// 清除日志回调
pub fn clear_callback() {
    LOG_CALLBACK.store(std::ptr::null_mut(), Ordering::SeqCst);
}

/// 记录日志
pub fn log(level: LogLevel, message: &str) {
    let callback_ptr = LOG_CALLBACK.load(Ordering::SeqCst);
    
    if callback_ptr.is_null() {
        return;  // 没有回调，静默忽略
    }
    
    // 安全地转换为函数指针
    let callback: LogCallback = unsafe { std::mem::transmute(callback_ptr) };
    
    // 创建 C 字符串
    if let Ok(c_msg) = CString::new(message) {
        callback(level as i32, c_msg.as_ptr());
    }
    // 如果消息包含 null 字节，静默忽略
}

/// 便捷宏
#[macro_export]
macro_rules! log_info {
    ($($arg:tt)*) => {
        $crate::logger::log($crate::logger::LogLevel::Info, &format!($($arg)*))
    };
}

#[macro_export]
macro_rules! log_error {
    ($($arg:tt)*) => {
        $crate::logger::log($crate::logger::LogLevel::Error, &format!($($arg)*))
    };
}
```

### 2. FFI 导出函数

```rust
// ffi.rs
use crate::logger::{self, LogCallback};

/// 设置日志回调
/// 
/// # Safety
/// - callback 必须是有效的函数指针
/// - callback 在整个使用期间必须保持有效
/// - 调用者负责在不再需要时调用 clear_log_callback
#[no_mangle]
pub unsafe extern "C" fn set_log_callback(callback: LogCallback) -> i32 {
    logger::set_callback(callback);
    ERR_SUCCESS
}

/// 清除日志回调
/// 
/// # Safety
/// 调用后，之前设置的回调将不再被调用
#[no_mangle]
pub extern "C" fn clear_log_callback() -> i32 {
    logger::clear_callback();
    ERR_SUCCESS
}
```

### 3. C# 侧实现

```csharp
// NativeMethods.cs
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void LogCallback(int level, IntPtr message);

internal static partial class NativeMethods
{
    [LibraryImport(DllName)]
    public static partial int set_log_callback(IntPtr callback);
    
    [LibraryImport(DllName)]
    public static partial int clear_log_callback();
}
```

```csharp
// EngineWrapper.cs
public sealed class EngineWrapper : IDisposable
{
    private EngineHandle? _handle;
    private bool _disposed;
    
    // CRITICAL: 保持 delegate 引用，防止 GC 回收
    // 如果这个字段被 GC 回收，Rust 调用回调时会访问已释放内存
    private LogCallback? _logCallbackKeepAlive;
    
    /// <summary>
    /// 设置日志回调
    /// </summary>
    /// <param name="handler">日志处理函数</param>
    /// <remarks>
    /// 回调会在 Rust 线程中调用，handler 必须是线程安全的。
    /// 内部会保持 delegate 引用，防止 GC 回收。
    /// </remarks>
    public void SetLogCallback(Action<LogLevel, string> handler)
    {
        ThrowIfDisposed();
        
        // 创建 native callback
        _logCallbackKeepAlive = (level, messagePtr) =>
        {
            try
            {
                string message = Marshal.PtrToStringUTF8(messagePtr) ?? "";
                handler((LogLevel)level, message);
            }
            catch
            {
                // 回调中的异常不能传播到 Rust
            }
        };
        
        // 获取函数指针并传递给 Rust
        IntPtr callbackPtr = Marshal.GetFunctionPointerForDelegate(_logCallbackKeepAlive);
        int result = NativeMethods.set_log_callback(callbackPtr);
        ErrorHandler.CheckResult(result, "set_log_callback");
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            // 先清除 Rust 侧的回调引用
            if (_logCallbackKeepAlive != null)
            {
                NativeMethods.clear_log_callback();
                _logCallbackKeepAlive = null;
            }
            
            // 再释放引擎
            _handle?.Dispose();
            _disposed = true;
        }
    }
}
```

### 4. 使用 GCHandle 额外保护 (可选)

```csharp
// 更保守的做法：使用 GCHandle 固定 delegate
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
    
    // 固定 delegate，防止 GC 移动或回收
    _logCallbackHandle = GCHandle.Alloc(_logCallbackKeepAlive);
    
    IntPtr callbackPtr = Marshal.GetFunctionPointerForDelegate(_logCallbackKeepAlive);
    NativeMethods.set_log_callback(callbackPtr);
}

public void Dispose()
{
    if (!_disposed)
    {
        NativeMethods.clear_log_callback();
        
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

## 内存顺序说明

```rust
// Ordering::SeqCst 是最严格的内存顺序
// 保证所有线程看到相同的操作顺序
LOG_CALLBACK.store(callback as *mut (), Ordering::SeqCst);
LOG_CALLBACK.load(Ordering::SeqCst);

// 对于简单的读写，也可以使用 Release/Acquire
// store 使用 Release，load 使用 Acquire
LOG_CALLBACK.store(callback as *mut (), Ordering::Release);
LOG_CALLBACK.load(Ordering::Acquire);
```

## 面试话术

### Q: 为什么用 AtomicPtr 而不是 Mutex？

**A**: 日志是高频操作，Mutex 有两个问题：
1. **性能**: 每次日志都要加锁/解锁
2. **死锁风险**: 如果回调中又调用了日志，会死锁

`AtomicPtr` 是无锁的，只需要一次原子 load 操作，性能接近普通指针访问。

### Q: C# delegate 为什么会被 GC 回收？

**A**: C# 的 GC 只看托管堆的引用。当我们把 delegate 转换为函数指针传给 Rust 后：
1. Rust 持有的是原始指针，GC 看不到
2. 如果 C# 侧没有其他引用，GC 认为 delegate 可以回收
3. 回收后，Rust 调用这个指针就是访问已释放内存

解决方案是在 C# 侧保持一个字段引用 delegate，告诉 GC "这个对象还在用"。

### Q: Ordering::SeqCst 和 Ordering::Relaxed 有什么区别？

**A**: 
- `Relaxed`: 只保证原子性，不保证顺序
- `Acquire`: 读操作，保证之后的读写不会重排到之前
- `Release`: 写操作，保证之前的读写不会重排到之后
- `SeqCst`: 最严格，所有线程看到相同的全局顺序

对于回调指针，我用 `SeqCst` 是最安全的选择。性能差异在日志场景下可以忽略。
