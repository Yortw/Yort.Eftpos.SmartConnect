using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Yort.Eftpos.SmartConnect.Internal;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// A reference <see cref="ISmartConnectTransactionState"/> implementation that stores one JSON file per
/// transaction in a directory. Suitable for samples and simple single-register integrations; products with a
/// database should provide their own store.
/// </summary>
/// <remarks>
/// <para>Genuinely asynchronous: file IO uses async streams and retry waits use <c>Task.Delay</c>, so store
/// calls never block the caller's thread — the first store call runs before the client's first yield, which
/// on a WinForms POS is the UI thread at tender start.</para>
/// <para>Writes are atomic (write-temp-then-replace) so a crash mid-write cannot truncate a record. The
/// directory is per-store; point each register at its own directory. Files contain the polling URL, which
/// carries a bearer token — restrict access to the directory accordingly.</para>
/// <para>The attempt record is pre-sized to <see cref="ReservationBytes"/> so passing the pre-POST gate
/// predicts the later polling-details update succeeding (ADR Decision 10). Atomicity deliberately wins over
/// prediction purity: the temp-then-replace step transiently needs ~2× the reservation, so the gate is a
/// good predictor, not a guarantee. Records larger than the reservation grow the file and succeed.</para>
/// <para>Transient IO failures (sharing/lock violations) are retried a small bounded number of times so a
/// thrown exception means "store actually unavailable", not "one anti-virus scan blip".</para>
/// </remarks>
public sealed class FileBasedTransactionStateStore : ISmartConnectTransactionState
{
	/// <summary>
	/// The capacity (bytes) the attempt record reserves up front so the later polling-details update stays
	/// in the same size class — passing the gate should predict the update succeeding (ADR Decision 10).
	/// </summary>
	public const int ReservationBytes = 4096;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

	private readonly string _directory;

	// (H1) NOT reentrant, unlike the lock it replaced (a lock cannot span await): public methods acquire
	// exactly once and private helpers never acquire — preserve that shape when changing this class.
	// The ISmartConnectTransactionState members take no CancellationToken, so none is passed to WaitAsync.
	private readonly SemaphoreSlim _sync = new SemaphoreSlim(1, 1);

	private readonly ILogger? _logger;

	/// <summary>The delay between transient-IO retry attempts. Internal seam for deterministic tests.</summary>
	internal TimeSpan RetryDelay { get; set; } = TransientFileRetry.DefaultDelay;

	/// <summary>The file-write operation. Internal seam so tests can inject IO failures deterministically.</summary>
	internal Func<string, string, Task> WriteFileAsync { get; set; } = DefaultWriteFileAsync;

	/// <summary>Creates a store backed by the given directory, creating it if it does not exist.</summary>
	/// <param name="directory">The directory in which to store transaction-state files.</param>
	/// <exception cref="ArgumentException"><paramref name="directory"/> is null, empty, or whitespace.</exception>
	public FileBasedTransactionStateStore(string directory)
	{
		if (string.IsNullOrWhiteSpace(directory))
		{
			throw new ArgumentException("Directory must not be null, empty, or whitespace.", nameof(directory));
		}

		_directory = directory;
		Directory.CreateDirectory(_directory);
	}

