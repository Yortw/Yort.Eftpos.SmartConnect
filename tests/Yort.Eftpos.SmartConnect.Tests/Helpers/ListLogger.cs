using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Yort.Eftpos.SmartConnect.Tests.Helpers;

/// <summary>A trivial <see cref="ILogger"/> capturing entries for assertions.</summary>
public sealed class ListLogger : ILogger
{
	public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new List<(LogLevel, string, Exception?)>();

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		Entries.Add((logLevel, formatter(state, exception), exception));
	}
}

/// <summary>An <see cref="ILogger"/> that always throws — for asserting diagnostics never fail the caller.</summary>
public sealed class ThrowingLogger : ILogger
{
	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		throw new InvalidOperationException("logger failure");
	}
}
