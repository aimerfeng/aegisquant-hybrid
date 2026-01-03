using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AegisQuant.UI.Services;
using AegisQuant.UI.Strategy;
using AegisQuant.UI.Strategy.Loaders;
using Microsoft.Win32;

namespace AegisQuant.UI.Views;

/// <summary>
/// Strategy editor window for creating and editing strategies.
/// </summary>
public partial class StrategyEditorWindow : Window
{
    private readonly JsonStrategyLoader _jsonLoader;
    private readonly PythonStrategyLoader _pythonLoader;
    private bool _isModified;
    private string? _currentFilePath;

    /// <summary>
    /// Gets the created strategy after successful load.
    /// </summary>
    public IStrategy? CreatedStrategy { get; private set; }

    /// <summary>
    /// Gets the file path where the strategy was saved.
    /// </summary>
    public string? SavedFilePath { get; private set; }

    public StrategyEditorWindow()
    {
        InitializeComponent();
        _jsonLoader = new JsonStrategyLoader();
        _pythonLoader = new PythonStrategyLoader();
        
        // Set default template
        TemplateComboBox.SelectedIndex = 0;
        LoadTemplate("blank");
    }

    private void StrategyTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StrategyTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string type)
        {
            UpdateEditorForType(type);
            LoadTemplate("blank");
        }
    }

    private void UpdateEditorForType(string type)
    {
        if (type == "json")
        {
            EditorTitleText.Text = "JSON 策略代码";
            JsonHelpPanel.Visibility = Visibility.Visible;
            PythonHelpPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            EditorTitleText.Text = "Python 策略代码";
            JsonHelpPanel.Visibility = Visibility.Collapsed;
            PythonHelpPanel.Visibility = Visibility.Visible;
        }
    }

    private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateComboBox.SelectedItem is ComboBoxItem item && item.Tag is string template)
        {
            if (_isModified)
            {
                var result = MessageBox.Show("当前代码已修改，是否放弃更改并加载模板?", "确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }
            LoadTemplate(template);
        }
    }

    private void LoadTemplate(string templateName)
    {
        var isJson = StrategyTypeComboBox.SelectedItem is ComboBoxItem item && 
                     item.Tag?.ToString() == "json";

        CodeEditor.Text = isJson 
            ? GetJsonTemplate(templateName) 
            : GetPythonTemplate(templateName);
        
        _isModified = false;
        ValidationOutput.Text = "";
    }

    private string GetJsonTemplate(string templateName)
    {
        return templateName switch
        {
            "ma_crossover" => @"{
  ""name"": ""MA Crossover Strategy"",
  ""description"": ""双均线交叉策略"",
  ""version"": ""1.0"",
  ""parameters"": {
    ""short_period"": 10,
    ""long_period"": 20,
    ""position_size"": 100
  },
  ""indicators"": [
    { ""name"": ""short_ma"", ""type"": ""SMA"", ""period"": ""$short_period"", ""source"": ""price"" },
    { ""name"": ""long_ma"", ""type"": ""SMA"", ""period"": ""$long_period"", ""source"": ""price"" }
  ],
  ""entry_conditions"": [
    { ""type"": ""crosses_above"", ""left"": ""short_ma"", ""right"": ""long_ma"", ""signal"": ""buy"" },
    { ""type"": ""crosses_below"", ""left"": ""short_ma"", ""right"": ""long_ma"", ""signal"": ""sell"" }
  ],
  ""exit_conditions"": []
}",
            "rsi" => @"{
  ""name"": ""RSI Strategy"",
  ""description"": ""RSI 超买超卖策略"",
  ""version"": ""1.0"",
  ""parameters"": {
    ""rsi_period"": 14,
    ""overbought"": 70,
    ""oversold"": 30,
    ""position_size"": 100
  },
  ""indicators"": [
    { ""name"": ""rsi"", ""type"": ""RSI"", ""period"": ""$rsi_period"", ""source"": ""price"" }
  ],
  ""entry_conditions"": [
    { ""type"": ""crosses_below"", ""left"": ""rsi"", ""right"": ""$oversold"", ""signal"": ""buy"" },
    { ""type"": ""crosses_above"", ""left"": ""rsi"", ""right"": ""$overbought"", ""signal"": ""sell"" }
  ],
  ""exit_conditions"": []
}",
            "macd" => @"{
  ""name"": ""MACD Strategy"",
  ""description"": ""MACD 交叉策略"",
  ""version"": ""1.0"",
  ""parameters"": {
    ""fast_period"": 12,
    ""slow_period"": 26,
    ""signal_period"": 9,
    ""position_size"": 100
  },
  ""indicators"": [
    { ""name"": ""macd"", ""type"": ""MACD"", ""fast"": ""$fast_period"", ""slow"": ""$slow_period"", ""signal"": ""$signal_period"", ""source"": ""price"" }
  ],
  ""entry_conditions"": [
    { ""type"": ""crosses_above"", ""left"": ""macd.histogram"", ""right"": 0, ""signal"": ""buy"" },
    { ""type"": ""crosses_below"", ""left"": ""macd.histogram"", ""right"": 0, ""signal"": ""sell"" }
  ],
  ""exit_conditions"": []
}",
            "breakout" => @"{
  ""name"": ""Breakout Strategy"",
  ""description"": ""价格突破策略"",
  ""version"": ""1.0"",
  ""parameters"": {
    ""lookback_period"": 20,
    ""position_size"": 100
  },
  ""indicators"": [
    { ""name"": ""high"", ""type"": ""HIGHEST"", ""period"": ""$lookback_period"", ""source"": ""price"" },
    { ""name"": ""low"", ""type"": ""LOWEST"", ""period"": ""$lookback_period"", ""source"": ""price"" }
  ],
  ""entry_conditions"": [
    { ""type"": ""greater_than"", ""left"": ""price"", ""right"": ""high"", ""signal"": ""buy"" },
    { ""type"": ""less_than"", ""left"": ""price"", ""right"": ""low"", ""signal"": ""sell"" }
  ],
  ""exit_conditions"": []
}",
            _ => @"{
  ""name"": ""My Strategy"",
  ""description"": ""策略描述"",
  ""version"": ""1.0"",
  ""parameters"": {
    ""param1"": 10
  },
  ""indicators"": [],
  ""entry_conditions"": [],
  ""exit_conditions"": []
}"
        };
    }

    private string GetPythonTemplate(string templateName)
    {
        return templateName switch
        {
            "ma_crossover" => @"from aegisquant import BaseStrategy, Signal

class MACrossoverStrategy(BaseStrategy):
    """"""双均线交叉策略""""""
    
    name = ""MA Crossover""
    description = ""双均线交叉策略""
    
    def __init__(self):
        super().__init__()
        self.short_period = 10
        self.long_period = 20
        self.position_size = 100
        self.prev_short_ma = None
        self.prev_long_ma = None
    
    def on_tick(self, ctx):
        short_ma = ctx.sma(self.short_period)
        long_ma = ctx.sma(self.long_period)
        
        if self.prev_short_ma is None:
            self.prev_short_ma = short_ma
            self.prev_long_ma = long_ma
            return Signal.NONE
        
        # 金叉买入
        if self.prev_short_ma <= self.prev_long_ma and short_ma > long_ma:
            self.prev_short_ma = short_ma
            self.prev_long_ma = long_ma
            return Signal.BUY
        
        # 死叉卖出
        if self.prev_short_ma >= self.prev_long_ma and short_ma < long_ma:
            self.prev_short_ma = short_ma
            self.prev_long_ma = long_ma
            return Signal.SELL
        
        self.prev_short_ma = short_ma
        self.prev_long_ma = long_ma
        return Signal.NONE
",
            "rsi" => @"from aegisquant import BaseStrategy, Signal

class RSIStrategy(BaseStrategy):
    """"""RSI 超买超卖策略""""""
    
    name = ""RSI Strategy""
    description = ""RSI 超买超卖策略""
    
    def __init__(self):
        super().__init__()
        self.rsi_period = 14
        self.overbought = 70
        self.oversold = 30
        self.position_size = 100
        self.prev_rsi = None
    
    def on_tick(self, ctx):
        rsi = ctx.rsi(self.rsi_period)
        
        if self.prev_rsi is None:
            self.prev_rsi = rsi
            return Signal.NONE
        
        # RSI 从超卖区回升，买入
        if self.prev_rsi <= self.oversold and rsi > self.oversold:
            self.prev_rsi = rsi
            return Signal.BUY
        
        # RSI 从超买区回落，卖出
        if self.prev_rsi >= self.overbought and rsi < self.overbought:
            self.prev_rsi = rsi
            return Signal.SELL
        
        self.prev_rsi = rsi
        return Signal.NONE
",
            _ => @"from aegisquant import BaseStrategy, Signal

class MyStrategy(BaseStrategy):
    """"""自定义策略""""""
    
    name = ""My Strategy""
    description = ""策略描述""
    
    def __init__(self):
        super().__init__()
        # 在这里定义策略参数
        self.param1 = 10
    
    def on_tick(self, ctx):
        """"""
        处理每个 tick 数据
        
        ctx.price: 当前价格
        ctx.volume: 当前成交量
        ctx.position: 当前持仓
        ctx.equity: 账户净值
        ctx.sma(period): 计算 SMA
        ctx.ema(period): 计算 EMA
        ctx.rsi(period): 计算 RSI
        
        返回: Signal.BUY, Signal.SELL, 或 Signal.NONE
        """"""
        
        # 在这里实现策略逻辑
        
        return Signal.NONE
"
        };
    }

    private void CodeEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        _isModified = true;
    }

    private void ValidateButton_Click(object sender, RoutedEventArgs e)
    {
        ValidateCode();
    }

    private bool ValidateCode()
    {
        var isJson = StrategyTypeComboBox.SelectedItem is ComboBoxItem item && 
                     item.Tag?.ToString() == "json";
        var code = CodeEditor.Text;

        try
        {
            if (isJson)
            {
                var strategy = _jsonLoader.LoadFromJson(code);
                ValidationOutput.Foreground = System.Windows.Media.Brushes.Green;
                ValidationOutput.Text = $"✓ 验证成功!\n策略名称: {strategy.Name}\n类型: {strategy.Type}\n参数数量: {strategy.Parameters.Count}";
                strategy.Dispose();
                return true;
            }
            else
            {
                var strategy = _pythonLoader.LoadFromCode(code);
                ValidationOutput.Foreground = System.Windows.Media.Brushes.Green;
                ValidationOutput.Text = $"✓ 验证成功!\n策略名称: {strategy.Name}\n类型: {strategy.Type}";
                strategy.Dispose();
                return true;
            }
        }
        catch (StrategyLoadException ex)
        {
            ValidationOutput.Foreground = System.Windows.Media.Brushes.Red;
            ValidationOutput.Text = $"✗ 验证失败 (行 {ex.LineNumber}):\n{ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            ValidationOutput.Foreground = System.Windows.Media.Brushes.Red;
            ValidationOutput.Text = $"✗ 验证失败:\n{ex.Message}";
            return false;
        }
    }

    private void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var isJson = StrategyTypeComboBox.SelectedItem is ComboBoxItem item && 
                     item.Tag?.ToString() == "json";

        var dialog = new OpenFileDialog
        {
            Filter = isJson 
                ? "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
                : "Python Files (*.py)|*.py|All Files (*.*)|*.*",
            Title = "打开策略文件"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                CodeEditor.Text = File.ReadAllText(dialog.FileName);
                _currentFilePath = dialog.FileName;
                _isModified = false;
                ValidationOutput.Text = $"已加载: {dialog.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开文件失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SaveFileButton_Click(object sender, RoutedEventArgs e)
    {
        var isJson = StrategyTypeComboBox.SelectedItem is ComboBoxItem item && 
                     item.Tag?.ToString() == "json";

        var dialog = new SaveFileDialog
        {
            Filter = isJson 
                ? "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
                : "Python Files (*.py)|*.py|All Files (*.*)|*.*",
            Title = "保存策略文件",
            FileName = _currentFilePath ?? (isJson ? "strategy.json" : "strategy.py")
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dialog.FileName, CodeEditor.Text);
                _currentFilePath = dialog.FileName;
                SavedFilePath = dialog.FileName;
                _isModified = false;
                ValidationOutput.Text = $"已保存: {dialog.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存文件失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateCode())
        {
            MessageBox.Show("请先修复验证错误", "验证失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var isJson = StrategyTypeComboBox.SelectedItem is ComboBoxItem item && 
                         item.Tag?.ToString() == "json";

            CreatedStrategy = isJson 
                ? _jsonLoader.LoadFromJson(CodeEditor.Text)
                : _pythonLoader.LoadFromCode(CodeEditor.Text);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载策略失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isModified)
        {
            var result = MessageBox.Show("当前代码已修改，是否放弃更改?", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        DialogResult = false;
        Close();
    }
}