	/// <summary>
	/// Creates a store backed by the given directory, logging retry attempts and reservation overflows to
	/// <paramref name="logger"/>. Logging failures are suppressed and never fail the store operation; log
	/// text never includes the polling URL.
	/// </summary>
	/// <param name="directory">The directory in which to store transaction-state files.</param>
	/// <param name="logger">The logger for diagnostics.</param>
	/// <exception cref="ArgumentException"><paramref name="directory"/> is null, empty, or whitespace.</exception>
	public FileBasedTransactionStateStore(string directory, ILogger logger)
		: this(directory)
	{
		_logger = logger;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Uses create-overwrite semantics: saving over an existing record (completed, failed, or partial
	/// garbage) produces a fresh pending sentinel, so gate-refusal retries reusing the same reference are
	/// idempotent.
	/// </remarks>
	/// <exception cref="IOException">The state file could not be written (after bounded transient retries).</exception>
	public async Task SaveTransactionAttemptAsync(string clientTransactionRef, string transactionType, long amountTotalCents)
	{
		var record = new StoredRecord
		{
			ClientTransactionRef = clientTransactionRef,
			TransactionType = transactionType,
			AmountTotalCents = amountTotalCents,
			CreatedAt = DateTimeOffset.UtcNow
		};

		await _sync.WaitAsync().ConfigureAwait(false);
		try
		{
			await ExecuteWithRetryAsync(() => WriteAtomicAsync(PathFor(clientTransactionRef), record)).ConfigureAwait(false);
		}
		finally
		{
			_sync.Release();
		}
	}

	/// <inheritdoc />
	/// <exception cref="InvalidOperationException">No state record exists for the reference, or it is unreadable/corrupt.</exception>
	/// <exception cref="IOException">The state file could not be written (after bounded transient retries).</exception>
	public async Task UpdatePollingDetailsAsync(string clientTransactionRef, string pollingUrl, string transactionId)
	{
		await _sync.WaitAsync().ConfigureAwait(false);
		try
		{
			// The whole read-modify-write retries as a unit — it is idempotent, and a transient failure
			// can hit the read just as easily as the write.
			await ExecuteWithRetryAsync(async () =>
			{
				var record = await ReadRequiredAsync(clientTransactionRef).ConfigureAwait(false);
				record.PollingUrl = pollingUrl;
				record.TransactionId = transactionId;
				await WriteAtomicAsync(PathFor(clientTransactionRef), record).ConfigureAwait(false);
			}).ConfigureAwait(false);
		}
		finally
		{
			_sync.Release();
		}
	}

	/// <inheritdoc />
	/// <exception cref="InvalidOperationException">No state record exists for the reference, or it is unreadable/corrupt.</exception>
	/// <exception cref="IOException">The state file could not be written (after bounded transient retries).</exception>
	public async Task UpdateCompletedAsync(string clientTransactionRef, SmartConnectTransactionStatus status)
	{
		await _sync.WaitAsync().ConfigureAwait(false);
		try
		{
			await ExecuteWithRetryAsync(async () =>
			{
				var record = await ReadRequiredAsync(clientTransactionRef).ConfigureAwait(false);
				record.Status = status;
				record.CompletedAt = DateTimeOffset.UtcNow;
				await WriteAtomicAsync(PathFor(clientTransactionRef), record).ConfigureAwait(false);
			}).ConfigureAwait(false);
		}
		finally
		{
			_sync.Release();
		}
	}

	/// <inheritdoc />
	/// <remarks>Individual unreadable/corrupt records and leftover temp files are skipped, not thrown.</remarks>
	/// <exception cref="IOException">The state directory could not be enumerated.</exception>
	public async Task<IEnumerable<PendingTransaction>> GetPendingTransactionsAsync()
	{
		var results = new List<PendingTransaction>();

		await _sync.WaitAsync().ConfigureAwait(false);
		try
		{
			foreach (var file in Directory.GetFiles(_directory))
			{
				// Match .json exactly via the extension, not a "*.json" glob (which can also match
				// "*.json.tmp" on Windows). Leftover .tmp files from an interrupted write are ignored.
				if (!string.Equals(Path.GetExtension(file), ".json", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var record = await TryReadAsync(file).ConfigureAwait(false);
				if (record != null && record.Status == null)
				{
					results.Add(new PendingTransaction
					{
						ClientTransactionRef = record.ClientTransactionRef,
						PollingUrl = record.PollingUrl,
						TransactionId = record.TransactionId,
						CreatedAt = record.CreatedAt
					});
				}
			}
		}
		finally
		{
			_sync.Release();
		}

		return results;
	}

	/// <inheritdoc />
	/// <exception cref="InvalidOperationException">The record does not exist, is unreadable/corrupt, or has not reached a terminal state.</exception>
	/// <exception cref="IOException">The state file could not be deleted (after bounded transient retries).</exception>
	public async Task RemoveAsync(string clientTransactionRef)
	{
		await _sync.WaitAsync().ConfigureAwait(false);
		try
		{
			await ExecuteWithRetryAsync(async () =>
			{
				var record = await ReadRequiredAsync(clientTransactionRef).ConfigureAwait(false);
				if (record.Status == null)
				{
					throw new InvalidOperationException($"Transaction '{clientTransactionRef}' has not reached a terminal state and cannot be removed.");
				}

				File.Delete(PathFor(clientTransactionRef));
			}).ConfigureAwait(false);
		}
		finally
		{
			_sync.Release();
		}
	}

	private Task ExecuteWithRetryAsync(Func<Task> operation)
	{
		return TransientFileRetry.ExecuteAsync(
			operation,
			TransientFileRetry.DefaultAttempts,
			RetryDelay,
			(attempt, ex) => SafeLog(LogLevel.Warning, ex, "Transient IO failure on transaction-state operation (attempt {Attempt} of {MaxAttempts}); retrying.", attempt, TransientFileRetry.DefaultAttempts));
	}

	// Escape so refs containing path-hostile characters (e.g. "branch/01-...") map to a safe, reversible file name.
	private string PathFor(string clientTransactionRef)
		=> Path.Combine(_directory, Uri.EscapeDataString(clientTransactionRef) + ".json");

	private async Task<StoredRecord> ReadRequiredAsync(string clientTransactionRef)
	{
		var path = PathFor(clientTransactionRef);
		if (!File.Exists(path))
		{
			throw new InvalidOperationException($"No transaction state found for '{clientTransactionRef}'.");
		}

		// IOException deliberately propagates (a transient read failure is retryable by the caller's
		// retry wrap); only parse failures map to the non-retryable "corrupt" InvalidOperationException.
		var json = await ReadFileAsync(path).ConfigureAwait(false);

		StoredRecord? record;
		try
		{
			record = JsonSerializer.Deserialize<StoredRecord>(json, JsonOptions);
		}
		catch (JsonException)
		{
			record = null;
		}

		if (record == null)
		{
			throw new InvalidOperationException($"Transaction state for '{clientTransactionRef}' is unreadable or corrupt.");
		}

		return record;
	}

	private static async Task<StoredRecord?> TryReadAsync(string path)
	{
		try
		{
			return JsonSerializer.Deserialize<StoredRecord>(await ReadFileAsync(path).ConfigureAwait(false), JsonOptions);
		}
		catch (IOException)
		{
			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private async Task WriteAtomicAsync(string path, StoredRecord record)
	{
		var json = SerializePadded(record);
		var tempPath = path + ".tmp";

		await WriteFileAsync(tempPath, json).ConfigureAwait(false);

		// Replace is atomic when the destination exists; Move is atomic for the first write. Either way a
		// reader/recovery never sees a half-written .json. These rename operations have no async API and
		// are metadata-fast — deliberately left synchronous.
		if (File.Exists(path))
		{
			File.Replace(tempPath, path, destinationBackupFileName: null);
		}
		else
		{
			File.Move(tempPath, path);
		}
	}

	private static async Task DefaultWriteFileAsync(string path, string contents)
	{
		using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
		using (var writer = new StreamWriter(stream))
		{
			await writer.WriteAsync(contents).ConfigureAwait(false);
			await writer.FlushAsync().ConfigureAwait(false);
		}
	}

	private static async Task<string> ReadFileAsync(string path)
	{
		using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
		using (var reader = new StreamReader(stream))
		{
			return await reader.ReadToEndAsync().ConfigureAwait(false);
		}
	}

	private string SerializePadded(StoredRecord record)
	{
		record.Padding = string.Empty;
		var json = JsonSerializer.Serialize(record, JsonOptions);
		var deficit = ReservationBytes - Encoding.UTF8.GetByteCount(json);

		if (deficit > 0)
		{
			// Spaces are one UTF-8 byte each and need no JSON escaping, so the padded total is exactly
			// the reservation.
			record.Padding = new string(' ', deficit);
			return JsonSerializer.Serialize(record, JsonOptions);
		}

		if (deficit < 0)
		{
			// (G8) Overflow grows the file and succeeds — but the pre-sized gate's disk-full prediction
			// is degraded for this record, and that must be observable.
			SafeLog(LogLevel.Warning, null, "Transaction-state record exceeds the {ReservationBytes}-byte reservation; the pre-sized sentinel's disk-full prediction is degraded for this record.", ReservationBytes);
		}

		return json;
	}

	// Diagnostics must be strictly weaker than the path they diagnose (G10) — a logger failure never
	// fails the store operation. Message templates with args preserve structured logging.
	private void SafeLog(LogLevel level, Exception? exception, string messageTemplate, params object?[] args)
	{
		if (_logger == null)
		{
			return;
		}

		try
		{
			_logger.Log(level, exception, messageTemplate, args);
		}
		catch
		{
			// Suppressed by design.
		}
	}

	private sealed class StoredRecord
	{
		public string ClientTransactionRef { get; set; } = string.Empty;
		public string? TransactionType { get; set; }
		public long AmountTotalCents { get; set; }
		public string? PollingUrl { get; set; }
		public string? TransactionId { get; set; }
		public SmartConnectTransactionStatus? Status { get; set; }
		public DateTimeOffset CreatedAt { get; set; }
		public DateTimeOffset? CompletedAt { get; set; }

		// Reserves the completed record's size class at sentinel time (ADR Decision 10) — recomputed on
		// every rewrite so the file stays at the reservation while real content grows.
		public string Padding { get; set; } = string.Empty;
	}
}
