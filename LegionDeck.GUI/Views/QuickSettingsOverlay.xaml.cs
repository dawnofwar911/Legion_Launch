using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using LegionDeck.Core.Services;
using LegionDeck.GUI.Services;
using System;
using System.IO;
using System.Runtime.InteropServices;
using WinRT.Interop;
using Microsoft.UI.Windowing;
using Microsoft.UI;

namespace LegionDeck.GUI.Views;

public sealed partial class QuickSettingsOverlay : Window
{
    public static bool IsOverlayActive { get; private set; } = false;
    private readonly AppWindow _appWindow;
    private bool _isUpdatingFromSystem = false;
    private DispatcherTimer _batteryTimer;

    public QuickSettingsOverlay()
    {
        IsOverlayActive = true;
        this.InitializeComponent();
        
        IntPtr hWnd = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        ConfigureWindow();
        LoadCurrentSettings();
        StartBatteryMonitoring();

        // Initialize volume monitoring
        SystemControlService.StartVolumeMonitoring();
        SystemControlService.OnVolumeChanged += SystemControlService_OnVolumeChanged;
        
        // Initialize brightness monitoring
        SystemControlService.StartBrightnessMonitoring();
        SystemControlService.OnBrightnessChanged += SystemControlService_OnBrightnessChanged;

        // Initialize Gamepad support
        if (GamepadService.Instance != null)
        {
            GamepadService.Instance.ButtonDown += OnGamepadButtonDown;
        }

        // Ensure we clean up event subscription when window closes (optional but good practice)
        this.Closed += (s, e) => {
            IsOverlayActive = false;
            SystemControlService.OnVolumeChanged -= SystemControlService_OnVolumeChanged;
            SystemControlService.OnBrightnessChanged -= SystemControlService_OnBrightnessChanged;
            _batteryTimer?.Stop();
            if (GamepadService.Instance != null)
            {
                GamepadService.Instance.ButtonDown -= OnGamepadButtonDown;
            }
        };
        
        // Initial Focus
        this.Activated += (s, e) => {
             // Ensure window is active and ready for focus
             this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
             {
                 VolumeSlider.Focus(FocusState.Keyboard);
             });
        };
    }

    private void OnGamepadButtonDown(object? sender, GamepadService.GamepadButton button)
    {
        if (this.Content == null || this.Content.XamlRoot == null) return;

        // Ensure we are on UI thread
        this.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (this.Content == null || this.Content.XamlRoot == null) return;
                
                var focusedElement = FocusManager.GetFocusedElement(this.Content.XamlRoot) as Control;
                
                // Fallback if focus is lost (e.g. clicking outside)
                if (focusedElement == null)
                {
                    VolumeSlider.Focus(FocusState.Keyboard);
                    focusedElement = VolumeSlider;
                }

                switch (button)
                {
                    case GamepadService.GamepadButton.Up:
                        FocusManager.TryMoveFocus(FocusNavigationDirection.Up, new FindNextElementOptions { SearchRoot = this.Content });
                        break;
                    case GamepadService.GamepadButton.Down:
                        FocusManager.TryMoveFocus(FocusNavigationDirection.Down, new FindNextElementOptions { SearchRoot = this.Content });
                        break;
                    case GamepadService.GamepadButton.Left:
                        if (focusedElement is Slider slider)
                        {
                            slider.Value -= slider.StepFrequency * 5; 
                        }
                        else
                        {
                            FocusManager.TryMoveFocus(FocusNavigationDirection.Left, new FindNextElementOptions { SearchRoot = this.Content });
                        }
                        break;
                    case GamepadService.GamepadButton.Right:
                        if (focusedElement is Slider sliderR)
                        {
                            sliderR.Value += sliderR.StepFrequency * 5;
                        }
                        else
                        {
                            FocusManager.TryMoveFocus(FocusNavigationDirection.Right, new FindNextElementOptions { SearchRoot = this.Content });
                        }
                        break;
                    case GamepadService.GamepadButton.A:
                        if (focusedElement is Button btn)
                        {
                            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(btn);
                            peer.Invoke();
                        }
                        else if (focusedElement is ToggleSwitch ts)
                        {
                            ts.IsOn = !ts.IsOn;
                        }
                        break;
                    case GamepadService.GamepadButton.B:
                        this.Close();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"Gamepad handling error: {ex.Message}");
            }
        });
    }

    private static void Log(string message)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck");
            Directory.CreateDirectory(logDir);
            var path = Path.Combine(logDir, "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [QuickSettings] {message}\n");
        }
        catch {{ }}
    }

    private void StartBatteryMonitoring()
    {
        _batteryTimer = new DispatcherTimer();
        _batteryTimer.Interval = TimeSpan.FromSeconds(30);
        _batteryTimer.Tick += (s, e) => UpdateBatteryStatus();
        _batteryTimer.Start();
    }

    private void UpdateBatteryStatus()
    {
        int percent = LenovoPowerService.GetBatteryPercentage();
        BatteryText.Text = $"{percent}%";
    }

    private void SystemControlService_OnVolumeChanged(object? sender, float newVolume)
    {
        // Marshal to UI thread
        this.DispatcherQueue.TryEnqueue(() =>
        {
            _isUpdatingFromSystem = true;
            VolumeSlider.Value = newVolume;
            _isUpdatingFromSystem = false;
        });
    }

    private void SystemControlService_OnBrightnessChanged(object? sender, int newBrightness)
    {
        // Marshal to UI thread
        this.DispatcherQueue.TryEnqueue(() =>
        {
            _isUpdatingFromSystem = true;
            BrightnessSlider.Value = newBrightness;
            _isUpdatingFromSystem = false;
        });
    }

    private void ConfigureWindow()
    {
        // Set size and position (right side of screen)
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        int width = 300;
        int height = displayArea.WorkArea.Height;
        _appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        _appWindow.Move(new Windows.Graphics.PointInt32(displayArea.WorkArea.Width - width, 0));

        // Always on top
        var presenter = _appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        // Titlebar hidden
        _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
    }

    private void LoadCurrentSettings()
    {
        BrightnessSlider.Value = SystemControlService.GetBrightness();
        
        _isUpdatingFromSystem = true;
        VolumeSlider.Value = SystemControlService.GetMasterVolume();
        _isUpdatingFromSystem = false;

        UpdateBatteryStatus();
        UpdateDisplayState();

        // Subscribe to changes
        VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
        BrightnessSlider.ValueChanged += BrightnessSlider_ValueChanged;
    }

    private void UpdateDisplayState()
    {
        var current = DisplayService.GetCurrentMode(); // e.g. "1920x1200 @ 144Hz"
        var currentPower = LenovoPowerService.GetCurrentPowerMode();

        // Update Power Profile Buttons
        foreach (var child in PowerGrid.Children)
        {
            if (child is Button btn && btn.Tag is string tag && int.TryParse(tag, out int mode))
            {
                if ((int)currentPower == mode)
                {
                    btn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
                }
                else
                {
                    btn.Style = (Style)Application.Current.Resources["DefaultButtonStyle"];
                }
            }
        }
        
        // Update Resolution Buttons
        foreach (var child in ResGrid.Children)
        {
            if (child is Button btn && btn.Tag is string tag)
            {
                var parts = tag.Split(',');
                var resStr = $"{parts[0]}x{parts[1]}";
                if (current.Contains(resStr))
                {
                    btn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
                }
                else
                {
                    btn.Style = (Style)Application.Current.Resources["DefaultButtonStyle"];
                }
            }
        }

        // Update Refresh Buttons
        foreach (var child in RefreshGrid.Children)
        {
             if (child is Button btn && btn.Tag is string tag)
             {
                 if (current.Contains($" {tag}Hz"))
                 {
                     btn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
                 }
                 else
                 {
                     btn.Style = (Style)Application.Current.Resources["DefaultButtonStyle"];
                 }
             }
        }
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingFromSystem) return;
        SystemControlService.SetMasterVolume((float)e.NewValue);
    }

    private void BrightnessSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingFromSystem) return;
        SystemControlService.SetBrightness((int)e.NewValue);
    }

    private void ResolutionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            var parts = tag.Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
            {
                // Get current Hz to maintain it
                var current = DisplayService.GetCurrentMode(); // e.g. "1920x1200 @ 144Hz"
                int hz = 60;
                if (current.Contains("@"))
                {
                    var hzPart = current.Split('@')[1].Replace("Hz", "").Trim();
                    int.TryParse(hzPart, out hz);
                }

                DisplayService.SetDisplayMode(w, h, hz);
                UpdateDisplayState();
            }
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out int hz))
        {
            // Get current resolution to maintain it
            var current = DisplayService.GetCurrentMode(); 
            if (current.Contains("x"))
            {
                var resPart = current.Split('@')[0].Trim(); // "1920x1200"
                var resParts = resPart.Split('x');
                if (resParts.Length == 2 && int.TryParse(resParts[0], out int w) && int.TryParse(resParts[1], out int h))
                {
                    DisplayService.SetDisplayMode(w, h, hz);
                    UpdateDisplayState();
                }
            }
        }
    }

    private void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int mode))
        {
            LenovoPowerService.SetPowerMode((LenovoPowerService.PowerMode)mode);
            UpdateDisplayState();
        }
    }
}