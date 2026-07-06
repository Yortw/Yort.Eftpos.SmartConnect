using System;
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
/// Tests for the non-financial operation APIs (Task 12.7). The boundary: money → sentinel + never-throws
/// results (<c>ProcessTransactionAsync</c>); not-money → ZERO state-store calls + typed transport throws on the
/// POST, result-based polling. Non-financial operations return <see cref="SmartConnectOperationResult"/>
/// (<see cref="SmartConnectOperationStatus"/> from the response's <c>Result == "OK"</c>); the journal query
/// returns a financial <see cref="SmartConnectTransactionResult"/> (it reports a recovered transaction). The
/// escape hatch rejects the library's own financial type names (J2 — the F5-bypass guard) while passing
/// genuinely unknown strings through.
/// </summary>
public class SmartConnectClientNonFinancialTests
{
	private const string Token = "tok123";
	private const string PollUrl = "https://poll.unit.test/poll?merchantAccessToken=" + Token;

	private static readonly string InitialResponseJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {\"PollingUrl\": \"" + PollUrl + "\"}}";

	private const string AcceptedPollJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"COMPLETED\", " +
		"\"data\": {\"TransactionResult\": \"OK-ACCEPTED\", \"Result\": \"OK\"}}";

	// The genuine non-financial COMPLETED shape: NO TransactionResult code, just Result=OK plus operation-
	// specific fields (mirrors the live Terminal.GetStatus response). The financial mapper would map this to
	// Failed; the operation mapper must read Result=="OK" and report Succeeded.
	private const string OperationOkPollJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"COMPLETED\", " +
		"\"data\": {\"Result\": \"OK\", \"Status\": \"READY\"}}";

	private const string OperationFailedPollJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"COMPLETED\", " +
		"\"data\": {\"Result\": \"FAILED\"}}";

	// A COMPLETED non-financial body carrying no Result field at all — the safety branch must not assert a
	// success it cannot see, so it maps to Unknown.
	private const string OperationNoResultPollJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"COMPLETED\", " +
		"\"data\": {\"Status\": \"READY\"}}";

	private static HttpResponseMessage Json(HttpStatusCode status, string json)
		=> new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

	// A 200 whose Content-Type declares an unresolvable charset — ReadAsStringAsync throws on decode.
	private static HttpResponseMessage BadCharset(string json)
	{
		var content = new StringContent(json, Encoding.UTF8, "application/json");
		content.Headers.ContentType!.CharSet = "foo-bar";
		return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
	}

	/// <summary>First request gets the initial POST response; later requests get <paramref name="completedPollJson"/>.</summary>
	private static MockHttpHandler Handler(string completedPollJson)
	{
		var index = -1;
		return new MockHttpHandler(_ =>
		{
			var i = Interlocked.Increment(ref index);
			return Task.FromResult(Json(HttpStatusCode.OK, i == 0 ? InitialResponseJson : completedPollJson));
		});
	}

	private static MockHttpHandler HappyHandler() => Handler(AcceptedPollJson);

	private static SmartConnectRegistration CreateRegistration()
	{
		return new SmartConnectRegistration
		{
			POSRegisterID = "11111111-2222-3333-4444-555555555555",
			POSBusinessName = "Demo Business",
			POSVendorName = "DemoVendor"
		};
	}

	private static SmartConnectClient CreateClient(MockHttpHandler handler, InMemoryTransactionStateStore? store = null, ILogger? logger = null)
	{
		var client = new SmartConnectClient(new SmartConnectClientConfiguration
		{
			BaseUrl = new Uri("https://unit.test/POS"),
			StateStore = store ?? new InMemoryTransactionStateStore(),
			HttpClient = new HttpClient(handler),
			Logger = logger,
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

	private static Task<SmartConnectTransactionResult> InvokeAcquirer(SmartConnectClient client, string method, SmartConnectRegistration registration)
	{
		switch (method)
		{
			case "Logon": return client.LogonAsync(registration);
			case "Inquiry": return client.SettlementInquiryAsync(registration);
			case "Cutover": return client.SettlementCutoverAsync(registration);
			default: throw new ArgumentOutOfRangeException(nameof(method));
		}
	}

	[Fact]
	public async Task TerminalStatus_SendsTypeAndSucceedsOnResultOk()
	{
		// Terminal.GetStatus has the one divergent shape — Result=OK but NO TransactionResult (the financial
		// mapper would return Failed). It is the one genuine OPERATION result; the operation mapper returns
		// Succeeded. This pins both the mapping and that Terminal.GetStatus stays a SmartConnectOperationResult.
		var handler = Handler(OperationOkPollJson);
		using var client = CreateClient(handler);

		SmartConnectOperationResult result = await client.GetTerminalStatusAsync(CreateRegistration());

		Assert.Equal(SmartConnectOperationStatus.Succeeded, result.Status);
		Assert.Equal(
			"POSRegisterID=11111111-2222-3333-4444-555555555555&POSBusinessName=Demo%20Business&POSVendorName=DemoVendor&TransactionMode=ASYNC&TransactionType=Terminal.GetStatus",
			handler.Requests[0].Body);
	}

	[Theory]
	[InlineData("Logon", "Acquirer.Logon")]
	[InlineData("Inquiry", "Acquirer.Settlement.Inquiry")]
	[InlineData("Cutover", "Acquirer.Settlement.Cutover")]
	public async Task AcquirerOps_SendRegistrationTripleAndType_AndReturnTransactionResult(string method, string expectedWireType)
	{
		// Verified live (2026-06-17): Acquirer.Logon / Settlement.* return a transaction-shaped envelope
		// (TransactionResult=OK-ACCEPTED) and map like a transaction -> SmartConnectTransactionResult (Decision 11).
		var handler = HappyHandler(); // AcceptedPollJson carries TransactionResult=OK-ACCEPTED.
		using var client = CreateClient(handler);

		SmartConnectTransactionResult result = await InvokeAcquirer(client, method, CreateRegistration());

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		// Literal expected body (protocol-fake rule).
		Assert.Equal(
			"POSRegisterID=11111111-2222-3333-4444-555555555555&POSBusinessName=Demo%20Business&POSVendorName=DemoVendor&TransactionMode=ASYNC&TransactionType=" + Uri.EscapeDataString(expectedWireType),
			handler.Requests[0].Body);
	}

	[Fact]
	public async Task Journal_SendsCorrectTypeAndReturnsFinancialResult()
	{
		// The journal query routes through the same core but reports a recovered TRANSACTION, so it keeps the
		// financial result shape and outcome enum.
		var handler = HappyHandler();
		using var client = CreateClient(handler);

		SmartConnectTransactionResult result = await client.GetLastTransactionResultAsync(CreateRegistration());

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		Assert.EndsWith("&TransactionType=Journal.GetTransResult", handler.Requests[0].Body);
	}

	[Fact]
	public async Task NonFinancial_ResultNotOk_IsFailedNotSucceeded()
	{
		// The requirement, not the mechanism: a COMPLETED non-financial body whose Result is NOT "OK" must NOT
		// be reported as success.
		var handler = Handler(OperationFailedPollJson);
		using var client = CreateClient(handler);

		var result = await client.GetTerminalStatusAsync(CreateRegistration());

		Assert.Equal(SmartConnectOperationStatus.Failed, result.Status);
		Assert.NotNull(result.ErrorMessage);
	}

	[Fact]
	public async Task EscapeHatch_SendsArbitraryUnknownType()
	{
		// The invariant half of the J2 guard: genuinely unknown strings pass through.
		var handler = HappyHandler();
		using var client = CreateClient(handler);

		await client.ExecuteNonFinancialAsync(CreateRegistration(), "Terminal.Reboot");

		Assert.EndsWith("&TransactionType=Terminal.Reboot", handler.Requests[0].Body);
	}

	[Fact]
	public async Task EscapeHatch_ReservedCharactersArrivePercentEncoded()
	{
		// (J5) A hostile/odd type string must not splice a phantom form pair.
		var handler = HappyHandler();
		using var client = CreateClient(handler);

		await client.ExecuteNonFinancialAsync(CreateRegistration(), "A B&C=D");

		Assert.EndsWith("&TransactionType=A%20B%26C%3DD", handler.Requests[0].Body);
	}

	[Theory]
	[InlineData("Card.Purchase")]
	[InlineData("Card.Refund")]
	[InlineData("Card.PurchasePlusCash")]
	[InlineData("Card.CashAdvance")]
	[InlineData("Card.Authorise")]
	[InlineData("Card.Finalise")]
	[InlineData("QR.Merchant.Purchase")]
	[InlineData("QR.Consumer.Purchase")]
	[InlineData("QR.Refund")]
	[InlineData("card.purchase")]
	public async Task EscapeHatch_RejectsFinancialTypes(string financialType)
	{
		// (J2) The F5-bypass guard: a financial transaction through the no-sentinel path would move money
		// with no crash-recovery record. Case-insensitive, from the ONE shared list.
		var handler = HappyHandler();
		using var client = CreateClient(handler);

		await Assert.ThrowsAsync<ArgumentException>(() => client.ExecuteNonFinancialAsync(CreateRegistration(), financialType));
		Assert.Equal(0, handler.RequestCount);
	}

	[Fact]
	public async Task NonFinancial_MakesNoStateStoreCalls_IncludingTerminalState()
	{
		var store = new InMemoryTransactionStateStore
		{
			ThrowOnSave = new InvalidOperationException("no store calls"),
			ThrowOnUpdatePollingDetails = new InvalidOperationException("no store calls"),
			ThrowOnUpdateCompleted = new InvalidOperationException("no store calls")
		};
		using var client = CreateClient(Handler(OperationOkPollJson), store);

		var result = await client.GetTerminalStatusAsync(CreateRegistration());

		Assert.Equal(SmartConnectOperationStatus.Succeeded, result.Status);
		Assert.Empty(store.CallLog);
	}

	[Fact]
	public async Task NonFinancial_PostTransportFailure_ThrowsTypedException()
	{
		var handler = new MockHttpHandler(_ => throw new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused)));
		using var client = CreateClient(handler);

		var thrown = await Assert.ThrowsAsync<SmartConnectTransportException>(() => client.GetTerminalStatusAsync(CreateRegistration()));

		Assert.Equal(SmartConnectRequestDelivery.NotSent, thrown.Delivery);
	}

	[Fact]
	public async Task NonFinancial_PollUrlRejected_IsUnknown()
	{
		// An invalid/expired polling URL means the operation outcome cannot be retrieved -> Unknown (the
		// financial path's PollingUrlInvalid maps onto the operation's Unknown).
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
		using var client = CreateClient(handler);

		var result = await client.GetTerminalStatusAsync(CreateRegistration());

		Assert.Equal(1, posted);
		Assert.Equal(SmartConnectOperationStatus.Unknown, result.Status);
	}

	[Theory]
	[InlineData(HttpStatusCode.BadRequest)]
	[InlineData(HttpStatusCode.Unauthorized)]
	[InlineData(HttpStatusCode.Forbidden)]
	public async Task PostDefinitive4xx_OperationFailed_SurfacesServiceError(HttpStatusCode status)
	{
		// (J8) 401-unpaired is the demo's literal first probe on a fresh install. A 4xx is a definitive
		// pre-processing rejection: the operation is Failed and the SERVICE's own message ("not paired") must
		// reach the caller, not a generic placeholder; the journal surfaces the financial Failed/ServiceError.
		var handler = new MockHttpHandler(_ => Task.FromResult(Json(status, "{\"error\": \"not paired\"}")));
		using var client = CreateClient(handler);

		var operationResult = await client.GetTerminalStatusAsync(CreateRegistration());
		var journalResult = await client.GetLastTransactionResultAsync(CreateRegistration());

		Assert.Equal(SmartConnectOperationStatus.Failed, operationResult.Status);
		Assert.Equal("not paired", operationResult.ErrorMessage);
		Assert.Equal(SmartConnectTransactionStatus.Failed, journalResult.Status);
		Assert.Equal(SmartConnectFailureCause.ServiceError, journalResult.FailureCause);
		Assert.Equal("not paired", journalResult.ErrorMessage);
	}

	[Fact]
	public async Task Post4xx_NoErrorBody_StillCarriesNonEmptyMessage()
	{
		// (F5 invariant) A ServiceError operation result must never carry a null/empty ErrorMessage — when the
		// body yields no extractable message, the HTTP status line stands in, never a swallowed null.
		var handler = new MockHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));
		using var client = CreateClient(handler);

		var operationResult = await client.GetTerminalStatusAsync(CreateRegistration());

		Assert.Equal(SmartConnectOperationStatus.Failed, operationResult.Status);
		Assert.False(string.IsNullOrEmpty(operationResult.ErrorMessage));
	}

