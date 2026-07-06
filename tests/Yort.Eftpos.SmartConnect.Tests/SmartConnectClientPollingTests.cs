using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Yort.Eftpos.SmartConnect.Tests.Helpers;

namespace Yort.Eftpos.SmartConnect.Tests;

/// <summary>
/// Tests for the polling loop (Task 8). Time is virtual: the injectable clock/delay seams advance a fake
/// clock instead of sleeping, so timeout behaviour is exact and the suite stays fast. The poll loop is
/// result-based throughout — transport failures retry (F11, honouring the interval), protocol verdicts on
/// the URL stop immediately (F8), and a terminal answer always wins.
/// </summary>
public class SmartConnectClientPollingTests
{
	private const string Ref = "100123-abc";
	private const string PollUrl = "https://poll.unit.test/poll?merchantAccessToken=tok123";

	private static readonly string InitialResponseJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {\"PollingUrl\": \"" + PollUrl + "\"}}";

	private const string PendingPollJson = "{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {}}";

	private const string DelayedPollJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {\"TransactionResult\": \"OK-DELAYED\"}}";

	private const string AcceptedPollJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"COMPLETED\", \"transactionTimeStamp\": \"201809182353193193\", " +
		"\"data\": {\"TransactionResult\": \"OK-ACCEPTED\", \"Result\": \"OK\", \"AuthId\": \"A1234\", \"AcquirerRef\": \"ACQ-9\", " +
		"\"TerminalRef\": \"T-7\", \"CardPan\": \"....1234\", \"CardType\": \"VISA\", \"AccountType\": \"CREDIT\", " +
		"\"Receipt\": \"RECEIPT\\nLINE2\", \"AmountTotal\": \"1250\", \"AmountSurcharge\": \"10\", \"AmountTip\": \"0\"}}";

	private const string DeclinedPollJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"COMPLETED\", " +
		"\"data\": {\"TransactionResult\": \"OK-DECLINED\", \"Result\": \"OK\", \"AmountTotal\": \"1250\"}}";

	private sealed class RecordingProgress : IProgress<SmartConnectPollingStatus>
	{
		public List<SmartConnectPollingStatus> Reports { get; } = new List<SmartConnectPollingStatus>();

		public void Report(SmartConnectPollingStatus value) => Reports.Add(value);
	}

	private sealed class ThrowingProgress : IProgress<SmartConnectPollingStatus>
	{
		public int Calls { get; private set; }

		public void Report(SmartConnectPollingStatus value)
		{
			Calls++;
			throw new ObjectDisposedException("progress sink");
		}
	}

	private static HttpResponseMessage Json(HttpStatusCode status, string json)
		=> new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

	// A 200 response whose Content-Type declares a charset the runtime cannot resolve — ReadAsStringAsync
	// throws InvalidOperationException on decode (an intermediary error page is the likely real-world source).
	private static HttpResponseMessage BadCharset(string json)
	{
		var content = new StringContent(json, Encoding.UTF8, "application/json");
		content.Headers.ContentType!.CharSet = "foo-bar";
		return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
	}

	/// <summary>First call gets the initial POST response; later calls walk the poll sequence (last repeats).</summary>
	private static MockHttpHandler SequencedHandler(params Func<HttpResponseMessage>[] pollResponses)
	{
		var index = -1;
		return new MockHttpHandler(_ =>
		{
			var i = Interlocked.Increment(ref index);
			if (i == 0)
			{
				return Task.FromResult(Json(HttpStatusCode.OK, InitialResponseJson));
			}

			var pollIndex = Math.Min(i - 1, pollResponses.Length - 1);
			return Task.FromResult(pollResponses[pollIndex]());
		});
	}

	private static SmartConnectTransactionRequest CreateRequest()
	{
		return new SmartConnectTransactionRequest
		{
			TransactionType = SmartConnectTransactionType.CardPurchase,
			AmountTotal = Money.FromCents(1250),
			POSRegisterID = "11111111-2222-3333-4444-555555555555",
			POSBusinessName = "Demo Business",
			POSVendorName = "DemoVendor",
			ClientTransactionRef = Ref
		};
	}

	private static SmartConnectClient CreateClient(MockHttpHandler handler, InMemoryTransactionStateStore store, ILogger? logger = null, TimeSpan? maxPollDuration = null)
	{
		var client = new SmartConnectClient(new SmartConnectClientConfiguration
		{
			BaseUrl = new Uri("https://unit.test/POS"),
			StateStore = store,
			HttpClient = new HttpClient(handler),
			Logger = logger,
			// Pinned (not the 3s default) — the F11 attempts-per-window assertion is exact: 10s / 2s = 5.
			PollInterval = TimeSpan.FromSeconds(2),
			MaxPollDuration = maxPollDuration ?? TimeSpan.FromSeconds(30)
		});

		// Virtual time: the delay seam advances the fake clock instead of sleeping.
		var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
		client.Clock = () => now;
		client.PollDelay = delay =>
		{
			now += delay;
			return Task.CompletedTask;
		};

		return client;
	}

	[Fact]
	public async Task Poll_PendingThreeTimesThenAccepted_ReturnsAccepted()
	{
		var store = new InMemoryTransactionStateStore();
		var handler = SequencedHandler(
			() => Json(HttpStatusCode.OK, PendingPollJson),
			() => Json(HttpStatusCode.OK, PendingPollJson),
			() => Json(HttpStatusCode.OK, PendingPollJson),
			() => Json(HttpStatusCode.OK, AcceptedPollJson));
		var progress = new RecordingProgress();
		using var client = CreateClient(handler, store);

		var result = await client.ProcessTransactionAsync(CreateRequest(), progress);

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		Assert.Equal(SmartConnectFailureCause.None, result.FailureCause);
		Assert.Equal(3, progress.Reports.Count(r => r.State == SmartConnectPollingState.Polling));
		Assert.Contains("UpdateCompleted:" + Ref + ":Accepted", store.CallLog);
	}

	[Fact]
	public async Task Poll_CompletedResponse_MapsAllFields()
	{
		var store = new InMemoryTransactionStateStore();
		var handler = SequencedHandler(() => Json(HttpStatusCode.OK, AcceptedPollJson));
		using var client = CreateClient(handler, store);

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		Assert.Equal("txn-1", result.TransactionId);
		Assert.Equal("A1234", result.AuthId);
		Assert.Equal("ACQ-9", result.AcquirerRef);
		Assert.Equal("T-7", result.TerminalRef);
		Assert.Equal("....1234", result.CardPan);
		Assert.Equal("VISA", result.CardType);
		Assert.Equal("CREDIT", result.AccountType);
		Assert.Equal("RECEIPT\nLINE2", result.Receipt);
		Assert.Equal(1250, result.AmountTotal.ToCents());
		Assert.Equal(10, result.AmountSurcharge.ToCents());
		Assert.Equal(0, result.AmountTip.ToCents());
		Assert.Equal("201809182353193193", result.ResponseTimestamp);
	}

	[Fact]
	public async Task Poll_Declined_IsNormalOutcome_FailureCauseNone()
	{
		// (R7) Declined is data, not an error — the driver must never see a transport-ish cause on it.
		var store = new InMemoryTransactionStateStore();
		var handler = SequencedHandler(() => Json(HttpStatusCode.OK, DeclinedPollJson));
		using var client = CreateClient(handler, store);

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Declined, result.Status);
		Assert.Equal(SmartConnectFailureCause.None, result.FailureCause);
		Assert.Contains("UpdateCompleted:" + Ref + ":Declined", store.CallLog);
	}

	[Fact]
	public async Task Poll_Delayed_ReportsDelayedAndKeepsPolling()
	{
		var store = new InMemoryTransactionStateStore();
		var handler = SequencedHandler(
			() => Json(HttpStatusCode.OK, DelayedPollJson),
			() => Json(HttpStatusCode.OK, AcceptedPollJson));
		var progress = new RecordingProgress();
		using var client = CreateClient(handler, store);

		var result = await client.ProcessTransactionAsync(CreateRequest(), progress);

		Assert.Contains(progress.Reports, r => r.State == SmartConnectPollingState.Delayed);
		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
	}

	[Fact]
	public async Task Poll_Timeout_ReturnsUnknownAndClosesSentinelUnknown()
	{
		var store = new InMemoryTransactionStateStore();
		var handler = SequencedHandler(() => Json(HttpStatusCode.OK, PendingPollJson));
		var logger = new ListLogger();
		using var client = CreateClient(handler, store, logger, maxPollDuration: TimeSpan.FromSeconds(10));

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Unknown, result.Status);
		// Distinguishable from POST-phase TransportUnknown — poll exhaustion carries no transport cause.
		Assert.Equal(SmartConnectFailureCause.None, result.FailureCause);
		Assert.Contains("UpdateCompleted:" + Ref + ":Unknown", store.CallLog);
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
	}

