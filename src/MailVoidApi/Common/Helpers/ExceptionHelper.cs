
using System.Runtime.Serialization;
namespace System;

/// <summary>
/// Exception helper class.
/// </summary>
internal class ExceptionHelper
{
    /// <summary>
    /// Throws NullReferenceException if true with message.
    /// </summary>
    internal static void ThrowIfNull(bool throwEx, string exMessage)
    {
        if (throwEx)
        {
            throw new NullReferenceException(exMessage);
        }
    }

    /// <summary>
    /// Throws ArgumentNullException if true with message.
    /// </summary>
    internal static void ThrowIfArgumentNull(bool throwEx, string exMessage)
    {
        if (throwEx)
        {
            throw new ArgumentNullException(exMessage);
        }
    }

    /// <summary>
    /// Throws EmptyException if true with message.
    /// </summary>
    internal static void ThrowIfEmpty(bool throwEx, string exMessage)
    {
        if (throwEx)
        {
            throw new EmptyException(exMessage);
        }
    }

    /// <summary>
    /// Throws ArgumentOutOfRangeException if true with message.
    /// </summary>
    internal static void ThrowIfArgumentOutOfRange(bool throwEx, string exMessage)
    {
        if (throwEx)
        {
            throw new ArgumentOutOfRangeException(exMessage);
        }
    }
}

[Serializable]
public class EmptyException : Exception
{
    public EmptyException() { }
    public EmptyException(string message)
        : base(message) { }
    public EmptyException(string message, Exception inner)
        : base(message, inner) { }
    protected EmptyException(SerializationInfo info, StreamingContext context)
        : base(info, context) { }
}
