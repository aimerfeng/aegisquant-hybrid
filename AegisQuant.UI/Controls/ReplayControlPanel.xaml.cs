using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AegisQuant.UI.Services.Interfaces;

namespace AegisQuant.UI.Controls;

/// <summary>
/// Replay control panel with Play, Pause, Step Forward, Step Backward buttons and time slider.
/// Requirements: 4.1 - Display replay control buttons
/// </summary>
public partial class ReplayControlPanel : UserControl
{
    private IReplayService? _replayService;
    private bool _isDragging = false;
    private bool _wasPlayingBeforeDrag = false;

    /// <summary>
    /// Event raised when play is requested.
    /// </summary>
    public event EventHandler? PlayRequested;

    /// <summary>
    /// Event raised when pause is requested.
    /// </summary>
    public event EventHandler? PauseRequested;

    /// <summary>
    /// Event raised when step forward is requested.
    /// </summary>
    public event EventHandler? StepForwardRequested;

    /// <summary>
    /// Event raised when step backward is requested.
    /// </summary>
    public event EventHandler? StepBackwardRequested;

    /// <summary>
    /// Event raised when reset is requested.
    /// </summary>
    public event EventHandler? ResetRequested;

    /// <summary>
    /// Event raised when seek is requested.
    /// </summary>
    public event EventHandler<int>? SeekRequested;

    /// <summary>
    /// Event raised when playback speed changes.
    /// </summary>
    public event EventHandler<int>? SpeedChanged;

    /// <summary>
    /// Gets or sets whether the replay is currently playing.
    /// </summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            _isPlaying = value;
            UpdatePlayPauseButton();
        }
    }
    private bool _isPlaying = false;

    /// <summary>
    /// Gets or sets the current position index.
    /// </summary>
    public int CurrentIndex
    {
        get => _currentIndex;
        set
        {
            _currentIndex = value;
            UpdatePositionDisplay();
            if (!_isDragging)
            {
                UpdateSliderPosition();
            }
        }
    }
    private int _currentIndex = 0;

    /// <summary>
    /// Gets or sets the total number of bars.
    /// </summary>
    public int TotalBars
    {
        get => _totalBars;
        set
        {
            _totalBars = value;
            TimeSlider.Maximum = Math.Max(0, value - 1);
            UpdatePositionDisplay();
        }
    }
    private int _totalBars = 0;

    /// <summary>
    /// Gets or sets the current timestamp display.
    /// </summary>
    public DateTime CurrentTime
    {
        get => _currentTime;
        set
        {
            _currentTime = value;
            TimeText.Text = value.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
    private DateTime _currentTime;

    /// <summary>
    /// Gets or sets the playback speed in milliseconds per bar.
    /// </summary>
    public int PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            _playbackSpeed = value;
            SelectSpeedInComboBox(value);
        }
    }
    private int _playbackSpeed = 500;

    public ReplayControlPanel()
    {
        InitializeComponent();
        UpdatePlayPauseButton();
        UpdatePositionDisplay();
    }

    /// <summary>
    /// Sets the replay service for direct integration.
    /// </summary>
    public void SetReplayService(IReplayService replayService)
    {
        _replayService = replayService;
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsPlaying)
        {
            PauseRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            PlayRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void StepForwardButton_Click(object sender, RoutedEventArgs e)
    {
        StepForwardRequested?.Invoke(this, EventArgs.Empty);
    }

    private void StepBackwardButton_Click(object sender, RoutedEventArgs e)
    {
        StepBackwardRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        ResetRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeedComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tagStr)
        {
            if (int.TryParse(tagStr, out int speed))
            {
                _playbackSpeed = speed;
                SpeedChanged?.Invoke(this, speed);
            }
        }
    }

    private void TimeSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _wasPlayingBeforeDrag = IsPlaying;
        
        // Pause during drag
        if (IsPlaying)
        {
            PauseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void TimeSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            
            // Seek to the new position
            int targetIndex = (int)TimeSlider.Value;
            SeekRequested?.Invoke(this, targetIndex);
            
            // Resume playing if it was playing before drag
            if (_wasPlayingBeforeDrag)
            {
                PlayRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Only update position display during drag, don't trigger seek
        if (_isDragging)
        {
            int position = (int)e.NewValue;
            PositionText.Text = $"{position} / {TotalBars}";
        }
    }

    private void UpdatePlayPauseButton()
    {
        if (IsPlaying)
        {
            PlayPauseIcon.Text = "⏸";
            PlayPauseButton.ToolTip = FindResource("String.Replay.Pause");
        }
        else
        {
            PlayPauseIcon.Text = "▶";
            PlayPauseButton.ToolTip = FindResource("String.Replay.Play");
        }
    }

    private void UpdatePositionDisplay()
    {
        PositionText.Text = $"{CurrentIndex} / {TotalBars}";
    }

    private void UpdateSliderPosition()
    {
        if (TotalBars > 0)
        {
            TimeSlider.Value = Math.Min(CurrentIndex, TotalBars - 1);
        }
    }

    private void SelectSpeedInComboBox(int speed)
    {
        foreach (ComboBoxItem item in SpeedComboBox.Items)
        {
            if (item.Tag is string tagStr && int.TryParse(tagStr, out int itemSpeed))
            {
                if (itemSpeed == speed)
                {
                    SpeedComboBox.SelectedItem = item;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Enables or disables all controls.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        PlayPauseButton.IsEnabled = enabled;
        StepForwardButton.IsEnabled = enabled;
        StepBackwardButton.IsEnabled = enabled;
        ResetButton.IsEnabled = enabled;
        TimeSlider.IsEnabled = enabled;
        SpeedComboBox.IsEnabled = enabled;
    }
}
