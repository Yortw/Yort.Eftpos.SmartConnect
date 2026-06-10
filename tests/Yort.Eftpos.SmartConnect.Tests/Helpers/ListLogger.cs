using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Yort.Eftpos.SmartConnect.Tests.Helpers;

/// <summary>
/// A trivial <see cref="ILogger"/> capturing entries for assertions — including the raw STATE key-value
/// pairs, so tests can assert structured-template shape (H3) and sweep template ARGUMENTS for secrets, not
/// just the formatted message (a token passed as an arg whose placeholder is omitted from the template never
/// renders into the message but still reaches structured providers).
/// </summary>
public sealed class ListLogger : ILogger
{
	public List<(LogLevel Level, string Message, Exception? Exception, IReadOnlyList<KeyValuePair<string, object?>> State)> Entries { get; }
		= new List<(LogLevel, string, Exception?, IReadOnlyList<KeyValuePair<string, object?>>)>();

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		var pairs = state as IEnumerable<KeyValuePair<string, object?>>;
		Entries.Add((logLevel, formatter(state, exception), exception, pairs?.ToList() ?? (IReadOnlyList<KeyValuePair<string, object?>>)Array.Empty<KeyValuePair<string, object?>>()));
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
