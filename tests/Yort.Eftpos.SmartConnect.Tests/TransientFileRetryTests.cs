using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Yort.Eftpos.SmartConnect.Internal;

namespace Yort.Eftpos.SmartConnect.Tests;

/// <summary>
/// Tests for the bounded transient-IO retry helper (ADR Decision 10, G4/G5; async per Task 12.5). Transient
/// is defined affirmatively — sharing violation (0x80070020) and lock violation (0x80070021) only — with a
/// conservative default of NO retry for anything else. All tests are deterministic: constructed exceptions,
/// injected delays, no real file locks.
/// </summary>
public class TransientFileRetryTests
{
	private const int SharingViolation = unchecked((int)0x80070020);
	private const int LockViolation = unchecked((int)0x80070021);
	private const int DiskFull = unchecked((int)0x80070070);

	private static IOException Transient() => new IOException("in use", SharingViolation);

	[Fact]
	public async Task Execute_TransientFailureThenSuccess_SucceedsWithTwoAttempts()
	{
		var attempts = 0;
		await TransientFileRetry.ExecuteAsync(() =>
		{
			attempts++;
			if (attempts == 1)
			{
				throw Transient();
			}

			return Task.CompletedTask;
		}, 3, TimeSpan.Zero, onRetry: null);

		Assert.Equal(2, attempts);
	}

	[Fact]
	public async Task Execute_LockViolationThenSuccess_AlsoRetries()
	{
		var attempts = 0;
		await TransientFileRetry.ExecuteAsync(() =>
		{
			attempts++;
			if (attempts == 1)
			{
				throw new IOException("locked", LockViolation);
			}

			return Task.CompletedTask;
		}, 3, TimeSpan.Zero, onRetry: null);

		Assert.Equal(2, attempts);
	}

	[Fact]
	public async Task Execute_PersistentTransientFailure_ThrowsAfterBoundedAttempts()
	{
		var attempts = 0;
		var thrown = await Assert.ThrowsAsync<IOException>(() => TransientFileRetry.ExecuteAsync(() =>
		{
			attempts++;
			throw Transient();
		}, 3, TimeSpan.Zero, onRetry: null));

		Assert.Equal(3, attempts);
		Assert.Equal(SharingViolation, thrown.HResult);
	}

	[Fact]
	public async Task Execute_DiskFull_ThrowsImmediately_NoRetry()
	{
		// Disk-full is an IOException but retrying cannot free disk — the pre-sized sentinel exists
		// for this case. Zero retries.
		var attempts = 0;
		await Assert.ThrowsAsync<IOException>(() => TransientFileRetry.ExecuteAsync(() =>
		{
			attempts++;
			throw new IOException("disk full", DiskFull);
		}, 3, TimeSpan.Zero, onRetry: null));

		Assert.Equal(1, attempts);
	}

	[Fact]
	public async Task Execute_UnrecognisedIOException_ThrowsImmediately_ConservativeDefault()
	{
		// The load-bearing invariant: anything not affirmatively classified transient gets NO retry.
		var attempts = 0;
		await Assert.ThrowsAsync<IOException>(() => TransientFileRetry.ExecuteAsync(() =>
		{
			attempts++;
			throw new IOException("novel failure");
		}, 3, TimeSpan.Zero, onRetry: null));

		Assert.Equal(1, attempts);
	}

	[Fact]
	public async Task Execute_UnauthorizedAccess_PassesThroughUntouched()
	{
		var attempts = 0;
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => TransientFileRetry.ExecuteAsync(() =>
		{
			attempts++;
			throw new UnauthorizedAccessException("denied");
		}, 3, TimeSpan.Zero, onRetry: null));

		Assert.Equal(1, attempts);
	}

	[Fact]
	public async Task Execute_DirectoryNotFound_PassesThroughUntouched()
	{
		var attempts = 0;
		await Assert.ThrowsAsync<DirectoryNotFoundException>(() => TransientFileRetry.ExecuteAsync(() =>
		{
			attempts++;
			throw new DirectoryNotFoundException("gone");
		}, 3, TimeSpan.Zero, onRetry: null));

		Assert.Equal(1, attempts);
	}

	[Fact]
	public async Task Execute_OnRetryCallback_ReceivesAttemptNumberAndException()
	{
		var notifications = new List<(int Attempt, IOException Exception)>();
		await TransientFileRetry.ExecuteAsync(
			new FailTwiceThenSucceed().InvokeAsync,
			3,
			TimeSpan.Zero,
			onRetry: (attempt, ex) => notifications.Add((attempt, ex)));

		Assert.Equal(2, notifications.Count);
		Assert.Equal(1, notifications[0].Attempt);
		Assert.Equal(2, notifications[1].Attempt);
		Assert.All(notifications, n => Assert.Equal(SharingViolation, n.Exception.HResult));
	}

	[Fact]
	public void Execute_RetryDelay_DoesNotBlockTheCaller()
	{
		// (H2) The motivating behaviour of Task 12.5: the old Thread.Sleep implementation completed the
		// whole retry (including the wait) before returning, so this assertion line would only execute
		// after the delay with a COMPLETED task. The async implementation returns at the first
		// Task.Delay await — incomplete.
		var attempts = 0;
		var task = TransientFileRetry.ExecuteAsync(() =>
		{
			attempts++;
			if (attempts == 1)
			{
				throw Transient();
			}

			return Task.CompletedTask;
		}, 3, TimeSpan.FromMilliseconds(200), onRetry: null);

		Assert.False(task.IsCompleted);
	}

	private sealed class FailTwiceThenSucceed
	{
		private int _calls;

		public Task InvokeAsync()
		{
			_calls++;
			if (_calls <= 2)
			{
				throw Transient();
			}

			return Task.CompletedTask;
		}
	}
}
