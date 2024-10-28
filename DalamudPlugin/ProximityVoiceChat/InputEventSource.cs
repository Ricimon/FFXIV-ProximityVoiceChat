using System;
using System.Collections.Generic;
using WindowsInput;
using WindowsInput.Events;
using WindowsInput.Events.Sources;

namespace ProximityVoiceChat;

public class InputEventSource : IDisposable
{
    private readonly List<Action<KeyDown>> subscribedActions = [];

    private IKeyboardEventSource? keyboard;
    private IMouseEventSource? mouse;

    public void Dispose()
    {
        if (this.keyboard != null)
        {
            this.keyboard.KeyDown -= OnKeyboardKeyDown;
        }
        this.keyboard?.Dispose();
        if (this.mouse != null)
        {
            this.mouse.ButtonDown -= OnMouseButtonDown;
        }
        this.mouse?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void SubscribeToKeyDown(Action<KeyDown> action)
    {
        if (this.keyboard == null)
        {
            this.keyboard = Capture.Global.KeyboardAsync();
            this.keyboard.KeyDown += OnKeyboardKeyDown;
        }
        if (this.mouse == null)
        {
            this.mouse = Capture.Global.MouseAsync();
            this.mouse.ButtonDown += OnMouseButtonDown;
        }

        this.subscribedActions.Add(action);
    }

    public void UnsubscribeToKeyDown(Action<KeyDown> action)
    {
        this.subscribedActions.Remove(action);
    }

    private void OnKeyboardKeyDown(object? o, EventSourceEventArgs<KeyDown> e)
    {
        foreach(var action in this.subscribedActions)
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

        foreach (var action in this.subscribedActions)
        {
            action.Invoke(new(keyCode));
        }
    }
}
