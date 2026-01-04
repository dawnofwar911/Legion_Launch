using System;
using System.Linq;
using System.Threading;
using Windows.Gaming.Input;
using Microsoft.UI.Dispatching;
using System.Collections.Generic;

namespace LegionDeck.GUI.Services;

public class GamepadService
{
    public static GamepadService Instance { get; private set; }

    public enum GamepadButton { Up, Down, Left, Right, A, B, X, Y, Menu, View }
    public event EventHandler<GamepadButton>? ButtonDown;

    private DispatcherQueueTimer _timer;
    private Gamepad _activeGamepad;
    private Dictionary<GamepadButtons, bool> _previousState = new();
    private Dictionary<string, bool> _stickState = new();
    private readonly DispatcherQueue _dispatcherQueue;
    private const double STICK_DEADZONE = 0.5;

    public GamepadService()
    {
        Instance = this;
        Log("GamepadService initializing...");
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        
        Gamepad.GamepadAdded += (s, e) => {
            if (_dispatcherQueue != null) {
                _dispatcherQueue.TryEnqueue(() => {
                    if (_activeGamepad == null) {
                        Log("Gamepad added.");
                        _activeGamepad = e;
                        StartPolling();
                    }
                });
            }
        };

        Gamepad.GamepadRemoved += (s, e) => {
            if (_dispatcherQueue != null) {
                _dispatcherQueue.TryEnqueue(() => {
                    if (_activeGamepad == e) {
                        Log("Gamepad removed.");
                        _activeGamepad = Gamepad.Gamepads.FirstOrDefault();
                        if (_activeGamepad == null) StopPolling();
                    }
                });
            }
        };
        
        if (Gamepad.Gamepads.Count > 0)
        {
            _activeGamepad = Gamepad.Gamepads.First();
            Log($"Gamepad found on init.");
            StartPolling();
        }
    }

    private static readonly object _logLock = new object();
    private void Log(string message)
    {
        lock (_logLock)
        {
            try
            {
                var path = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(path, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} - [GamepadService] {message}\n");
            }
            catch {{ }}
        }
    }

    private void StartPolling()
    {
        if (_dispatcherQueue == null) return;
        _dispatcherQueue.TryEnqueue(() => {
            Log("Starting polling loop.");
            if (_timer == null)
            {
                _timer = _dispatcherQueue.CreateTimer();
                _timer.Interval = TimeSpan.FromMilliseconds(16);
                _timer.Tick += Timer_Tick;
            }
            _timer.Start();
        });
    }

    private void StopPolling()
    {
        _dispatcherQueue?.TryEnqueue(() => {
            Log("Stopping polling loop.");
            _timer?.Stop();
        });
    }

    private int _tickCount = 0;
    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_activeGamepad == null) return;

        _tickCount++;
        if (_tickCount % 600 == 0) Log($"[Heartbeat] Polling active. Gamepad: {_activeGamepad.GetType().Name}");

        try 
        {
            var reading = _activeGamepad.GetCurrentReading();
            var buttons = reading.Buttons;

            CheckButton(buttons, GamepadButtons.DPadUp, GamepadButton.Up);
            CheckButton(buttons, GamepadButtons.DPadDown, GamepadButton.Down);
            CheckButton(buttons, GamepadButtons.DPadLeft, GamepadButton.Left);
            CheckButton(buttons, GamepadButtons.DPadRight, GamepadButton.Right);
            CheckStick(reading.LeftThumbstickX, reading.LeftThumbstickY);
            CheckButton(buttons, GamepadButtons.A, GamepadButton.A);
            CheckButton(buttons, GamepadButtons.B, GamepadButton.B);
            CheckButton(buttons, GamepadButtons.X, GamepadButton.X);
            CheckButton(buttons, GamepadButtons.Y, GamepadButton.Y);
            CheckButton(buttons, GamepadButtons.Menu, GamepadButton.Menu);
            CheckButton(buttons, GamepadButtons.View, GamepadButton.View);
        }
        catch { }
    }

    private void CheckStick(double x, double y)
    {
        UpdateStickState("StickUp", y > STICK_DEADZONE, GamepadButton.Up);
        UpdateStickState("StickDown", y < -STICK_DEADZONE, GamepadButton.Down);
        UpdateStickState("StickLeft", x < -STICK_DEADZONE, GamepadButton.Left);
        UpdateStickState("StickRight", x > STICK_DEADZONE, GamepadButton.Right);
    }

    private void UpdateStickState(string key, bool isPressed, GamepadButton button)
    {
        bool wasPressed = _stickState.TryGetValue(key, out var state) && state;
        if (isPressed && !wasPressed)
        {
            try { ButtonDown?.Invoke(this, button); } catch {{ }}
        }
        _stickState[key] = isPressed;
    }

    private void CheckButton(GamepadButtons currentButtons, GamepadButtons targetButton, GamepadButton mappedButton)
    {
        bool isPressed = (currentButtons & targetButton) == targetButton;
        bool wasPressed = _previousState.TryGetValue(targetButton, out var state) && state;
        if (isPressed && !wasPressed)
        {
            try { ButtonDown?.Invoke(this, mappedButton); } catch {{ }}
        }
        _previousState[targetButton] = isPressed;
    }
}
