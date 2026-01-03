# 错误传播与 Panic 消除

## 概述

在热路径中使用 `unwrap()` 是定时炸弹。本文档说明如何用 `?` 运算符优雅地传播错误。

## 问题分析

### 原始代码 (危险)

```rust
// data_loader.rs - 到处都是 unwrap
pub fn load_csv(path: &str) -> DataFrame {
    let df = CsvReader::from_path(path)
        .unwrap()  // ⚠️ 文件不存在就 panic
        .has_header(true)
        .finish()
        .unwrap();  // ⚠️ 格式错误就 panic
    
    let timestamps = df.column("timestamp").unwrap();  // ⚠️ 列不存在就 panic
    // ...
}
```

**问题**: 任何一个 `unwrap()` 失败都会导致整个引擎 panic，即使有 `catch_unwind`，用户也只能看到 "Internal Panic" 这种无用的错误信息。

## 解决方案

### 1. 定义错误类型

```rust
// error.rs
use thiserror::Error;

#[derive(Error, Debug)]
pub enum DataLoadError {
    #[error("File not found: {0}")]
    FileNotFound(String),
    
    #[error("IO error: {0}")]
    IoError(#[from] std::io::Error),
    
    #[error("CSV parse error: {0}")]
    ParseError(#[from] polars::error::PolarsError),
    
    #[error("Missing column: {0}")]
    MissingColumn(String),
    
    #[error("Invalid data: {0}")]
    ValidationError(String),
}

impl DataLoadError {
    pub fn to_error_code(&self) -> i32 {
        match self {
            DataLoadError::FileNotFound(_) => ERR_FILE_NOT_FOUND,
            DataLoadError::IoError(_) => ERR_DATA_LOAD_FAILED,
            DataLoadError::ParseError(_) => ERR_DATA_LOAD_FAILED,
            DataLoadError::MissingColumn(_) => ERR_INVALID_DATA,
            DataLoadError::ValidationError(_) => ERR_INVALID_DATA,
        }
    }
}
```

### 2. 使用 ? 运算符

```rust
// data_loader.rs - 优化后
pub fn load_csv(path: &str) -> Result<DataFrame, DataLoadError> {
    // 检查文件是否存在
    if !std::path::Path::new(path).exists() {
        return Err(DataLoadError::FileNotFound(path.to_string()));
    }
    
    // ? 自动将 PolarsError 转换为 DataLoadError
    let df = CsvReader::from_path(path)?
        .has_header(true)
        .finish()?;
    
    // 验证必需列
    let required_columns = ["timestamp", "price", "volume"];
    for col in required_columns {
        if df.column(col).is_err() {
            return Err(DataLoadError::MissingColumn(col.to_string()));
        }
    }
    
    Ok(df)
}
```

### 3. FFI 层错误处理

```rust
// ffi.rs
#[no_mangle]
pub unsafe extern "C" fn load_data_from_file(
    engine: *mut BacktestEngine,
    file_path: *const c_char,
    report: *mut DataQualityReport,
    error_msg: *mut c_char,      // 新增：错误消息缓冲区
    error_msg_len: i32,          // 新增：缓冲区长度
) -> i32 {
    if engine.is_null() || file_path.is_null() || report.is_null() {
        return ERR_NULL_POINTER;
    }
    
    let path = match CStr::from_ptr(file_path).to_str() {
        Ok(s) => s,
        Err(_) => return ERR_INVALID_PARAM,
    };
    
    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        let engine = &mut *engine;
        engine.load_data(path)
    }));
    
    match result {
        Ok(Ok(quality_report)) => {
            *report = quality_report;
            ERR_SUCCESS
        }
        Ok(Err(e)) => {
            // 写入错误消息
            if !error_msg.is_null() && error_msg_len > 0 {
                let msg = e.to_string();
                let bytes = msg.as_bytes();
                let copy_len = std::cmp::min(bytes.len(), (error_msg_len - 1) as usize);
                std::ptr::copy_nonoverlapping(
                    bytes.as_ptr(),
                    error_msg as *mut u8,
                    copy_len
                );
                *error_msg.add(copy_len) = 0;  // null terminator
            }
            e.to_error_code()
        }
        Err(_) => ERR_INTERNAL_PANIC,
    }
}
```

### 4. C# 侧获取错误消息

```csharp
public DataQualityReport LoadData(string filePath)
{
    ThrowIfDisposed();
    
    unsafe
    {
        DataQualityReport report;
        byte* errorBuffer = stackalloc byte[256];
        
        int result = NativeMethods.load_data_from_file(
            _handle!.DangerousGetHandle(),
            filePath,
            &report,
            errorBuffer,
            256
        );
        
        if (result != ErrorCodes.Success)
        {
            string errorMsg = Marshal.PtrToStringUTF8((IntPtr)errorBuffer) ?? "Unknown error";
            throw new DataLoadException($"Failed to load data: {errorMsg}");
        }
        
        return report;
    }
}
```

## Polars 特定错误处理

```rust
pub fn validate_dataframe(df: &DataFrame) -> Result<(), DataLoadError> {
    // 检查价格列
    let prices = df.column("price")
        .map_err(|_| DataLoadError::MissingColumn("price".to_string()))?
        .f64()
        .map_err(|_| DataLoadError::ValidationError("price column must be f64".to_string()))?;
    
    // 检查是否有负价格
    if prices.min().unwrap_or(0.0) <= 0.0 {
        return Err(DataLoadError::ValidationError(
            "price must be positive".to_string()
        ));
    }
    
    Ok(())
}
```

## 面试话术

### Q: 为什么不用 unwrap()？

**A**: `unwrap()` 在生产代码中是反模式，原因有三：
1. **用户体验差**: panic 信息对用户毫无帮助
2. **难以调试**: 只知道 panic 了，不知道具体原因
3. **不可恢复**: panic 会终止当前线程

我使用 `?` 运算符配合自定义错误类型，可以：
- 提供详细的错误信息
- 让调用者决定如何处理错误
- 在 FFI 边界转换为错误码

### Q: thiserror 和 anyhow 有什么区别？

**A**: 
- `thiserror`: 用于库代码，定义结构化的错误类型，支持 `#[from]` 自动转换
- `anyhow`: 用于应用代码，提供 `anyhow::Error` 通用错误类型，方便快速开发

在 FFI 库中我选择 `thiserror`，因为需要将错误映射为具体的错误码。

### Q: catch_unwind 能捕获所有 panic 吗？

**A**: 不能。`catch_unwind` 只能捕获 "unwinding panic"，不能捕获：
- `panic = "abort"` 配置下的 panic
- 栈溢出
- 段错误

所以 `catch_unwind` 是最后一道防线，不能依赖它。正确做法是从源头消除 `unwrap()`。
