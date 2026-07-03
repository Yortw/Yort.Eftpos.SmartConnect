using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Yort.Eftpos.SmartConnect.Tests.Helpers;

namespace Yort.Eftpos.SmartConnect.Tests;

/// <summary>
/// Tests for the transaction POST itself: wire shape (literal expected bodies — never computed via the
/// library's own encoder), service-rejection mapping, and argument validation.
/// </summary>
public class SmartConnectClientTransactionTests
{

	private static SmartConnectClient WithVirtualTime(SmartConnectClient client)
	{
		// The polling loop is real now: without virtual time, any test whose handler keeps answering
		// PENDING would poll for the actual MaxPollDuration (minutes of wall-clock per test).
		var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
		client.Clock = () => now;
		client.PollDelay = delay =>
		{
			now += delay;
			return Task.CompletedTask;
		};
		return client;
	}
	private const string Ref = "100123-abc";

	private static readonly string InitialResponseJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {\"PollingUrl\": \"https://poll.unit.test/poll?merchantAccessToken=tok123\"}}";

	private static SmartConnectTransactionRequest CreateRequest()
	{
		return new SmartConnectTransactionRequest
		{
			TransactionType = SmartConnectTransactionType.CardPurchase,
			AmountTotal = Money.FromCents(1250),
			POSRegisterID = "11111111-2222-3333-4444-555555555555",
			POSBusinessName = "Demo Business",
			POSVendorName = "Ontempo",
			ClientTransactionRef = Ref
		};
	}

	private static SmartConnectClientConfiguration CreateConfiguration(MockHttpHandler handler, InMemoryTransactionStateStore? store = null)
	{
		return new SmartConnectClientConfiguration
		{
			BaseUrl = new Uri("https://unit.test/POS"),
			StateStore = store ?? new InMemoryTransactionStateStore(),
			HttpClient = new HttpClient(handler)
		};
	}

	private static MockHttpHandler PendingResponseHandler()
	{
		return new MockHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(InitialResponseJson, Encoding.UTF8, "application/json")
		}));
	}

	[Fact]
	public async Task Process_SendsFormEncodedPostToTransactionUrl()
	{
		var handler = PendingResponseHandler();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler)));

		await client.ProcessTransactionAsync(CreateRequest());

		// Requests after the first are poll GETs — the POST itself is always the first request.
		var request = handler.Requests[0];
		Assert.Equal(HttpMethod.Post, request.Method);
		Assert.Equal("https://unit.test/POS/Transaction", request.Uri?.AbsoluteUri);
		Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
	}

	[Fact]
	public async Task Process_SendsMandatoryFields()
	{
		var handler = PendingResponseHandler();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler)));

		await client.ProcessTransactionAsync(CreateRequest());

		// Literal expected body (protocol-fake rule): an encoding defect cannot self-confirm here.
		Assert.Equal(
			"POSRegisterID=11111111-2222-3333-4444-555555555555&POSBusinessName=Demo%20Business&POSVendorName=Ontempo&TransactionMode=ASYNC&TransactionType=Card.Purchase&AmountTotal=1250",
			handler.Requests[0].Body);
	}

	[Fact]
	public async Task Process_PurchasePlusCash_IncludesAmountCash()
	{
		var handler = PendingResponseHandler();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler)));

		var request = CreateRequest();
		request.TransactionType = SmartConnectTransactionType.CardPurchasePlusCash;
		request.AmountCash = Money.FromCents(250);
		await client.ProcessTransactionAsync(request);

		Assert.Equal(
			"POSRegisterID=11111111-2222-3333-4444-555555555555&POSBusinessName=Demo%20Business&POSVendorName=Ontempo&TransactionMode=ASYNC&TransactionType=Card.PurchasePlusCash&AmountTotal=1250&AmountCash=250",
			handler.Requests[0].Body);
	}

	[Fact]
	public async Task Process_Refund_SendsRefundTypeWithPositiveAmount()
	{
		// A refund's direction is carried by the TransactionType, not the sign — Card.Refund with a positive AmountTotal.
		var handler = PendingResponseHandler();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler)));

		var request = CreateRequest();
		request.TransactionType = SmartConnectTransactionType.CardRefund;
		await client.ProcessTransactionAsync(request);

		Assert.Equal(
			"POSRegisterID=11111111-2222-3333-4444-555555555555&POSBusinessName=Demo%20Business&POSVendorName=Ontempo&TransactionMode=ASYNC&TransactionType=Card.Refund&AmountTotal=1250",
			handler.Requests[0].Body);
	}

	[Fact]
	public async Task Process_UnderHostileCulture_SendsInvariantAmount()
	{
		// The amount is built via FromDecimal(12.50) and must serialise as integer cents (1250) even under a culture
		// whose decimal separator is ',' — proves the money wire path is InvariantCulture end to end.
		var original = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = new CultureInfo("de-DE");
			var handler = PendingResponseHandler();
			using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler)));

			var request = CreateRequest();
			request.AmountTotal = Money.FromDecimal(12.50m);
			await client.ProcessTransactionAsync(request);

			Assert.Equal(
				"POSRegisterID=11111111-2222-3333-4444-555555555555&POSBusinessName=Demo%20Business&POSVendorName=Ontempo&TransactionMode=ASYNC&TransactionType=Card.Purchase&AmountTotal=1250",
				handler.Requests[0].Body);
		}
		finally
		{
			CultureInfo.CurrentCulture = original;
		}
	}

	[Fact]
	public async Task Process_NonPurchasePlusCash_WithAmountCash_Throws()
	{
		// AmountCash is valid only for Card.PurchasePlusCash. Setting it on another type is a caller error, not a
		// silently-dropped money field — reject it rather than quietly omitting it from the wire.
		var handler = PendingResponseHandler();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler)));

		var request = CreateRequest(); // Card.Purchase
		request.AmountCash = Money.FromCents(250);

		await Assert.ThrowsAsync<ArgumentException>(() => client.ProcessTransactionAsync(request));
	}

	[Fact]
	public async Task Process_TransactionReferenceSet_IsIncluded()
	{
		// Vendor reference pairs Card.Authorise with Card.Finalise.
		var handler = PendingResponseHandler();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler)));

		var request = CreateRequest();
		request.TransactionReference = "preauth-77";
		await client.ProcessTransactionAsync(request);

		Assert.EndsWith("&TransactionReference=preauth-77", handler.Requests[0].Body);
	}

	// --- Service rejection (Decision 9: service answered → result) ---

	[Fact]
	public async Task Process_Http400_ReturnsFailedServiceError_NoException()
	{
		var store = new InMemoryTransactionStateStore();
		var handler = new MockHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
		{
			Content = new StringContent("{\"error\": \"Invalid register\"}", Encoding.UTF8, "application/json")
		}));
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler, store)));

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Failed, result.Status);
		Assert.Equal(SmartConnectFailureCause.ServiceError, result.FailureCause);
		// (R7) A service rejection must never read as "never sent, retry freely".
		Assert.NotEqual(SmartConnectFailureCause.TransportNotSent, result.FailureCause);
		// The service refused it — terminal; the sentinel closes as Failed.
		Assert.Equal(SmartConnectTransactionStatus.Failed, store.Records[Ref].Status);
		// The service's rejection reason is surfaced so a consumer can show/log WHY it failed.
		Assert.Equal("Invalid register", result.ErrorMessage);
	}

	[Theory]
	[InlineData(HttpStatusCode.Unauthorized)]
	[InlineData(HttpStatusCode.Forbidden)]
	[InlineData(HttpStatusCode.NotFound)]
	[InlineData(HttpStatusCode.TooManyRequests)]
	public async Task Process_Http4xx_ReturnsFailedServiceError_SentinelClosed(HttpStatusCode status)
	{
		// The 4xx bucket is a genuine verdict that the request was NOT processed (429 included:
		// rate-limited means refused wherever it was generated) — terminal Failed, sentinel closed.
		var store = new InMemoryTransactionStateStore();
		var handler = new MockHttpHandler(_ => Task.FromResult(new HttpResponseMessage(status)));
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler, store)));

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Failed, result.Status);
		Assert.Equal(SmartConnectFailureCause.ServiceError, result.FailureCause);
		Assert.Equal(SmartConnectTransactionStatus.Failed, store.Records[Ref].Status);
		// A bodyless rejection still surfaces a reason (the status line) rather than null.
		Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
		Assert.Contains(((int)status).ToString(), result.ErrorMessage);
	}

	[Theory]
	[InlineData(HttpStatusCode.InternalServerError)]
	[InlineData(HttpStatusCode.BadGateway)]
	[InlineData(HttpStatusCode.ServiceUnavailable)]
	[InlineData(HttpStatusCode.GatewayTimeout)]
	[InlineData(HttpStatusCode.RequestTimeout)]
	public async Task Process_Http5xxOr408_ReturnsUnknownTransportUnknown_SentinelPending(HttpStatusCode status)
	{
		// (Decision 9, 2026-07-02 update) 5xx/408 on the initial POST is routinely intermediary-generated
		// (LB/WAF/proxy) AFTER the origin received the request — epistemically the same state as a transport
		// timeout, which maps to Unknown. Labelling it Failed/ServiceError ("blind retry will fail again")
		// invites a re-tender over a possibly-live charge.
		var store = new InMemoryTransactionStateStore();
		var handler = new MockHttpHandler(_ => Task.FromResult(new HttpResponseMessage(status)));
		var logger = new ListLogger();
		var configuration = CreateConfiguration(handler, store);
		configuration.Logger = logger;
		using var client = WithVirtualTime(new SmartConnectClient(configuration));

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Unknown, result.Status);
		Assert.Equal(SmartConnectFailureCause.TransportUnknown, result.FailureCause);
		// The requirement, not the mechanism: this must never present as a terminal failure...
		Assert.NotEqual(SmartConnectTransactionStatus.Failed, result.Status);
		// ...and the sentinel must stay PENDING (null status) so the sale remains visible for
		// manual reconciliation — closing it would hide a possibly-live charge.
		Assert.Null(store.Records[Ref].Status);
		// Diagnosability: an ambiguous outcome is always logged as an Error with the ref.
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(Ref));
		// The gateway/proxy reason is surfaced on the Unknown result too (diagnostic text only).
		Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
		Assert.Contains(((int)status).ToString(), result.ErrorMessage);
	}

	[Fact]
	public async Task Process_Http200WithoutPollingUrl_ReturnsUnknownTransportUnknown()
	{
		// (F10-adjacent) The service answered 200 but the response is unusable — the transaction may be
		// live on the pinpad with no way to poll it. That is outcome-unknown, never "failed".
		var store = new InMemoryTransactionStateStore();
		var handler = new MockHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\"}", Encoding.UTF8, "application/json")
		}));
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler, store)));

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Unknown, result.Status);
		Assert.Equal(SmartConnectFailureCause.TransportUnknown, result.FailureCause);
		// Sentinel stays pending for recovery.
		Assert.Null(store.Records[Ref].Status);
	}

	// --- Validation guards (programming errors — these DO throw) ---

	[Fact]
	public async Task Process_NullRequest_Throws()
	{
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(PendingResponseHandler())));

		await Assert.ThrowsAsync<ArgumentNullException>(() => client.ProcessTransactionAsync(null!));
	}

	[Theory]
	[InlineData("ClientTransactionRef")]
	[InlineData("POSRegisterID")]
	[InlineData("POSBusinessName")]
	[InlineData("POSVendorName")]
	[InlineData("TransactionType")]
	public async Task Process_MissingMandatoryField_Throws(string fieldName)
	{
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(PendingResponseHandler())));

		var request = CreateRequest();
		switch (fieldName)
		{
			case "ClientTransactionRef":
				request.ClientTransactionRef = string.Empty;
				break;
			case "POSRegisterID":
				request.POSRegisterID = string.Empty;
				break;
			case "POSBusinessName":
				request.POSBusinessName = string.Empty;
				break;
			case "POSVendorName":
				request.POSVendorName = string.Empty;
				break;
			case "TransactionType":
				request.TransactionType = string.Empty;
				break;
		}

		await Assert.ThrowsAsync<ArgumentException>(() => client.ProcessTransactionAsync(request));
	}

	[Fact]
	public async Task Process_NonPositiveAmount_Throws()
	{
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(PendingResponseHandler())));

		var request = CreateRequest();
		request.AmountTotal = Money.FromCents(0);

		await Assert.ThrowsAsync<ArgumentException>(() => client.ProcessTransactionAsync(request));
	}

	[Fact]
	public async Task Process_PurchasePlusCash_NonPositiveCash_Throws()
	{
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(PendingResponseHandler())));

		var request = CreateRequest();
		request.TransactionType = SmartConnectTransactionType.CardPurchasePlusCash;
		request.AmountCash = Money.FromCents(0);

		await Assert.ThrowsAsync<ArgumentException>(() => client.ProcessTransactionAsync(request));
	}

	[Fact]
	public async Task Process_PurchasePlusCash_CashExceedsTotal_Throws()
	{
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(PendingResponseHandler())));

		var request = CreateRequest();
		request.TransactionType = SmartConnectTransactionType.CardPurchasePlusCash;
		request.AmountTotal = Money.FromCents(500);
		request.AmountCash = Money.FromCents(600);

		await Assert.ThrowsAsync<ArgumentException>(() => client.ProcessTransactionAsync(request));
	}

	// Disposing the HttpClient mid-send surfaces ObjectDisposedException; ProcessTransactionAsync must NOT
	// throw it (Decision 9 never-throws) — the request may have reached the service, so it resolves to Unknown.
	[Fact]
	public async Task Process_ObjectDisposedDuringSend_ReturnsUnknown_DoesNotThrow()
	{
		var handler = new MockHttpHandler(_ => throw new ObjectDisposedException("HttpClient"));
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler)));

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Unknown, result.Status);
		Assert.Equal(SmartConnectFailureCause.TransportUnknown, result.FailureCause);
	}

	[Fact]
	public async Task Process_AfterDispose_Throws()
	{
		var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(PendingResponseHandler())));
		client.Dispose();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ProcessTransactionAsync(CreateRequest()));
	}
}
