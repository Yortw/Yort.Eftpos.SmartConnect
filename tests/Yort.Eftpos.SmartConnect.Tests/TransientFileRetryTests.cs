using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Yort.Eftpos.SmartConnect.Internal;

namespace Yort.Eftpos.SmartConnect.Tests;

/// <summary>
/// Tests for the bounded transient-IO retry helper (ADR Decision 10, G4/G5). Transient is defined
/// affirmatively — sharing violation (0x80070020) and lock violation (0x80070021) only — with a
/// conservative default of NO retry for anything else. All tests are deterministic: constructed
/// exceptions, injected zero delay, no real file locks.
/// </summary>
public class TransientFileRetryTests
{
	private const int SharingViolation = unchecked((int)0x80070020);
	private const int LockViolation = unchecked((int)0x80070021);
	private const int DiskFull = unchecked((int)0x80070070);

	private static IOException Transient() => new IOException("in use", SharingViolation);

	[Fact]
	public void Execute_TransientFailureThenSuccess_SucceedsWithTwoAttempts()
	{
		var attempts = 0;
		TransientFileRetry.Execute(() =>
		{
			attempts++;
			if (attempts == 1)
			{
				throw Transient();
			}
		}, 3, TimeSpan.Zero, onRetry: null);

		Assert.Equal(2, attempts);
	}

	[Fact]
	public void Execute_LockViolationThenSuccess_AlsoRetries()
	{
		var attempts = 0;
		TransientFileRetry.Execute(() =>
		{
			attempts++;
			if (attempts == 1)
			{
				throw new IOException("locked", LockViolation);
			}
		}, 3, TimeSpan.Zero, onRetry: null);

		Assert.Equal(2, attempts);
	}

	[Fact]
	public void Execute_PersistentTransientFailure_ThrowsAfterBoundedAttempts()
	{
		var attempts = 0;
		var thrown = Assert.Throws<IOException>(() => TransientFileRetry.Execute(() =>
		{
			attempts++;
			throw Transient();
		}, 3, TimeSpan.Zero, onRetry: null));

		Assert.Equal(3, attempts);
		Assert.Equal(SharingViolation, thrown.HResult);
	}

	[Fact]
	public void Execute_DiskFull_ThrowsImmediately_NoRetry()
	{
		// Disk-full is an IOException but retrying cannot free disk — the pre-sized sentinel exists
		// for this case. Zero retries.
		var attempts = 0;
		Assert.Throws<IOException>(() => TransientFileRetry.Execute(() =>
		{
			attempts++;
			throw new IOException("disk full", DiskFull);
		}, 3, TimeSpan.Zero, onRetry: null));

		Assert.Equal(1, attempts);
	}

	[Fact]
	public void Execute_UnrecognisedIOException_ThrowsImmediately_ConservativeDefault()
	{
		// The load-bearing invariant: anything not affirmatively classified transient gets NO retry.
		var attempts = 0;
		Assert.Throws<IOException>(() => TransientFileRetry.Execute(() =>
		{
			attempts++;
			throw new IOException("novel failure");
		}, 3, TimeSpan.Zero, onRetry: null));

		Assert.Equal(1, attempts);
	}

	[Fact]
	public void Execute_UnauthorizedAccess_PassesThroughUntouched()
	{
		var attempts = 0;
		Assert.Throws<UnauthorizedAccessException>(() => TransientFileRetry.Execute(() =>
		{
			attempts++;
			throw new UnauthorizedAccessException("denied");
		}, 3, TimeSpan.Zero, onRetry: null));

		Assert.Equal(1, attempts);
	}

	[Fact]
	public void Execute_DirectoryNotFound_PassesThroughUntouched()
	{
		var attempts = 0;
		Assert.Throws<DirectoryNotFoundException>(() => TransientFileRetry.Execute(() =>
		{
			attempts++;
			throw new DirectoryNotFoundException("gone");
		}, 3, TimeSpan.Zero, onRetry: null));

		Assert.Equal(1, attempts);
	}

	[Fact]
	public void Execute_OnRetryCallback_ReceivesAttemptNumberAndException()
	{
		var notifications = new List<(int Attempt, IOException Exception)>();
		TransientFileRetry.Execute(
			new FailTwiceThenSucceed().Invoke,
			3,
			TimeSpan.Zero,
			onRetry: (attempt, ex) => notifications.Add((attempt, ex)));

		Assert.Equal(2, notifications.Count);
		Assert.Equal(1, notifications[0].Attempt);
		Assert.Equal(2, notifications[1].Attempt);
		Assert.All(notifications, n => Assert.Equal(SharingViolation, n.Exception.HResult));
	}

	private sealed class FailTwiceThenSucceed
	{
		private int _calls;

		public void Invoke()
		{
			_calls++;
			if (_calls <= 2)
			{
				throw Transient();
			}
		}
	}
}
