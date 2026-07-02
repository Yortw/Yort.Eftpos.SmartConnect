using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Yort.Eftpos.SmartConnect.Tests.Helpers;

namespace Yort.Eftpos.SmartConnect.Tests;

public sealed class FileBasedTransactionStateStoreTests : IDisposable
{
	private readonly string _directory;

	public FileBasedTransactionStateStoreTests()
	{
		_directory = Path.Combine(Path.GetTempPath(), "scstore-" + Guid.NewGuid().ToString("N"));
	}

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(_directory))
			{
				Directory.Delete(_directory, recursive: true);
			}
		}
		catch
		{
			// best-effort cleanup
		}
	}

	private FileBasedTransactionStateStore NewStore() => new FileBasedTransactionStateStore(_directory);

	[Fact]
	public async Task GetPending_SkipsCorruptRecord_LogsWarningNamingTheFile()
	{
		// A skipped record is the ONLY clue support gets when a charged sale has no POS record (there is
		// no automated recovery) — silence here turns a torn write into an unexplainable mystery.
		var logger = new ListLogger();
		var store = new FileBasedTransactionStateStore(_directory, logger);
		await store.SaveTransactionAttemptAsync("ref-good", SmartConnectTransactionType.CardPurchase, 500);
		File.WriteAllText(Path.Combine(_directory, "corrupt.json"), "{ this is not valid json");

		var pending = (await store.GetPendingTransactionsAsync()).ToList();

		Assert.Single(pending);
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("corrupt.json"));
	}

	[Fact]
	public async Task GetPending_UnauthorizedAccessOnOneFile_IsSkippedAndLogged_NotThrown()
	{
		// UnauthorizedAccessException is NOT an IOException subclass: one bad-ACL file (AV/backup/admin
		// tooling) must not make every pending transaction unenumerable — the doc contract says skipped.
		var logger = new ListLogger();
		var store = new FileBasedTransactionStateStore(_directory, logger);
		await store.SaveTransactionAttemptAsync("ref-good", SmartConnectTransactionType.CardPurchase, 500);
		await store.SaveTransactionAttemptAsync("ref-denied", SmartConnectTransactionType.CardPurchase, 700);

		var deniedPath = Path.Combine(_directory, "ref-denied.json");
		var originalRead = store.ReadFileAsync;
		store.ReadFileAsync = path => string.Equals(path, deniedPath, StringComparison.OrdinalIgnoreCase)
			? Task.FromException<string>(new UnauthorizedAccessException("Access to the path is denied."))
			: originalRead(path);

		var pending = (await store.GetPendingTransactionsAsync()).ToList();

		var record = Assert.Single(pending);
		Assert.Equal("ref-good", record.ClientTransactionRef);
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("ref-denied.json"));
	}

	[Fact]
	public void Constructor_CreatesMissingDirectory()
	{
		Assert.False(Directory.Exists(_directory));
		_ = NewStore();
		Assert.True(Directory.Exists(_directory));
	}

	[Fact]
	public async Task SaveTransactionAttempt_ThenGetPending_ReturnsSentinelWithNoPollingUrl()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);

		var pending = (await store.GetPendingTransactionsAsync()).ToList();

		var record = Assert.Single(pending);
		Assert.Equal("ref-1", record.ClientTransactionRef);
		Assert.Null(record.PollingUrl);
	}

	[Fact]
	public async Task UpdatePollingDetails_RecordsPollingUrlAndTransactionId()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);
		await store.UpdatePollingDetailsAsync("ref-1", "https://poll/here?merchantAccessToken=abc", "txn-1");

		var record = Assert.Single(await store.GetPendingTransactionsAsync());
		Assert.Equal("https://poll/here?merchantAccessToken=abc", record.PollingUrl);
		Assert.Equal("txn-1", record.TransactionId);
	}

	[Fact]
	public async Task UpdateCompleted_RemovesFromPending()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);
		await store.UpdateCompletedAsync("ref-1", SmartConnectTransactionStatus.Accepted);

		Assert.Empty(await store.GetPendingTransactionsAsync());
	}

	[Fact]
	public async Task RemoveAsync_OnCompletedRecord_DeletesIt()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);
		await store.UpdateCompletedAsync("ref-1", SmartConnectTransactionStatus.Accepted);

		await store.RemoveAsync("ref-1");

		Assert.Empty(Directory.GetFiles(_directory, "*.json"));
	}

	[Fact]
	public async Task RemoveAsync_OnPendingRecord_Throws()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);

		await Assert.ThrowsAsync<InvalidOperationException>(() => store.RemoveAsync("ref-1"));
	}

	[Fact]
	public async Task UpdatePollingDetails_OnMissingRecord_Throws()
	{
		var store = NewStore();
		await Assert.ThrowsAsync<InvalidOperationException>(
			() => store.UpdatePollingDetailsAsync("does-not-exist", "https://poll", "txn"));
	}

	// F6: a kill mid-write can leave a *.tmp; recovery must ignore it, not crash or surface it as pending.
	[Fact]
	public async Task GetPending_IgnoresLeftoverTempFiles()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);
		File.WriteAllText(Path.Combine(_directory, "ref-2.json.tmp"), "{ partial, not valid json");

		var record = Assert.Single(await store.GetPendingTransactionsAsync());
		Assert.Equal("ref-1", record.ClientTransactionRef);
	}

	// F6: a corrupt record must not crash recovery for the other (valid) records.
	[Fact]
	public async Task GetPending_SkipsCorruptRecord_WithoutCrashing()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("good", SmartConnectTransactionType.CardPurchase, 500);
		File.WriteAllText(Path.Combine(_directory, "corrupt.json"), "{ this is not valid json");

		var record = Assert.Single(await store.GetPendingTransactionsAsync());
		Assert.Equal("good", record.ClientTransactionRef);
	}

	// Refs with filename-hostile characters must round-trip.
	[Fact]
	public async Task HandlesRefsWithPathUnsafeCharacters()
	{
		var store = NewStore();
		var ref1 = "branch/01-9444ae07";
		await store.SaveTransactionAttemptAsync(ref1, SmartConnectTransactionType.CardPurchase, 500);

		var record = Assert.Single(await store.GetPendingTransactionsAsync());
		Assert.Equal(ref1, record.ClientTransactionRef);

		await store.UpdateCompletedAsync(ref1, SmartConnectTransactionStatus.Declined);
		await store.RemoveAsync(ref1);
		Assert.Empty(await store.GetPendingTransactionsAsync());
	}

	// --- Task 6.6 (ADR Decision 10): pre-sized sentinel ---

	private string FileFor(string clientTransactionRef)
		=> Path.Combine(_directory, Uri.EscapeDataString(clientTransactionRef) + ".json");

	private const int SharingViolation = unchecked((int)0x80070020);

	[Fact]
	public async Task Save_PadsSentinelToReservation()
	{
		// The gate's value is how well the sentinel write predicts the later update succeeding —
		// it must claim the completed record's size class up front (disk-full protection).
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);

		Assert.True(new FileInfo(FileFor("ref-1")).Length >= FileBasedTransactionStateStore.ReservationBytes);
	}

	[Fact]
	public async Task UpdatePollingDetails_LongUrl_DoesNotGrowFileBeyondInitialSize()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);
		var initialSize = new FileInfo(FileFor("ref-1")).Length;

		// ~2KB URL — well within the reservation; padding shrinks as real content grows.
		var longUrl = "https://poll.example/transaction?merchantAccessToken=" + new string('a', 2048);
		await store.UpdatePollingDetailsAsync("ref-1", longUrl, "txn-1");

		Assert.True(new FileInfo(FileFor("ref-1")).Length <= initialSize);
	}

	[Fact]
	public async Task Update_RecordExceedingReservation_SucceedsAndRoundTrips()
	{
		// (G8) Overflow grows the file and succeeds — prediction degrades, the operation never
		// truncates or throws.
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);

		var hugeUrl = "https://poll.example/transaction?merchantAccessToken=" + new string('b', 8192);
		await store.UpdatePollingDetailsAsync("ref-1", hugeUrl, "txn-1");

		var record = Assert.Single(await store.GetPendingTransactionsAsync());
		Assert.Equal(hugeUrl, record.PollingUrl);
		Assert.True(new FileInfo(FileFor("ref-1")).Length > FileBasedTransactionStateStore.ReservationBytes);
	}

	[Fact]
	public async Task Update_RecordExceedingReservation_LogsWarning()
	{
		var logger = new ListLogger();
		var store = new FileBasedTransactionStateStore(_directory, logger);
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);

		await store.UpdatePollingDetailsAsync("ref-1", "https://poll/?t=" + new string('c', 8192), "txn-1");

		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("reservation"));
	}

	// --- Task 6.6 (G3): re-Save over an existing ref resets to a fresh pending sentinel ---

	[Fact]
	public async Task Save_OverCompletedRecord_ResetsToFreshPendingSentinel()
	{
		// Gate-refusal/NotSent retries reuse the same client-transaction ref — a re-tender must start clean.
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);
		await store.UpdatePollingDetailsAsync("ref-1", "https://poll/old", "txn-old");
		await store.UpdateCompletedAsync("ref-1", SmartConnectTransactionStatus.Failed);

		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 700);

		var record = Assert.Single(await store.GetPendingTransactionsAsync());
		Assert.Equal("ref-1", record.ClientTransactionRef);
		Assert.Null(record.PollingUrl);
		Assert.Null(record.TransactionId);
	}

	[Fact]
	public async Task Save_OverTruncatedGarbageFile_ResetsToFreshPendingSentinel()
	{
		var store = NewStore();
		File.WriteAllText(FileFor("ref-1"), "{ truncated garbage");

		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);

		var record = Assert.Single(await store.GetPendingTransactionsAsync());
		Assert.Equal("ref-1", record.ClientTransactionRef);
		Assert.Null(record.PollingUrl);
	}

	// --- Task 6.6 (G4/G5): transient retry wiring via the internal write seam ---

	[Fact]
	public async Task Save_TransientSharingViolationOnce_SucceedsViaRetry()
	{
		var store = NewStore();
		store.RetryDelay = TimeSpan.Zero;
		var attempts = 0;
		store.WriteFileAsync = (path, contents) =>
		{
			attempts++;
			if (attempts == 1)
			{
				throw new IOException("in use", SharingViolation);
			}

			File.WriteAllText(path, contents);
			return Task.CompletedTask;
		};

		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);

		Assert.Equal(2, attempts);
		Assert.Single(await store.GetPendingTransactionsAsync());
	}

	[Fact]
	public async Task Save_PersistentFailure_ThrowsAndLeavesNoPendingRecord()
	{
		// (G2) A refused gate must leave no phantom: write-temp-then-replace means a failed Save
		// leaves at most an ignored .tmp, never a record GetPending reports as pending.
		var store = NewStore();
		store.RetryDelay = TimeSpan.Zero;
		var attempts = 0;
		store.WriteFileAsync = (path, contents) =>
		{
			attempts++;
			throw new IOException("in use", SharingViolation);
		};

		await Assert.ThrowsAsync<IOException>(() => store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500));

		Assert.Equal(3, attempts);
		Assert.Empty(await store.GetPendingTransactionsAsync());
	}

	// --- Task 6.6 (G9/G10): logger behaviour ---

	[Fact]
	public async Task Save_RetryAttempt_LogsWarning()
	{
		// (G9) A store degrading toward the gate threshold must be visible before it refuses outright.
		var logger = new ListLogger();
		var store = new FileBasedTransactionStateStore(_directory, logger) { RetryDelay = TimeSpan.Zero };
		var attempts = 0;
		store.WriteFileAsync = (path, contents) =>
		{
			attempts++;
			if (attempts == 1)
			{
				throw new IOException("in use", SharingViolation);
			}

			File.WriteAllText(path, contents);
			return Task.CompletedTask;
		};

		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);

		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
	}

	[Fact]
	public async Task Save_ThrowingLogger_DoesNotBreakOperation()
	{
		// (G10) Diagnostics must be strictly weaker than the path they diagnose.
		var store = new FileBasedTransactionStateStore(_directory, new ThrowingLogger()) { RetryDelay = TimeSpan.Zero };
		var attempts = 0;
		store.WriteFileAsync = (path, contents) =>
		{
			attempts++;
			if (attempts == 1)
			{
				throw new IOException("in use", SharingViolation);
			}

			File.WriteAllText(path, contents);
			return Task.CompletedTask;
		};

		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);

		Assert.Single(await store.GetPendingTransactionsAsync());
	}

	// --- Task 12.5 (H2): the store is genuinely asynchronous ---

	[Fact]
	public async Task Save_WithRetryDelayPending_ReturnsIncompleteTaskImmediately()
	{
		// (H2) A secretly-synchronous implementation completes the work AND the retry wait before
		// returning, so this assertion line would only run after the delay with a completed task. The
		// async implementation returns at the first Task.Delay await.
		// The retry delay is deliberately wide (2s): the incompleteness check runs microseconds after the
		// synchronous return, so only a multi-second scheduler/GC pause in that gap could complete the task
		// early — a wide margin makes that impossible on a loaded CI runner. The task is then awaited (not
		// abandoned) so its deferred second write lands in the fixture directory BEFORE Dispose deletes it,
		// and no faulted task escapes unobserved.
		var store = NewStore();
		store.RetryDelay = TimeSpan.FromSeconds(2);
		var attempts = 0;
		store.WriteFileAsync = (path, contents) =>
		{
			attempts++;
			if (attempts == 1)
			{
				throw new IOException("in use", SharingViolation);
			}

			File.WriteAllText(path, contents);
			return Task.CompletedTask;
		};

		var task = store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);

		Assert.False(task.IsCompleted);

		await task;
		Assert.Equal(2, attempts);
		Assert.Single(await store.GetPendingTransactionsAsync());
	}

	// --- Task 12.5 (H4): the semaphore is released when a write faults ---

	[Fact]
	public async Task Save_NonTransientFault_ReleasesSemaphore_SubsequentOperationSucceeds()
	{
		// A leaked semaphore would hang every later store call - the worst possible POS failure mode
		// (every subsequent tender freezes at the gate).
		var store = NewStore();
		store.RetryDelay = TimeSpan.Zero;
		store.WriteFileAsync = (path, contents) => throw new IOException("disk full", unchecked((int)0x80070070));

		await Assert.ThrowsAsync<IOException>(() => store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500));

		store.WriteFileAsync = (path, contents) =>
		{
			File.WriteAllText(path, contents);
			return Task.CompletedTask;
		};

		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);
		Assert.Single(await store.GetPendingTransactionsAsync());
	}

	// The update/complete/remove path reads the existing record via ReadRequiredAsync, which maps a corrupt
	// file to a NON-retryable InvalidOperationException. Corruption coverage previously existed only for
	// GetPending (skip) and Save (overwrite-reset) — not this read-modify-write path.
	[Fact]
	public async Task UpdatePollingDetails_OverCorruptRecord_ThrowsInvalidOperation()
	{
		var store = NewStore();
		File.WriteAllText(FileFor("ref-1"), "{ corrupt, not valid json");

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => store.UpdatePollingDetailsAsync("ref-1", "https://poll?merchantAccessToken=t", "txn-1"));
	}

	[Fact]
	public async Task UpdateCompleted_OverCorruptRecord_ThrowsInvalidOperation()
	{
		var store = NewStore();
		File.WriteAllText(FileFor("ref-1"), "{ corrupt, not valid json");

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => store.UpdateCompletedAsync("ref-1", SmartConnectTransactionStatus.Accepted));
	}

	// Exercises the single _sync semaphore under real contention: interleaved writers (distinct refs) and
	// readers must serialise without losing a write or leaking the semaphore. A leaked semaphore deadlocks
	// every later call, so the wait is bounded — a regression fails in 10s rather than hanging the suite.
	[Fact]
	public async Task ConcurrentOperations_AreSerialisedWithoutLossOrDeadlock()
	{
		var store = NewStore();
		const int count = 40;
		var writes = Enumerable.Range(0, count).Select(i =>
			store.SaveTransactionAttemptAsync("ref-" + i, SmartConnectTransactionType.CardPurchase, 100 + i));
		var reads = Enumerable.Range(0, count).Select(_ => (Task)store.GetPendingTransactionsAsync());
		var all = Task.WhenAll(writes.Concat(reads).ToArray());

		var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(10)));
		Assert.Same(all, finished); // completed, not the deadlock guard
		await all;                  // observe any faulted operation

		Assert.Equal(count, (await store.GetPendingTransactionsAsync()).Count());
	}
}
