using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AegisQuant.UI.Services.Interfaces;
using AegisQuant.UI.Strategy;

namespace AegisQuant.UI.Controls;

/// <summary>
/// Status panel displaying Equity, Position, P&L, current timestamp and signal.
/// Requirements: 5.1, 5.2, 5.3, 5.4
/// </summary>
public partial class StatusPanel : UserControl
{
    private double _equity = 100000;
    private double _position = 0;
    private double _unrealizedPnL = 0;
    private double _realizedPnL = 0;
    private DateTime _currentTime;
    private Signal _currentSignal = Signal.None;

    /// <summary>
    /// Gets or sets the equity value.
    /// </summary>
    public double Equity
    {
        get => _equity;
        set
        {
            var oldValue = _equity;
            _equity = value;
            UpdateEquityDisplay(oldValue != value);
        }
    }

    /// <summary>
    /// Gets or sets the position value.
    /// </summary>
    public double Position
    {
        get => _position;
        set
        {
            _position = value;
            UpdatePositionDisplay();
        }
    }

    /// <summary>
    /// Gets or sets the unrealized P&L.
    /// </summary>
    public double UnrealizedPnL
    {
        get => _unrealizedPnL;
        set
        {
            var oldValue = _unrealizedPnL;
            _unrealizedPnL = value;
            UpdateUnrealizedPnLDisplay(oldValue != value);
        }
    }

    /// <summary>
    /// Gets or sets the realized P&L.
    /// </summary>
    public double RealizedPnL
    {
        get => _realizedPnL;
        set
        {
            var oldValue = _realizedPnL;
            _realizedPnL = value;
            UpdateRealizedPnLDisplay(oldValue != value);
        }
    }

    /// <summary>
    /// Gets or sets the current timestamp.
    /// </summary>
    public DateTime CurrentTime
    {
        get => _currentTime;
        set
        {
            _currentTime = value;
            CurrentTimeText.Text = value.ToString("yyyy-MM-dd HH:mm");
        }
    }

    /// <summary>
    /// Gets or sets the current signal.
    /// </summary>
    public Signal CurrentSignal
    {
        get => _currentSignal;
        set
        {
            _currentSignal = value;
            UpdateSignalDisplay();
        }
    }

    public StatusPanel()
    {
        InitializeComponent();
        UpdateEquityDisplay(false);
        UpdatePositionDisplay();
        UpdateUnrealizedPnLDisplay(false);
        UpdateRealizedPnLDisplay(false);
        UpdateSignalDisplay();
    }

    /// <summary>
    /// Updates the status panel with replay state info.
    /// </summary>
    public void UpdateFromReplayState(ReplayStateInfo state, DateTime? time = null)
    {
        Equity = state.Equity;
        Position = state.Position;
        UnrealizedPnL = state.UnrealizedPnL;
        RealizedPnL = state.RealizedPnL;
        
        if (time.HasValue)
        {
            CurrentTime = time.Value;
        }
    }

    /// <summary>
    /// Updates the signal display and triggers flash effect.
    /// Requirements: 5.5 - Flash to indicate trade event
    /// </summary>
    public void ShowTradeSignal(Signal signal)
    {
        CurrentSignal = signal;
        
        // Trigger flash effect on equity when trade occurs
        if (signal == Signal.Buy || signal == Signal.Sell)
        {
            EquityText.TriggerFlash(signal == Signal.Buy);
        }
    }

    /// <summary>
    /// Resets the status panel to initial state.
    /// </summary>
    public void Reset(double initialCapital = 100000)
    {
        Equity = initialCapital;
        Position = 0;
        UnrealizedPnL = 0;
        RealizedPnL = 0;
        CurrentSignal = Signal.None;
        CurrentTimeText.Text = "--:--:--";
    }

    private void UpdateEquityDisplay(bool triggerFlash)
    {
        EquityText.Text = _equity.ToString("C2");
        
        if (triggerFlash)
        {
            // Flash green if equity increased, red if decreased
            EquityText.TriggerFlash(_equity > 100000);
        }
    }

    private void UpdatePositionDisplay()
    {
        PositionText.Text = _position.ToString("F2");
        
        // Color based on position direction
        if (_position > 0)
        {
            PositionText.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else if (_position < 0)
        {
            PositionText.Foreground = (Brush)FindResource("ErrorBrush");
        }
        else
        {
            PositionText.Foreground = (Brush)FindResource("PrimaryTextBrush");
        }
    }

    private void UpdateUnrealizedPnLDisplay(bool triggerFlash)
    {
        UnrealizedPnLText.Text = (_unrealizedPnL >= 0 ? "+" : "") + _unrealizedPnL.ToString("C2");
        
        if (_unrealizedPnL >= 0)
        {
            UnrealizedPnLText.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            UnrealizedPnLText.Foreground = (Brush)FindResource("ErrorBrush");
        }
        
        if (triggerFlash)
        {
            UnrealizedPnLText.TriggerFlash(_unrealizedPnL >= 0);
        }
    }

    private void UpdateRealizedPnLDisplay(bool triggerFlash)
    {
        RealizedPnLText.Text = (_realizedPnL >= 0 ? "+" : "") + _realizedPnL.ToString("C2");
        
        if (_realizedPnL >= 0)
        {
            RealizedPnLText.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            RealizedPnLText.Foreground = (Brush)FindResource("ErrorBrush");
        }
        
        if (triggerFlash)
        {
            RealizedPnLText.TriggerFlash(_realizedPnL >= 0);
        }
    }

    private void UpdateSignalDisplay()
    {
        switch (_currentSignal)
        {
            case Signal.Buy:
                SignalText.Text = FindResource("String.Replay.Buy") as string ?? "BUY";
                SignalText.Foreground = Brushes.White;
                SignalBorder.Background = (Brush)FindResource("SuccessBrush");
                break;
                
            case Signal.Sell:
                SignalText.Text = FindResource("String.Replay.Sell") as string ?? "SELL";
                SignalText.Foreground = Brushes.White;
                SignalBorder.Background = (Brush)FindResource("ErrorBrush");
                break;
                
            case Signal.CloseLong:
                SignalText.Text = "CLOSE LONG";
                SignalText.Foreground = Brushes.White;
                SignalBorder.Background = (Brush)FindResource("WarningBrush");
                break;
                
            case Signal.CloseShort:
                SignalText.Text = "CLOSE SHORT";
                SignalText.Foreground = Brushes.White;
                SignalBorder.Background = (Brush)FindResource("WarningBrush");
                break;
                
            default:
                SignalText.Text = FindResource("String.Replay.NoSignal") as string ?? "None";
                SignalText.Foreground = (Brush)FindResource("SecondaryTextBrush");
                SignalBorder.Background = Brushes.Transparent;
                break;
        }
    }
}
