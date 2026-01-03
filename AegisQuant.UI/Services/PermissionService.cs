using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AegisQuant.UI.Services;

/// <summary>
/// 功能权限枚举
/// </summary>
public enum Permission
{
    /// <summary>查看数据</summary>
    ViewData,
    /// <summary>加载数据</summary>
    LoadData,
    /// <summary>运行回测</summary>
    RunBacktest,
    /// <summary>修改参数</summary>
    ModifyParameters,
    /// <summary>手动下单</summary>
    ManualOrder,
    /// <summary>紧急停止</summary>
    EmergencyStop,
    /// <summary>一键清仓</summary>
    CloseAllPositions,
    /// <summary>切换环境</summary>
    SwitchEnvironment,
    /// <summary>切换到实盘</summary>
    SwitchToLive,
    /// <summary>修改配置</summary>
    ModifyConfig,
    /// <summary>查看审计日志</summary>
    ViewAuditLog,
    /// <summary>管理用户</summary>
    ManageUsers,
    /// <summary>导出数据</summary>
    ExportData
}

/// <summary>
/// 权限控制服务
/// Requirements: 14.6
/// </summary>
public class PermissionService : INotifyPropertyChanged
{
    private static PermissionService? _instance;
    private static readonly object _lock = new();

    public static PermissionService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new PermissionService();
                }
            }
            return _instance;
        }
    }

    // 权限映射表: Permission -> 最低所需角色
    private readonly Dictionary<Permission, UserRole> _permissionMap = new()
    {
        // Viewer 权限
        { Permission.ViewData, UserRole.Viewer },
        { Permission.ViewAuditLog, UserRole.Viewer },
        
        // Trader 权限
        { Permission.LoadData, UserRole.Trader },
        { Permission.RunBacktest, UserRole.Trader },
        { Permission.ModifyParameters, UserRole.Trader },
        { Permission.ManualOrder, UserRole.Trader },
        { Permission.EmergencyStop, UserRole.Trader },
        { Permission.CloseAllPositions, UserRole.Trader },
        { Permission.SwitchEnvironment, UserRole.Trader },
        { Permission.ExportData, UserRole.Trader },
        
        // Admin 权限
        { Permission.SwitchToLive, UserRole.Admin },
        { Permission.ModifyConfig, UserRole.Admin },
        { Permission.ManageUsers, UserRole.Admin }
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private PermissionService() { }

    /// <summary>
    /// 检查当前用户是否有指定权限
    /// </summary>
    public bool HasPermission(Permission permission)
    {
        var auth = AuthenticationService.Instance;
        if (!auth.IsAuthenticated || auth.CurrentUser == null)
            return false;

        if (!_permissionMap.TryGetValue(permission, out var requiredRole))
            return false;

        return auth.CurrentUser.Role >= requiredRole;
    }

    /// <summary>
    /// 检查并执行操作，如果没有权限则显示提示
    /// </summary>
    public bool CheckAndExecute(Permission permission, Action action, string operationName = "此操作")
    {
        if (!HasPermission(permission))
        {
            System.Windows.MessageBox.Show(
                $"您没有执行{operationName}的权限。\n\n所需角色: {GetRequiredRoleName(permission)}",
                "权限不足",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        action();
        return true;
    }

    /// <summary>
    /// 获取权限所需的角色名称
    /// </summary>
    public string GetRequiredRoleName(Permission permission)
    {
        if (!_permissionMap.TryGetValue(permission, out var role))
            return "未知";

        return role switch
        {
            UserRole.Viewer => "查看者",
            UserRole.Trader => "交易员",
            UserRole.Admin => "管理员",
            _ => "未知"
        };
    }

    /// <summary>
    /// 获取当前用户角色名称
    /// </summary>
    public string CurrentRoleName
    {
        get
        {
            var user = AuthenticationService.Instance.CurrentUser;
            if (user == null)
                return "未登录";

            return user.Role switch
            {
                UserRole.Viewer => "查看者",
                UserRole.Trader => "交易员",
                UserRole.Admin => "管理员",
                _ => "未知"
            };
        }
    }

    /// <summary>
    /// 当前用户是否可以交易
    /// </summary>
    public bool CanTrade => HasPermission(Permission.ManualOrder);

    /// <summary>
    /// 当前用户是否是管理员
    /// </summary>
    public bool IsAdmin => AuthenticationService.Instance.IsAdmin();

    /// <summary>
    /// 刷新权限状态 (用户登录/登出后调用)
    /// </summary>
    public void RefreshPermissions()
    {
        OnPropertyChanged(nameof(CanTrade));
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(CurrentRoleName));
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// 权限检查扩展方法
/// </summary>
public static class PermissionExtensions
{
    /// <summary>
    /// 检查是否有权限
    /// </summary>
    public static bool Can(this Permission permission)
    {
        return PermissionService.Instance.HasPermission(permission);
    }

    /// <summary>
    /// 检查并执行
    /// </summary>
    public static bool CheckAndDo(this Permission permission, Action action, string operationName = "此操作")
    {
        return PermissionService.Instance.CheckAndExecute(permission, action, operationName);
    }
}
