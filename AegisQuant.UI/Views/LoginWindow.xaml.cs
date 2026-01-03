using System.Windows;
using AegisQuant.UI.Services;

namespace AegisQuant.UI.Views;

/// <summary>
/// 登录窗口
/// Requirements: 14.1, 14.2, 14.3, 14.4
/// </summary>
public partial class LoginWindow : Window
{
    private const int MaxFailedAttempts = 3;
    private const int LockoutMinutes = 5;
    
    private int _failedAttempts;
    private DateTime? _lockoutUntil;

    public string? AuthenticatedUsername { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        
        // 尝试自动登录
        TryAutoLogin();
    }

    private void TryAutoLogin()
    {
        var token = AuthenticationService.Instance.GetRememberToken();
        if (!string.IsNullOrEmpty(token))
        {
            var username = AuthenticationService.Instance.ValidateRememberToken(token);
            if (!string.IsNullOrEmpty(username))
            {
                AuthenticatedUsername = username;
                AuditLogService.Instance.LogLogin(username, true);
                DialogResult = true;
                Close();
            }
        }
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        // 检查锁定状态
        if (_lockoutUntil.HasValue && DateTime.Now < _lockoutUntil.Value)
        {
            var remaining = (_lockoutUntil.Value - DateTime.Now).TotalMinutes;
            LockoutText.Text = $"账户已锁定，请在 {remaining:F0} 分钟后重试";
            LockoutText.Visibility = Visibility.Visible;
            return;
        }

        var username = UsernameTextBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("请输入用户名和密码");
            return;
        }

        // 验证登录
        if (AuthenticationService.Instance.ValidateCredentials(username, password))
        {
            AuthenticatedUsername = username;
            _failedAttempts = 0;
            
            // 记住我
            if (RememberMeCheckBox.IsChecked == true)
            {
                AuthenticationService.Instance.SaveRememberToken(username);
            }
            
            AuditLogService.Instance.LogLogin(username, true);
            DialogResult = true;
            Close();
        }
        else
        {
            _failedAttempts++;
            AuditLogService.Instance.LogLogin(username, false);
            
            if (_failedAttempts >= MaxFailedAttempts)
            {
                _lockoutUntil = DateTime.Now.AddMinutes(LockoutMinutes);
                LockoutText.Text = $"登录失败次数过多，账户已锁定 {LockoutMinutes} 分钟";
                LockoutText.Visibility = Visibility.Visible;
                ErrorText.Visibility = Visibility.Collapsed;
                LoginButton.IsEnabled = false;
                
                // 启动解锁计时器
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMinutes(LockoutMinutes)
                };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    _lockoutUntil = null;
                    _failedAttempts = 0;
                    LockoutText.Visibility = Visibility.Collapsed;
                    LoginButton.IsEnabled = true;
                };
                timer.Start();
            }
            else
            {
                ShowError($"用户名或密码错误 (剩余 {MaxFailedAttempts - _failedAttempts} 次尝试)");
            }
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
