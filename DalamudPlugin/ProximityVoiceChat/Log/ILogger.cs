namespace ProximityVoiceChat.Log;

public interface ILogger
{
    void Trace(string message, params object[] values);
    void Debug(string message, params object[] values);
    void Info(string message, params object[] values);
    void Warn(string message, params object[] values);
    void Error(string message, params object[] values);
    void Fatal(string message, params object[] values);
}
