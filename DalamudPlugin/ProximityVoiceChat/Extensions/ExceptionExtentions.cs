using System;
using System.Text;

namespace ProximityVoiceChat.Extensions;

public static class ExceptionExtensions
{
    public static string ToStringFull(this Exception e)
    {
        var str = new StringBuilder($"{e.Message}\n{e.GetType()}\n{e.StackTrace}");
        var inner = e.InnerException;
        for (var i = 1; inner != null; i++)
        {
            str.Append($"\nAn inner exception ({i}) was thrown: {inner.Message}\n{inner.GetType()}{inner.StackTrace}");
            inner = inner.InnerException;
        }
        return str.ToString();
    }
}
