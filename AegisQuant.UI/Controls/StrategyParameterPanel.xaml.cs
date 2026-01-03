using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AegisQuant.UI.Strategy;
using AegisQuant.UI.Strategy.Models;

namespace AegisQuant.UI.Controls;

/// <summary>
/// View model for a single parameter in the panel.
/// </summary>
public class ParameterViewModel : INotifyPropertyChanged
{
    private string _value = string.Empty;
    private string? _validationError;

    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ParameterType { get; set; } = "string";
    public object? MinValue { get; set; }
    public object? MaxValue { get; set; }
    public object? DefaultValue { get; set; }
    public bool IsEditable { get; set; } = true;

    public string Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
                Validate();
            }
        }
    }

    public string? ValidationError
    {
        get => _validationError;
        set
        {
            if (_validationError != value)
            {
                _validationError = value;
                OnPropertyChanged(nameof(ValidationError));
                OnPropertyChanged(nameof(IsValid));
            }
        }
    }

    public bool IsValid => string.IsNullOrEmpty(ValidationError);
    public bool HasDescription => !string.IsNullOrEmpty(Description);

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Validate()
    {
        ValidationError = null;

        if (string.IsNullOrWhiteSpace(Value))
        {
            ValidationError = $"{DisplayName} is required";
            return;
        }

        switch (ParameterType.ToLowerInvariant())
        {
            case "int":
            case "integer":
                if (!int.TryParse(Value, out var intVal))
                {
                    ValidationError = $"{DisplayName} must be an integer";
                }
                else if (MinValue != null && intVal < Convert.ToInt32(MinValue))
                {
                    ValidationError = $"{DisplayName} must be >= {MinValue}";
                }
                else if (MaxValue != null && intVal > Convert.ToInt32(MaxValue))
                {
                    ValidationError = $"{DisplayName} must be <= {MaxValue}";
                }
                break;

            case "double":
            case "float":
            case "number":
                if (!double.TryParse(Value, out var doubleVal))
                {
                    ValidationError = $"{DisplayName} must be a number";
                }
                else if (MinValue != null && doubleVal < Convert.ToDouble(MinValue))
                {
                    ValidationError = $"{DisplayName} must be >= {MinValue}";
                }
                else if (MaxValue != null && doubleVal > Convert.ToDouble(MaxValue))
                {
                    ValidationError = $"{DisplayName} must be <= {MaxValue}";
                }
                break;
        }
    }

    public object? GetTypedValue()
    {
        return ParameterType.ToLowerInvariant() switch
        {
            "int" or "integer" => int.TryParse(Value, out var i) ? i : null,
            "double" or "float" or "number" => double.TryParse(Value, out var d) ? d : null,
            "bool" or "boolean" => bool.TryParse(Value, out var b) ? b : null,
            _ => Value
        };
    }
}

/// <summary>
/// Dynamic parameter editing panel for strategies.
/// </summary>
public partial class StrategyParameterPanel : UserControl
{
    private ObservableCollection<ParameterViewModel> _parameters = new();

    public event EventHandler<ParameterChangedEventArgs>? ParameterChanged;
    public event EventHandler<ValidationChangedEventArgs>? ValidationChanged;

    public StrategyParameterPanel()
    {
        InitializeComponent();
        ParametersItemsControl.ItemsSource = _parameters;
    }

    /// <summary>
    /// Gets whether all parameters are valid.
    /// </summary>
    public bool IsValid => _parameters.All(p => p.IsValid);

    /// <summary>
    /// Gets the current parameter values.
    /// </summary>
    public Dictionary<string, object> GetParameterValues()
    {
        return _parameters
            .Where(p => p.GetTypedValue() != null)
            .ToDictionary(p => p.Name, p => p.GetTypedValue()!);
    }

    /// <summary>
    /// Loads parameters from a strategy.
    /// </summary>
    public void LoadFromStrategy(IStrategy strategy)
    {
        _parameters.Clear();

        foreach (var kvp in strategy.Parameters)
        {
            var vm = new ParameterViewModel
            {
                Name = kvp.Key,
                DisplayName = FormatDisplayName(kvp.Key),
                Value = kvp.Value?.ToString() ?? string.Empty,
                DefaultValue = kvp.Value,
                ParameterType = InferType(kvp.Value)
            };
            _parameters.Add(vm);
        }

        UpdateVisibility();
    }

    /// <summary>
    /// Loads parameters from strategy info.
    /// </summary>
    public void LoadFromStrategyInfo(StrategyInfo info)
    {
        _parameters.Clear();

        foreach (var kvp in info.Parameters)
        {
            var param = kvp.Value;
            var vm = new ParameterViewModel
            {
                Name = kvp.Key,
                DisplayName = param.DisplayName ?? FormatDisplayName(kvp.Key),
                Description = param.Description ?? string.Empty,
                Value = param.EffectiveValue?.ToString() ?? string.Empty,
                DefaultValue = param.DefaultValue,
                ParameterType = param.Type ?? "string",
                MinValue = param.MinValue,
                MaxValue = param.MaxValue
            };
            _parameters.Add(vm);
        }

        UpdateVisibility();
    }

    /// <summary>
    /// Clears all parameters.
    /// </summary>
    public void Clear()
    {
        _parameters.Clear();
        UpdateVisibility();
    }

    /// <summary>
    /// Sets whether parameters can be edited.
    /// </summary>
    public void SetEditable(bool editable)
    {
        foreach (var param in _parameters)
        {
            param.IsEditable = editable;
        }
    }

    private void UpdateVisibility()
    {
        if (_parameters.Count == 0)
        {
            ParametersItemsControl.Visibility = Visibility.Collapsed;
            NoParametersText.Visibility = Visibility.Visible;
        }
        else
        {
            ParametersItemsControl.Visibility = Visibility.Visible;
            NoParametersText.Visibility = Visibility.Collapsed;
        }
    }

    private void ParameterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.Tag is string paramName)
        {
            var param = _parameters.FirstOrDefault(p => p.Name == paramName);
            if (param != null)
            {
                ParameterChanged?.Invoke(this, new ParameterChangedEventArgs(paramName, param.GetTypedValue()));
                UpdateValidationDisplay();
            }
        }
    }

    private void UpdateValidationDisplay()
    {
        var errors = _parameters.Where(p => !p.IsValid).Select(p => p.ValidationError).ToList();

        if (errors.Count > 0)
        {
            ValidationText.Text = string.Join("\n", errors);
            ValidationBorder.Visibility = Visibility.Visible;
        }
        else
        {
            ValidationBorder.Visibility = Visibility.Collapsed;
        }

        ValidationChanged?.Invoke(this, new ValidationChangedEventArgs(IsValid, errors!));
    }

    private static string FormatDisplayName(string name)
    {
        // Convert camelCase or snake_case to Title Case
        var result = System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
        result = result.Replace("_", " ");
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(result.ToLower());
    }

    private static string InferType(object? value)
    {
        return value switch
        {
            int => "int",
            double or float => "double",
            bool => "bool",
            _ => "string"
        };
    }
}

public class ParameterChangedEventArgs : EventArgs
{
    public string ParameterName { get; }
    public object? NewValue { get; }

    public ParameterChangedEventArgs(string parameterName, object? newValue)
    {
        ParameterName = parameterName;
        NewValue = newValue;
    }
}

public class ValidationChangedEventArgs : EventArgs
{
    public bool IsValid { get; }
    public IReadOnlyList<string> Errors { get; }

    public ValidationChangedEventArgs(bool isValid, IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }
}