	[Theory]
	[InlineData(HttpStatusCode.InternalServerError)]
	[InlineData(HttpStatusCode.BadGateway)]
	[InlineData(HttpStatusCode.ServiceUnavailable)]
	[InlineData(HttpStatusCode.GatewayTimeout)]
	[InlineData(HttpStatusCode.RequestTimeout)]
	public async Task PostIntermediary5xx408_OperationAndJournalUnknown(HttpStatusCode status)
	{
		// (I1) A 5xx/408 can be generated by an intermediary AFTER the service received the POST — the outcome
		// is unprovable. For the STATE-CHANGING settlement cutover, Failed ("blind retry will fail again") would
		// invite a second settlement window over an executed cutover, so the whole non-financial path maps
		// 5xx/408 to Unknown. Read-only ops (status/journal) get the same honest Unknown.
		var handler = new MockHttpHandler(_ => Task.FromResult(Json(status, "{\"error\": \"upstream\"}")));
		using var client = CreateClient(handler);

		var operationResult = await client.GetTerminalStatusAsync(CreateRegistration());
		var cutoverResult = await client.SettlementCutoverAsync(CreateRegistration());
		var journalResult = await client.GetLastTransactionResultAsync(CreateRegistration());

		Assert.Equal(SmartConnectOperationStatus.Unknown, operationResult.Status);
		Assert.Equal(SmartConnectTransactionStatus.Unknown, cutoverResult.Status);
		Assert.Equal(SmartConnectFailureCause.TransportUnknown, cutoverResult.FailureCause);
		Assert.Equal(SmartConnectTransactionStatus.Unknown, journalResult.Status);
		Assert.Equal(SmartConnectFailureCause.TransportUnknown, journalResult.FailureCause);
		// The intermediary's message is surfaced on both shapes, not dropped.
		Assert.Equal("upstream", operationResult.ErrorMessage);
		Assert.Equal("upstream", cutoverResult.ErrorMessage);
	}

