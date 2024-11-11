using System;
using System.Collections.Generic;
using WindowsInput;
using WindowsInput.Events;
using WindowsInput.Events.Sources;

namespace ProximityVoiceChat.Input;

public class InputEventSource : IDisposable
{
    private readonly List<Action<KeyDown>> subscribedKeyDownActions = [];
    private readonly List<Action<KeyUp>> subscribedKeyUpActions = [];

    private IKeyboardEventSource? keyboard;
    private IMouseEventSource? mouse;

    public void Dispose()
    {
        if (keyboard != null)
        {
            keyboard.KeyDown -= OnKeyboardKeyDown;
            keyboard.KeyUp -= OnKeyboardKeyUp;
        }
        keyboard?.Dispose();
        if (mouse != null)
        {
            mouse.ButtonDown -= OnMouseButtonDown;
            mouse.ButtonUp -= OnMouseButtonUp;
        }
        mouse?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void SubscribeToKeyDown(Action<KeyDown> action)
    {
        // One-time setup of persistent listeners
        SetupGlobalSubscriptions();

        subscribedKeyDownActions.Add(action);
    }

    public void UnsubscribeToKeyDown(Action<KeyDown> action)
    {
        subscribedKeyDownActions.Remove(action);
    }

    public void SubscribeToKeyUp(Action<KeyUp> action)
    {
        // One-time setup of persistent listeners
        SetupGlobalSubscriptions();

        subscribedKeyUpActions.Add(action);
    }

    public void UnsubscribeToKeyUp(Action<KeyUp> action)
    {
        subscribedKeyUpActions.Remove(action);
    }

    private void SetupGlobalSubscriptions()
    {
        if (keyboard == null)
        {
            keyboard = Capture.Global.KeyboardAsync();
            keyboard.KeyDown += OnKeyboardKeyDown;
            keyboard.KeyUp += OnKeyboardKeyUp;
        }
        if (mouse == null)
        {
            mouse = Capture.Global.MouseAsync();
            mouse.ButtonDown += OnMouseButtonDown;
            mouse.ButtonUp += OnMouseButtonUp;
        }
    }

    private void OnKeyboardKeyDown(object? o, EventSourceEventArgs<KeyDown> e)
    {
        foreach (var action in subscribedKeyDownActions)
        {
            action.Invoke(e.Data);
        }
    }

    private void OnKeyboardKeyUp(object? o, EventSourceEventArgs<KeyUp> e)
    {
        foreach (var action in subscribedKeyUpActions)
        {
            action.Invoke(e.Data);
        }
    }

    private void OnMouseButtonDown(object? o, EventSourceEventArgs<ButtonDown> e)
    {
        // Only accept middle mouse, mouse4, and mouse5
        KeyCode keyCode;
        if (e.Data.Button == ButtonCode.XButton1)
        {
            keyCode = KeyCode.XButton1;
        }
        else if (e.Data.Button == ButtonCode.XButton2)
        {
            keyCode = KeyCode.XButton2;
        }
        else if (e.Data.Button == ButtonCode.Middle)
        {
            keyCode = KeyCode.MButton;
        }
        else
        {
            return;
        }

        foreach (var action in subscribedKeyDownActions)
        {
            action.Invoke(new(keyCode));
        }
    }

    private void OnMouseButtonUp(object? o, EventSourceEventArgs<ButtonUp> e)
    {
        // Only accept middle mouse, mouse4, and mouse5
        KeyCode keyCode;
        if (e.Data.Button == ButtonCode.XButton1)
        {
            keyCode = KeyCode.XButton1;
        }
        else if (e.Data.Button == ButtonCode.XButton2)
        {
            keyCode = KeyCode.XButton2;
        }
        else if (e.Data.Button == ButtonCode.Middle)
        {
            keyCode = KeyCode.MButton;
        }
        else
        {
            return;
        }

        foreach (var action in subscribedKeyUpActions)
        {
            action.Invoke(new(keyCode));
        }
    }
}
