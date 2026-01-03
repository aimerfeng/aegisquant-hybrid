# FFI 字符串处理内存安全

## 概述

跨语言字符串传递是 FFI 中最容易出错的地方之一。本文档详细说明如何避免内存泄漏和悬垂指针问题。

## 问题分析

### 原始代码 (有风险)

```rust
// ffi.rs - 潜在内存泄漏
#[no_mangle]
pub unsafe extern "C" fn get_error_message() -> *const c_char {
    let msg = CString::new("Error occurred").unwrap();
    msg.into_raw()  // ⚠️ 内存泄漏！谁来释放这块内存？
}
```

**问题**: `into_raw()` 将 CString 的所有权转移给调用者，但 C# 侧不知道如何释放 Rust 分配的内存。

### 解决方案 1: 回调方式 (推荐)

```rust
// ffi.rs - 使用回调传递字符串
pub type StringCallback = extern "C" fn(*const c_char);

#[no_mangle]
pub unsafe extern "C" fn get_error_message_with_callback(callback: StringCallback) {
    let msg = CString::new("Error occurred").unwrap();
    callback(msg.as_ptr());  // ✅ 传递引用，Rust 保持所有权
    // msg 在这里自动释放
}
```

```csharp
// C# 侧
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void StringCallback(IntPtr message);

public string GetErrorMessage()
{
    string result = "";
    StringCallback callback = (ptr) => {
        result = Marshal.PtrToStringUTF8(ptr) ?? "";  // 立即复制
    };
    
    // 保持 delegate 引用
    _stringCallbackKeepAlive = callback;
    NativeMethods.get_error_message_with_callback(callback);
    return result;
}
```

### 解决方案 2: 配套 free 函数

如果必须返回字符串指针，必须提供配套的释放函数：

```rust
// ffi.rs
#[no_mangle]
pub unsafe extern "C" fn get_error_message() -> *mut c_char {
    let msg = CString::new("Error occurred").unwrap();
    msg.into_raw()
}

#[no_mangle]
pub unsafe extern "C" fn free_string(ptr: *mut c_char) {
    if !ptr.is_null() {
        let _ = CString::from_raw(ptr);  // 重新获取所有权并释放
    }
}
```

```csharp
// C# 侧必须调用 free_string
public string GetErrorMessage()
{
    IntPtr ptr = NativeMethods.get_error_message();
    try
    {
        return Marshal.PtrToStringUTF8(ptr) ?? "";
    }
    finally
    {
        NativeMethods.free_string(ptr);  // 必须释放！
    }
}
```

## load_data_from_file 优化

### 原始代码

```rust
pub unsafe extern "C" fn load_data_from_file(
    engine: *mut BacktestEngine,
    file_path: *const c_char,
    report: *mut DataQualityReport,
) -> i32 {
    let path_cstr = CStr::from_ptr(file_path);
    let path = path_cstr.to_str().unwrap();  // ⚠️ 可能 panic
    // ...
}
```

### 优化后代码

```rust
pub unsafe extern "C" fn load_data_from_file(
    engine: *mut BacktestEngine,
    file_path: *const c_char,
    report: *mut DataQualityReport,
) -> i32 {
    // 指针验证
    if engine.is_null() || file_path.is_null() || report.is_null() {
        return ERR_NULL_POINTER;
    }
    
    // 安全的字符串转换
    let path_cstr = CStr::from_ptr(file_path);
    let path = match path_cstr.to_str() {
        Ok(s) => s,
        Err(_) => return ERR_INVALID_PARAM,  // 无效 UTF-8
    };
    
    // 使用 catch_unwind 防止 panic
    let result = std::panic::catch_unwind(|| {
        let engine = &mut *engine;
        engine.load_data(path)
    });
    
    match result {
        Ok(Ok(quality_report)) => {
            *report = quality_report;
            ERR_SUCCESS
        }
        Ok(Err(e)) => e.to_error_code(),
        Err(_) => ERR_INTERNAL_PANIC,
    }
}
```

## 面试话术

### Q: FFI 中字符串传递有哪些陷阱？

**A**: 主要有三个陷阱：
1. **内存泄漏**: 使用 `CString::into_raw()` 后忘记释放
2. **悬垂指针**: 返回栈上字符串的指针
3. **编码问题**: Rust 是 UTF-8，C# 默认是 UTF-16

我的解决方案是使用回调模式：Rust 保持字符串所有权，通过回调传递引用，C# 在回调中立即复制。这样 Rust 负责内存管理，C# 只需要复制数据。

### Q: 为什么推荐回调方式而不是返回指针？

**A**: 回调方式有三个优势：
1. **所有权清晰**: Rust 始终拥有字符串，自动释放
2. **无需配套函数**: 不需要 `free_string`
3. **更安全**: 避免 C# 侧忘记释放导致的内存泄漏

唯一的缺点是 API 稍微复杂，但安全性收益远大于复杂性成本。

### Q: CStr 和 CString 有什么区别？

**A**: 
- `CStr`: 借用的 C 字符串切片，类似 `&str`，不拥有内存
- `CString`: 拥有所有权的 C 字符串，类似 `String`，负责分配和释放

从 C 接收字符串用 `CStr::from_ptr()`，向 C 传递字符串用 `CString::new()`。