	[Fact]
	public async Task NonFinancial_UnusablePollingUrlInBody_IsUnknown()
	{
		// (M3/F4) The non-financial POST returned an unusable polling URL — operation Unknown, no throw, no
		// store interaction (the non-financial path never touches the store).
		var badInitial = "{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {\"PollingUrl\": \"relative/path\"}}";
		var handler = new MockHttpHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, badInitial)));
		using var client = CreateClient(handler);

		var result = await client.GetTerminalStatusAsync(CreateRegistration());

		Assert.Equal(SmartConnectOperationStatus.Unknown, result.Status);
	}

	[Fact]
	public async Task NonFinancial_UndecodableBody_IsUnknown()
	{
		// (I2/F6) The non-financial POST answered 200 but its body cannot be decoded — no polling URL, outcome
		// unprovable → operation Unknown, no thrown decode exception, no store interaction.
		var handler = new MockHttpHandler(_ => Task.FromResult(BadCharset(InitialResponseJson)));
		using var client = CreateClient(handler);

		var result = await client.GetTerminalStatusAsync(CreateRegistration());

		Assert.Equal(SmartConnectOperationStatus.Unknown, result.Status);
	}

	[Fact]
	public async Task NonFinancialCompletedWithoutResult_IsUnknown()
	{
		// (T3) The "never assert a success we cannot see" branch: a COMPLETED body with no Result field maps to
		// Unknown, never Succeeded.
		var handler = Handler(OperationNoResultPollJson);
		using var client = CreateClient(handler);

		var result = await client.GetTerminalStatusAsync(CreateRegistration());

		Assert.Equal(SmartConnectOperationStatus.Unknown, result.Status);
	}

	[Fact]
	public async Task NonFinancial_LogsStructuredTemplateWithTransactionType()
	{
		// (J6) Operations are reconstructible from logs, structured.
		var logger = new ListLogger();
		using var client = CreateClient(HappyHandler(), logger: logger);

		await client.GetTerminalStatusAsync(CreateRegistration());

		var sending = logger.Entries.First(e => e.Message.Contains("Terminal.GetStatus"));
		Assert.Contains(sending.State, p => p.Key == "TransactionType" && (string?)p.Value == "Terminal.GetStatus");
	}

	[Fact]
	public async Task NonFinancial_TokenNeverAppearsInAnyLogEntry()
	{
		// (J6/G7) These methods poll the token-bearing URL too — the F2 sweep holds on this path.
		var logger = new ListLogger();
		using var client = CreateClient(HappyHandler(), logger: logger);

		await client.GetTerminalStatusAsync(CreateRegistration());

		Assert.NotEmpty(logger.Entries);
		Assert.All(logger.Entries, e =>
		{
			Assert.DoesNotContain(Token, e.Message);
			Assert.DoesNotContain(Token, e.Exception?.ToString() ?? string.Empty);
			Assert.All(e.State, pair => Assert.DoesNotContain(Token, pair.Value?.ToString() ?? string.Empty));
		});
	}

	[Fact]
	public async Task NonFinancial_ValidationGuards()
	{
		using var client = CreateClient(HappyHandler());

		await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetTerminalStatusAsync(null!));

		var blank = CreateRegistration();
		blank.POSVendorName = string.Empty;
		await Assert.ThrowsAsync<ArgumentException>(() => client.GetTerminalStatusAsync(blank));

		await Assert.ThrowsAsync<ArgumentNullException>(() => client.ExecuteNonFinancialAsync(CreateRegistration(), null!));
		await Assert.ThrowsAsync<ArgumentException>(() => client.ExecuteNonFinancialAsync(CreateRegistration(), "  "));
	}

	[Fact]
	public async Task NonFinancial_BlankField_ArgumentExceptionNamesRegistration()
	{
		// (M4) The thrown ArgumentException must name the actual parameter (registration), not a hardcoded
		// "request" that does not exist on these methods.
		using var client = CreateClient(HappyHandler());
		var blank = CreateRegistration();
		blank.POSVendorName = string.Empty;

		var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.GetTerminalStatusAsync(blank));

		Assert.Equal("registration", ex.ParamName);
	}

	[Fact]
	public async Task NonFinancial_AfterDispose_Throws()
	{
		var client = CreateClient(HappyHandler());
		client.Dispose();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetTerminalStatusAsync(CreateRegistration()));
	}

	[Fact]
	public async Task ProcessTransaction_AmountGuard_Unchanged_ForNonFinancialTypes()
	{
		// The invariant the whole task hangs on: the financial gate never weakened. A non-financial type
		// pushed through the FINANCIAL path with no amount still throws — use the dedicated methods.
		using var client = CreateClient(HappyHandler());

		await Assert.ThrowsAsync<ArgumentException>(() => client.ProcessTransactionAsync(new SmartConnectTransactionRequest
		{
			TransactionType = SmartConnectTransactionType.TerminalGetStatus,
			AmountTotal = Money.FromCents(0),
			POSRegisterID = "11111111-2222-3333-4444-555555555555",
			POSBusinessName = "Demo Business",
			POSVendorName = "DemoVendor",
			ClientTransactionRef = "ref-1"
		}));
	}
}
