using System;
using System.Linq;
using System.Threading;
using Windows.Gaming.Input;
using Microsoft.UI.Dispatching;
using System.Collections.Generic;

namespace LegionDeck.GUI.Services;

public class GamepadService
{
    private DispatcherQueueTimer _timer;
    private Gamepad _activeGamepad;
    private Dictionary<GamepadButtons, bool> _previousState = new();
    private Dictionary<string, bool> _stickState = new();
    private readonly DispatcherQueue _dispatcherQueue;
    
    // Virtual Keys
    private const ushort VK_UP = 0x26;
    private const ushort VK_DOWN = 0x28;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_RETURN = 0x0D; // Enter
    private const ushort VK_BACK = 0x08;   // Backspace
    private const ushort VK_TAB = 0x09;
    private const double STICK_DEADZONE = 0.5;

    public GamepadService()
    {
        Log("GamepadService initializing...");
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        Gamepad.GamepadAdded += Gamepad_GamepadAdded;
        Gamepad.GamepadRemoved += Gamepad_GamepadRemoved;
        
        // Check if one is already connected
        if (Gamepad.Gamepads.Count > 0)
        {
            _activeGamepad = Gamepad.Gamepads.First();
            Log($"Gamepad found on init: {_activeGamepad.GetType().Name}");
            StartPolling();
        }
        else
        {
            Log("No gamepads found on init.");
        }
    }

    private void Log(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            System.IO.File.AppendAllText(path, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} - [GamepadService] {message}\n");
        }
        catch {{ }}
    }

    private void Gamepad_GamepadAdded(object sender, Gamepad e)
    {
        _dispatcherQueue.TryEnqueue(() => 
        {
            if (_activeGamepad == null)
            {
                Log("Gamepad added event received.");
                _activeGamepad = e;
                StartPolling();
            }
        });
    }

    private void Gamepad_GamepadRemoved(object sender, Gamepad e)
    {
        _dispatcherQueue.TryEnqueue(() => 
        {
            if (_activeGamepad == e)
            {
                Log("Gamepad removed.");
                _activeGamepad = null;
                _activeGamepad = Gamepad.Gamepads.FirstOrDefault();
                if (_activeGamepad == null) StopPolling();
                else Log("Switched to another gamepad.");
            }
        });
    }

    private void StartPolling()
    {
        Log("Starting polling loop.");
        if (_timer == null)
        {
            _timer = _dispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
            _timer.Tick += Timer_Tick;
        }
        _timer.Start();
    }

    private void StopPolling()
    {
        Log("Stopping polling loop.");
        _timer?.Stop();
    }

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_activeGamepad == null) return;

        try 
        {
            var reading = _activeGamepad.GetCurrentReading();
            var buttons = reading.Buttons;

            // D-Pad
            CheckButton(buttons, GamepadButtons.DPadUp, VK_UP, "DPadUp");
            CheckButton(buttons, GamepadButtons.DPadDown, VK_DOWN, "DPadDown");
            CheckButton(buttons, GamepadButtons.DPadLeft, VK_LEFT, "DPadLeft");
            CheckButton(buttons, GamepadButtons.DPadRight, VK_RIGHT, "DPadRight");
            
            // Thumbstick Emulation
            CheckStick(reading.LeftThumbstickX, reading.LeftThumbstickY);

            // Buttons
            CheckButton(buttons, GamepadButtons.A, VK_RETURN, "A");
            CheckButton(buttons, GamepadButtons.B, 0x1B, "B"); // Escape
            CheckButton(buttons, GamepadButtons.X, 0x58, "X");
            CheckButton(buttons, GamepadButtons.Y, 0x59, "Y");
            CheckButton(buttons, GamepadButtons.Menu, 0x4D, "Menu");
        }
        catch (Exception ex)
        {
             Log($"Error in Timer_Tick: {ex.Message}");
        }
    }

    private void CheckStick(double x, double y)
    {
        // Up
        bool isUp = y > STICK_DEADZONE;
        UpdateStickState("StickUp", isUp, VK_UP);

        // Down
        bool isDown = y < -STICK_DEADZONE;
        UpdateStickState("StickDown", isDown, VK_DOWN);

        // Left
        bool isLeft = x < -STICK_DEADZONE;
        UpdateStickState("StickLeft", isLeft, VK_LEFT);

        // Right
        bool isRight = x > STICK_DEADZONE;
        UpdateStickState("StickRight", isRight, VK_RIGHT);
    }

    private void UpdateStickState(string key, bool isPressed, ushort vk)
    {
        bool wasPressed = _stickState.TryGetValue(key, out var state) && state;
        
        if (isPressed && !wasPressed)
        {
            // Entering active zone -> Press Key
            Log($"Stick {key} Active. Sending Key: {vk}");
            NativeMethods.SendKey(vk);
        }
        
        _stickState[key] = isPressed;
    }

    private void CheckButton(GamepadButtons currentButtons, GamepadButtons targetButton, ushort vk, string name)
    {
        bool isPressed = (currentButtons & targetButton) == targetButton;
        bool wasPressed = _previousState.TryGetValue(targetButton, out var state) && state;

        if (isPressed && !wasPressed)
        {
            Log($"Button Pressed: {name}. Sending Key: {vk}");
            NativeMethods.SendKey(vk);
        }

        _previousState[targetButton] = isPressed;
    }
}