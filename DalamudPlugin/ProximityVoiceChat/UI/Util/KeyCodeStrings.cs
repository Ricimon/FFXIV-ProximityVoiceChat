using WindowsInput.Events;

namespace ProximityVoiceChat.UI.Util;

public class KeyCodeStrings
{
    public static string TranslateKeyCode(KeyCode keyCode)
    {
        switch(keyCode)
        {
            case KeyCode.None:
                return "Not set";
            case KeyCode.MButton:
                return "MouseMiddle";
            case KeyCode.XButton1:
                return "Mouse5";
            case KeyCode.XButton2:
                return "Mouse4";
            case KeyCode.LMenu:
                return "LAlt";
            case KeyCode.RMenu:
                return "RAlt";
            case KeyCode.Oem1:
                return ";";
            case KeyCode.Oem3:
                return "`";
            case KeyCode.Oem4:
                return "[";
            case KeyCode.Oem5:
                return "\\";
            case KeyCode.Oem6:
                return "]";
            case KeyCode.Oem7:
                return "'";
            case KeyCode.OemMinus:
                return "-";
            case KeyCode.Oemplus:
                return "=";
            case KeyCode.Oemcomma:
                return ",";
            case KeyCode.OemPeriod:
                return ".";
            case KeyCode.OemQuestion:
                return "/";
            case KeyCode.Decimal:
                return "NumPad.";
            case KeyCode.Add:
                return "NumPad+";
            case KeyCode.Subtract:
                return "NumPad-";
            case KeyCode.Divide:
                return "NumPad/";
            case KeyCode.Multiply:
                return "NumPad*";
            case KeyCode.Next:
                return "PageDown";
            case KeyCode.Scroll:
                return "ScrollLock";
            default:
                return keyCode.ToString();
        }
    }
}