	[Fact]
	public async Task Poll_NetworkErrorThenCompleted_ReportsNetworkErrorAndRecovers()
	{
		var store = new InMemoryTransactionStateStore();
		var pollCount = 0;
		var handler = new MockHttpHandler(_ =>
		{
			pollCount++;
			if (pollCount == 1)
			{
				return Task.FromResult(Json(HttpStatusCode.OK, InitialResponseJson));
			}

			if (pollCount == 2)
			{
				throw new HttpRequestException("reset", new SocketException((int)SocketError.ConnectionReset));
			}

			return Task.FromResult(Json(HttpStatusCode.OK, AcceptedPollJson));
		});
		var progress = new RecordingProgress();
		using var client = CreateClient(handler, store);

		var result = await client.ProcessTransactionAsync(CreateRequest(), progress);

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		var networkReport = Assert.Single(progress.Reports, r => r.State == SmartConnectPollingState.NetworkError);
		Assert.NotNull(networkReport.Error);
	}

	[Fact]
	public async Task Poll_PersistentNetworkError_HonoursIntervalAndTimesOut()
	{
		// (F11) The delay runs on the catch path too — attempts within the window must equal
		// MaxPollDuration / PollInterval (10s / 2s = 5 polls), never a tight retry-storm.
		var store = new InMemoryTransactionStateStore();
		var requestCount = 0;
		var handler = new MockHttpHandler(_ =>
		{
			requestCount++;
			if (requestCount == 1)
			{
				return Task.FromResult(Json(HttpStatusCode.OK, InitialResponseJson));
			}

			throw new HttpRequestException("down", new SocketException((int)SocketError.ConnectionReset));
		});
		using var client = CreateClient(handler, store, maxPollDuration: TimeSpan.FromSeconds(10));

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Unknown, result.Status);
		Assert.Equal(5, requestCount - 1);
	}

	[Theory]
	[InlineData(HttpStatusCode.Unauthorized)]
	[InlineData(HttpStatusCode.Forbidden)]
	[InlineData(HttpStatusCode.NotFound)]
	[InlineData(HttpStatusCode.Gone)]
	public async Task Poll_ProtocolVerdictOnUrl_StopsImmediatelyWithPollingUrlInvalid(HttpStatusCode status)
	{
		// (F8) These are ANSWERS saying the URL itself is no good — spinning NetworkError to timeout would
		// waste MaxPollDuration and mislead the operator. Stop at once; the caller reconciles manually.
		var store = new InMemoryTransactionStateStore();
		var pollAttempts = 0;
		var handler = new MockHttpHandler(_ =>
		{
			pollAttempts++;
			if (pollAttempts == 1)
			{
				return Task.FromResult(Json(HttpStatusCode.OK, InitialResponseJson));
			}

			return Task.FromResult(new HttpResponseMessage(status));
		});
		var logger = new ListLogger();
		using var client = CreateClient(handler, store, logger);

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Unknown, result.Status);
		Assert.Equal(SmartConnectFailureCause.PollingUrlInvalid, result.FailureCause);
		Assert.Equal(2, pollAttempts);
		// Sentinel stays pending — the outcome is unresolved until manual reconciliation closes it.
		Assert.Null(store.Records[Ref].Status);
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
	}

	[Fact]
	public async Task Poll_RateLimited_KeepsPolling()
	{
		// HTTP 429 is transient by definition; Task 9 adds Retry-After-aware backoff on top.
		var store = new InMemoryTransactionStateStore();
		var handler = SequencedHandler(
			() => new HttpResponseMessage((HttpStatusCode)429),
			() => Json(HttpStatusCode.OK, AcceptedPollJson));
		using var client = CreateClient(handler, store);

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
	}

	[Fact]
	public async Task Poll_UpdateCompletedThrowsAtTerminal_ResultStillReturned()
	{
		// (R3) A persistence failure never masks an outcome the library holds; (G6) the cause stays None.
		var store = new InMemoryTransactionStateStore { ThrowOnUpdateCompleted = new System.IO.IOException("store down") };
		var handler = SequencedHandler(() => Json(HttpStatusCode.OK, AcceptedPollJson));
		var logger = new ListLogger();
		using var client = CreateClient(handler, store, logger);

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		Assert.NotEqual(SmartConnectFailureCause.StateStoreFailure, result.FailureCause);
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
	}

	[Fact]
	public async Task Poll_UndecodableBody_TreatedAsTransient_Recovers()
	{
		// (I2) A poll response whose body cannot be decoded (bad charset) is a transient blip, not a fatal
		// error — treat it like a garbled body: report NetworkError, keep polling, and recover on the next
		// answer. The decode failure is logged (F7) so it is diagnosable.
		var store = new InMemoryTransactionStateStore();
		var handler = SequencedHandler(
			() => BadCharset(PendingPollJson),
			() => Json(HttpStatusCode.OK, AcceptedPollJson));
		var progress = new RecordingProgress();
		var logger = new ListLogger();
		using var client = CreateClient(handler, store, logger);

		var result = await client.ProcessTransactionAsync(CreateRequest(), progress);

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		Assert.Contains(progress.Reports, r => r.State == SmartConnectPollingState.NetworkError);
		var decodeWarning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("decode", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(decodeWarning.State, p => p.Key == "ExceptionType" && (string?)p.Value == nameof(InvalidOperationException));
	}

	[Fact]
	public async Task Post_UndecodableBody_IsUnknownAndLeavesSentinelPending()
	{
		// (I2) The POST answered 200 but its body cannot be decoded, so no polling URL can be read — the
		// transaction may be live on the pinpad. Unknown, sentinel pending (routes through the existing
		// no-polling-URL branch), never a thrown decode exception.
		var store = new InMemoryTransactionStateStore();
		var handler = new MockHttpHandler(_ => Task.FromResult(BadCharset(InitialResponseJson)));
		using var client = CreateClient(handler, store);

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Unknown, result.Status);
		Assert.Equal(SmartConnectFailureCause.TransportUnknown, result.FailureCause);
		Assert.Null(store.Records[Ref].Status);
	}

	[Theory]
	[InlineData("relative/path")]
	[InlineData("foo:bar")]
	public async Task Post_UnusablePollingUrlInBody_IsUnknownPollingUrlInvalid_SentinelPending(string badUrl)
	{
		// (M3/F1) The POST answered 200 but the polling URL it returned is not a usable absolute http(s) URL —
		// sending to it would throw from HttpClient mid-loop. Map to Unknown/PollingUrlInvalid with the
		// sentinel left pending (like the no-URL and poll-verdict paths), never a raw throw.
		var store = new InMemoryTransactionStateStore();
		var badInitial = "{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {\"PollingUrl\": \"" + badUrl + "\"}}";
		var handler = new MockHttpHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, badInitial)));
		using var client = CreateClient(handler, store);

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Unknown, result.Status);
		Assert.Equal(SmartConnectFailureCause.PollingUrlInvalid, result.FailureCause);
		Assert.Null(store.Records[Ref].Status);
	}

	[Fact]
	public async Task Poll_ProgressSinkThrows_SwallowedAndPollingContinues()
	{
		// (I4) A consumer's IProgress sink is an informational side-channel — a throw from it (the classic case
		// is a WinForms Control.Invoke onto a form the operator just closed) must never abort the poll of a
		// live payment. It is swallowed and logged by type (like a failing logger), and the outcome still
		// returns normally.
		var store = new InMemoryTransactionStateStore();
		var handler = SequencedHandler(
			() => Json(HttpStatusCode.OK, PendingPollJson),
			() => Json(HttpStatusCode.OK, AcceptedPollJson));
		var progress = new ThrowingProgress();
		var logger = new ListLogger();
		using var client = CreateClient(handler, store, logger);

		var result = await client.ProcessTransactionAsync(CreateRequest(), progress);

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		Assert.True(progress.Calls > 0);
		var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("progress", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(warning.State, p => p.Key == "ExceptionType" && (string?)p.Value == nameof(ObjectDisposedException));
	}

	[Fact]
	public async Task Poll_NullProgress_DoesNotThrow()
	{
		var store = new InMemoryTransactionStateStore();
		var handler = SequencedHandler(
			() => Json(HttpStatusCode.OK, DelayedPollJson),
			() => Json(HttpStatusCode.OK, AcceptedPollJson));
		using var client = CreateClient(handler, store);

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
	}
}
