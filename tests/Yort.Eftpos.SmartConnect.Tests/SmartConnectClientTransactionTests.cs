using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
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
	public async Task Process_CardPurchase_NeverIncludesAmountCash()
	{
		// Invariant: AmountCash rides only on PurchasePlusCash — even if a caller sets it on another type.
		var handler = PendingResponseHandler();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler)));

		var request = CreateRequest();
		request.AmountCash = Money.FromCents(250);
		await client.ProcessTransactionAsync(request);

		Assert.DoesNotContain("AmountCash", handler.Requests[0].Body);
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
	public async Task Process_AfterDispose_Throws()
	{
		var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(PendingResponseHandler())));
		client.Dispose();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ProcessTransactionAsync(CreateRequest()));
	}
}
