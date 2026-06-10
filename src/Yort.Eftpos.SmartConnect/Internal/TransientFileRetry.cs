using System;
using System.IO;
using System.Threading.Tasks;

namespace Yort.Eftpos.SmartConnect.Internal;

/// <summary>
/// Bounded retry for file IO, retrying ONLY affirmatively-classified transient failures (ADR Decision 10):
/// sharing violation (<c>0x80070020</c>) and lock violation (<c>0x80070021</c>) — the AV-scanner/backup/
/// indexer cases. Everything else throws immediately: disk-full is an <see cref="IOException"/> but
/// retrying cannot free disk, and the conservative default for unrecognised shapes is no retry.
/// </summary>
internal static class TransientFileRetry
{
	internal const int DefaultAttempts = 3;
	internal static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(100);

	private const int ErrorSharingViolation = unchecked((int)0x80070020);
	private const int ErrorLockViolation = unchecked((int)0x80070021);

	/// <summary>Runs <paramref name="operation"/>, retrying transient IO failures up to <paramref name="attempts"/> total tries.</summary>
	/// <param name="operation">The IO operation. Must be idempotent — it may run multiple times.</param>
	/// <param name="attempts">The maximum total tries (not extra retries).</param>
	/// <param name="delay">The wait between tries (via <see cref="Task.Delay(TimeSpan)"/> — never blocks the caller's thread); injectable so tests run with zero.</param>
	/// <param name="onRetry">Invoked after each failed transient attempt (attempt number, exception) — for logging.</param>
	internal static async Task ExecuteAsync(Func<Task> operation, int attempts, TimeSpan delay, Action<int, IOException>? onRetry)
	{
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				await operation().ConfigureAwait(false);
				return;
			}
			catch (IOException ex) when (attempt < attempts && IsTransient(ex))
			{
				onRetry?.Invoke(attempt, ex);

				if (delay > TimeSpan.Zero)
				{
					await Task.Delay(delay).ConfigureAwait(false);
				}
			}
		}
	}

	private static bool IsTransient(IOException exception)
		=> exception.HResult == ErrorSharingViolation || exception.HResult == ErrorLockViolation;
}
