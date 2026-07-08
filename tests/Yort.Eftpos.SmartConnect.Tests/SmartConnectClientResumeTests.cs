using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Yort.Eftpos.SmartConnect.Tests.Helpers;

namespace Yort.Eftpos.SmartConnect.Tests;

/// <summary>
/// Tests for the crash-recovery APIs (Task 11). <c>ResumePollingAsync</c> jumps straight to the poll loop
/// for a persisted URL; <c>GetLastTransactionResultAsync</c> sends the diagnostic journal query with NO
/// state store interaction at all (the caller owns any existing sentinel and reconciles manually).
/// Transport contracts per ADR Decision 9/R5: the journal POST throws the typed transport exception
/// (idempotent query, like PairAsync); everything in a poll phase stays result-based.
/// </summary>
public class SmartConnectClientResumeTests
{
	private const string Ref = "100123-abc";
	private const string PollUrl = "https://poll.unit.test/poll?merchantAccessToken=tok123";

	private const string PendingPollJson = "{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {}}";

	private const string AcceptedPollJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"COMPLETED\", " +
		"\"data\": {\"TransactionResult\": \"OK-ACCEPTED\", \"Result\": \"OK\", \"AmountTotal\": \"1250\"}}";

	private const string InitialResponseJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {\"PollingUrl\": \"" + PollUrl + "\"}}";

	private static HttpResponseMessage Json(HttpStatusCode status, string json)
		=> new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

	private static SmartConnectClient CreateClient(MockHttpHandler handler, InMemoryTransactionStateStore store)
	{
		var client = new SmartConnectClient(new SmartConnectClientConfiguration
		{
			BaseUrl = new Uri("https://unit.test/POS"),
			StateStore = store,
			HttpClient = new HttpClient(handler),
			PollInterval = TimeSpan.FromSeconds(2),
			MaxPollDuration = TimeSpan.FromSeconds(10)
		});

		var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
		client.Clock = () => now;
		client.PollDelay = delay =>
		{
			now += delay;
			return Task.CompletedTask;
		};

		return client;
	}

	/// <summary>A store with a pre-existing pending sentinel, as crash recovery would find it.</summary>
	private static InMemoryTransactionStateStore StoreWithPendingSentinel()
	{
		var store = new InMemoryTransactionStateStore();
		store.Records[Ref] = new InMemoryTransactionStateStore.Record
		{
			ClientTransactionRef = Ref,
			TransactionType = SmartConnectTransactionType.CardPurchase,
			AmountTotalCents = 1250,
			PollingUrl = PollUrl,
			TransactionId = "txn-1"
		};
		return store;
	}

	private static SmartConnectRegistration CreateRegistration()
	{
		return new SmartConnectRegistration
		{
			POSRegisterID = "11111111-2222-3333-4444-555555555555",
			POSBusinessName = "Demo Business",
			POSVendorName = "DemoVendor"
		};
	}

	// --- ResumePollingAsync ---

	[Fact]
	public async Task Resume_PollsTheGivenUrl_NoPostIsSent()
	{
		var store = StoreWithPendingSentinel();
		var handler = new MockHttpHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, AcceptedPollJson)));
		using var client = CreateClient(handler, store);

		var result = await client.ResumePollingAsync(PollUrl, Ref);

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
		Assert.Equal(PollUrl, handler.Requests[0].Uri?.AbsoluteUri);
	}

	[Fact]
	public async Task Resume_NeverCallsSaveOrUpdatePollingDetails()
	{
		// (F5) Enforced with throw-hooks, not just log assertions: if resume touched either method the
		// call would blow up, not silently pass.
		var store = StoreWithPendingSentinel();
		store.ThrowOnSave = new InvalidOperationException("Save must not be called on resume");
		store.ThrowOnUpdatePollingDetails = new InvalidOperationException("UpdatePollingDetails must not be called on resume");
		var handler = new MockHttpHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, AcceptedPollJson)));
		using var client = CreateClient(handler, store);

		var result = await client.ResumePollingAsync(PollUrl, Ref);

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		Assert.DoesNotContain(store.CallLog, entry => entry.StartsWith("Save:"));
		Assert.DoesNotContain(store.CallLog, entry => entry.StartsWith("UpdatePolling:"));
	}

	[Fact]
	public async Task Resume_LeavesSentinelPendingOnCompletion()
	{
		// (Case C) The library no longer finalizes on a completed outcome — it leaves the sentinel pending for
		// the consumer to complete after durably persisting. Recovery replays it if the consumer crashes first.
		var store = StoreWithPendingSentinel();
		var handler = new MockHttpHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, AcceptedPollJson)));
		using var client = CreateClient(handler, store);

		var result = await client.ResumePollingAsync(PollUrl, Ref);

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		Assert.DoesNotContain(store.CallLog, e => e.StartsWith("UpdateCompleted:"));
		Assert.Null(store.Records[Ref].Status);
	}

	[Fact]
	public async Task Resume_CompletedButNotFinalized_ReplaysTheSameOutcome_UntilConsumerCompletes()
	{
		// (Case C) The library leaves a completed transaction pending. If the consumer crashes before it marks
		// completion, the row is still pending, so a second resume — via a FRESH client, as after a process
		// restart — re-polls and replays the SAME terminal outcome. Only the consumer's UpdateCompletedAsync
		// (after durable persist) moves it out of the pending scan. This pins the money-safety requirement
		// (recoverable from durable state alone, F6) and that completing an already-completed record is
		// idempotent (F7).
		var store = StoreWithPendingSentinel();
		var handler = new MockHttpHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, AcceptedPollJson)));

		using (var client = CreateClient(handler, store))
		{
			var first = await client.ResumePollingAsync(PollUrl, Ref);
			Assert.Equal(SmartConnectTransactionStatus.Accepted, first.Status);
			Assert.Null(store.Records[Ref].Status);                                  // library did not finalize
			Assert.Contains(await store.GetPendingTransactionsAsync(), p => p.ClientTransactionRef == Ref);
		}

		// Fresh client = process restart: its only knowledge is the durable store row + polling URL.
		using (var client2 = CreateClient(handler, store))
		{
			var replay = await client2.ResumePollingAsync(PollUrl, Ref);
			Assert.Equal(SmartConnectTransactionStatus.Accepted, replay.Status);
			Assert.Contains(await store.GetPendingTransactionsAsync(), p => p.ClientTransactionRef == Ref);

			await store.UpdateCompletedAsync(Ref, replay.Status);                    // consumer persisted → complete
			Assert.DoesNotContain(await store.GetPendingTransactionsAsync(), p => p.ClientTransactionRef == Ref);

			await store.UpdateCompletedAsync(Ref, replay.Status);                    // idempotent double-complete
			Assert.DoesNotContain(await store.GetPendingTransactionsAsync(), p => p.ClientTransactionRef == Ref);
		}
	}

	[Theory]
	[InlineData(HttpStatusCode.Unauthorized)]
	[InlineData(HttpStatusCode.Forbidden)]
	[InlineData(HttpStatusCode.NotFound)]
	public async Task Resume_ExpiredUrl_ReturnsPollingUrlInvalid_NoSpinToTimeout(HttpStatusCode status)
	{
		// (F8) The whole point of the classification: an expired persisted URL must surface
		// PollingUrlInvalid immediately, not burn MaxPollDuration pretending it's a network problem.
		var store = StoreWithPendingSentinel();
		var attempts = 0;
		var handler = new MockHttpHandler(_ =>
		{
			attempts++;
			return Task.FromResult(new HttpResponseMessage(status));
		});
		using var client = CreateClient(handler, store);

		var result = await client.ResumePollingAsync(PollUrl, Ref);

		Assert.Equal(SmartConnectTransactionStatus.Unknown, result.Status);
		Assert.Equal(SmartConnectFailureCause.PollingUrlInvalid, result.FailureCause);
		Assert.Equal(1, attempts);
		Assert.Null(store.Records[Ref].Status);
	}

	[Fact]
	public async Task Resume_TransientTransportFailure_RetriesAndRecovers_NeverThrows()
	{
		// (R5) ResumePollingAsync has no POST phase — transport failures are poll-loop business:
		// retry within MaxPollDuration, surface as results, never exceptions.
		var store = StoreWithPendingSentinel();
		var attempts = 0;
		var handler = new MockHttpHandler(_ =>
		{
			attempts++;
			if (attempts == 1)
			{
				throw new HttpRequestException("reset", new SocketException((int)SocketError.ConnectionReset));
			}

			return Task.FromResult(Json(HttpStatusCode.OK, AcceptedPollJson));
		});
		using var client = CreateClient(handler, store);

		var result = await client.ResumePollingAsync(PollUrl, Ref);

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
	}

	[Fact]
	public async Task Resume_Timeout_ReturnsUnknown_LeavesSentinelPending()
	{
		// (Decision 13) A recovery resume that itself exhausts must NOT drop the record from the pending scan —
		// otherwise the next recovery pass never retries a transaction that may settle moments later (the
		// recovery-exhaustion window). Caller still gets Unknown; record stays pending.
		var store = StoreWithPendingSentinel();
		var handler = new MockHttpHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, PendingPollJson)));
		using var client = CreateClient(handler, store);

		var result = await client.ResumePollingAsync(PollUrl, Ref);

		Assert.Equal(SmartConnectTransactionStatus.Unknown, result.Status);
		// (F5) No transport cause leaks on the resume path either.
		Assert.Equal(SmartConnectFailureCause.None, result.FailureCause);
		// ResumePollingAsync has no transactionId parameter and never looks the persisted TransactionId up from
		// the store (by design — see Resume_NeverCallsSaveOrUpdatePollingDetails), so PollForResultAsync seeds
		// its transactionId local as null. The poll loop harvests the id from each poll body instead, so the
		// exhaustion result still carries the id the PENDING poll body reported ("txn-1" in PendingPollJson).
		Assert.Equal("txn-1", result.TransactionId);
		Assert.DoesNotContain(store.CallLog, e => e.StartsWith("UpdateCompleted:"));
		Assert.Null(store.Records[Ref].Status);
		Assert.Contains(await store.GetPendingTransactionsAsync(), p => p.ClientTransactionRef == Ref);
	}

	[Theory]
	[InlineData("relative/path")]
	[InlineData("foo:bar")]
	[InlineData("file:///x")]
	public async Task Resume_PresentButUnusableUrl_ReturnsPollingUrlInvalid_NeverThrows(string badUrl)
	{
		// (M3/F1/F8) A persisted polling URL is a runtime/data value, not a caller bug — a present-but-unusable
		// one (relative, or a non-http scheme like foo:/file: that Uri.TryCreate(Absolute) would still accept
		// but HttpClient would throw on) resolves to Unknown/PollingUrlInvalid, never a throw and never a send
		// attempt. The pre-crash sentinel is left pending for manual reconciliation.
		var store = StoreWithPendingSentinel();
		var sent = 0;
		var handler = new MockHttpHandler(_ =>
		{
			sent++;
			return Task.FromResult(Json(HttpStatusCode.OK, AcceptedPollJson));
		});
		using var client = CreateClient(handler, store);

		var result = await client.ResumePollingAsync(badUrl, Ref);

		Assert.Equal(SmartConnectTransactionStatus.Unknown, result.Status);
		Assert.Equal(SmartConnectFailureCause.PollingUrlInvalid, result.FailureCause);
		Assert.Equal(0, sent);
		Assert.Null(store.Records[Ref].Status);
	}

	[Fact]
	public async Task Resume_NullOrEmptyArguments_Throw()
	{
		using var client = CreateClient(new MockHttpHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, AcceptedPollJson))), StoreWithPendingSentinel());

		await Assert.ThrowsAsync<ArgumentNullException>(() => client.ResumePollingAsync(null!, Ref));
		await Assert.ThrowsAsync<ArgumentException>(() => client.ResumePollingAsync(" ", Ref));
		await Assert.ThrowsAsync<ArgumentNullException>(() => client.ResumePollingAsync(PollUrl, null!));
		await Assert.ThrowsAsync<ArgumentException>(() => client.ResumePollingAsync(PollUrl, " "));
	}

	// --- GetLastTransactionResultAsync (diagnostic journal query) ---

	[Fact]
	public async Task GetLast_SendsJournalGetTransResultPost()
	{
		var handler = new MockHttpHandler(request => Task.FromResult(
			request.Method == HttpMethod.Post
				? Json(HttpStatusCode.OK, InitialResponseJson)
				: Json(HttpStatusCode.OK, AcceptedPollJson)));
		using var client = CreateClient(handler, new InMemoryTransactionStateStore());

		await client.GetLastTransactionResultAsync(CreateRegistration());

		Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
		Assert.Equal("https://unit.test/POS/Transaction", handler.Requests[0].Uri?.AbsoluteUri);
		// Literal expected body (protocol-fake rule).
		Assert.Equal(
			"POSRegisterID=11111111-2222-3333-4444-555555555555&POSBusinessName=Demo%20Business&POSVendorName=DemoVendor&TransactionMode=ASYNC&TransactionType=Journal.GetTransResult",
			handler.Requests[0].Body);
	}

	[Fact]
	public async Task GetLast_MakesNoStateStoreCallsAtAll()
	{
		// The driver's BeginReprocessTransaction owns the existing sentinel; the library must not touch
		// the store — not even at terminal state. Throw-hooks armed on everything.
		var store = new InMemoryTransactionStateStore
		{
			ThrowOnSave = new InvalidOperationException("no store calls"),
			ThrowOnUpdatePollingDetails = new InvalidOperationException("no store calls"),
			ThrowOnUpdateCompleted = new InvalidOperationException("no store calls")
		};
		var handler = new MockHttpHandler(request => Task.FromResult(
			request.Method == HttpMethod.Post
				? Json(HttpStatusCode.OK, InitialResponseJson)
				: Json(HttpStatusCode.OK, AcceptedPollJson)));
		using var client = CreateClient(handler, store);

		var result = await client.GetLastTransactionResultAsync(CreateRegistration());

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		Assert.Empty(store.CallLog);
	}

	[Fact]
	public async Task GetLast_Timeout_ReturnsUnknown_StillNoStoreCalls()
	{
		var store = new InMemoryTransactionStateStore();
		var handler = new MockHttpHandler(request => Task.FromResult(
			request.Method == HttpMethod.Post
				? Json(HttpStatusCode.OK, InitialResponseJson)
				: Json(HttpStatusCode.OK, PendingPollJson)));
		using var client = CreateClient(handler, store);

		var result = await client.GetLastTransactionResultAsync(CreateRegistration());

		Assert.Equal(SmartConnectTransactionStatus.Unknown, result.Status);
		Assert.Empty(store.CallLog);
	}

	[Fact]
	public async Task GetLast_PostTransportFailure_ThrowsTypedTransportException()
	{
		// (R5/Decision 9) The journal query is non-financial and idempotent — the driver may safely
		// retry the whole call regardless of Delivery. Same contract shape as PairAsync.
		var handler = new MockHttpHandler(_ => throw new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused)));
		using var client = CreateClient(handler, new InMemoryTransactionStateStore());

		var thrown = await Assert.ThrowsAsync<SmartConnectTransportException>(() => client.GetLastTransactionResultAsync(CreateRegistration()));

		Assert.Equal(SmartConnectRequestDelivery.NotSent, thrown.Delivery);
	}

	[Fact]
	public async Task GetLast_PollPhase_StaysResultBased()
	{
		// (R5) Once the POST succeeded, transport contracts switch to poll-loop semantics: a verdict on
		// the URL is a result, never an exception.
		var posted = 0;
		var handler = new MockHttpHandler(request =>
		{
			if (request.Method == HttpMethod.Post)
			{
				posted++;
				return Task.FromResult(Json(HttpStatusCode.OK, InitialResponseJson));
			}

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		});
		using var client = CreateClient(handler, new InMemoryTransactionStateStore());

		var result = await client.GetLastTransactionResultAsync(CreateRegistration());

		Assert.Equal(1, posted);
		Assert.Equal(SmartConnectFailureCause.PollingUrlInvalid, result.FailureCause);
	}

	[Fact]
	public async Task GetLast_ServiceRejection_ReturnsFailedServiceError()
	{
		var handler = new MockHttpHandler(_ => Task.FromResult(Json(HttpStatusCode.BadRequest, "{\"error\": \"unsupported\"}")));
		using var client = CreateClient(handler, new InMemoryTransactionStateStore());

		var result = await client.GetLastTransactionResultAsync(CreateRegistration());

		Assert.Equal(SmartConnectTransactionStatus.Failed, result.Status);
		Assert.Equal(SmartConnectFailureCause.ServiceError, result.FailureCause);
	}

	[Fact]
	public async Task GetLast_ValidationGuards()
	{
		using var client = CreateClient(new MockHttpHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, InitialResponseJson))), new InMemoryTransactionStateStore());

		await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetLastTransactionResultAsync(null!));

		var request = CreateRegistration();
		request.POSRegisterID = string.Empty;
		await Assert.ThrowsAsync<ArgumentException>(() => client.GetLastTransactionResultAsync(request));
	}
}
