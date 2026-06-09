using System;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// The base type for all operational exceptions thrown by this library. Catching this type catches
/// everything the library can throw at runtime other than argument-validation and
/// <see cref="ObjectDisposedException"/> (programming errors), and exceptions thrown by consumer-supplied
/// callbacks (e.g. <see cref="SmartConnectClientConfiguration.AuthorizeRequestAsync"/>), which propagate as-is.
/// </summary>
public class SmartConnectException : Exception
{
	/// <summary>Creates the exception with no message.</summary>
	public SmartConnectException()
	{
	}

	/// <summary>Creates the exception with the given message.</summary>
	public SmartConnectException(string message) : base(message)
	{
	}

	/// <summary>Creates the exception with the given message and underlying cause.</summary>
	public SmartConnectException(string message, Exception innerException) : base(message, innerException)
	{
	}
}
