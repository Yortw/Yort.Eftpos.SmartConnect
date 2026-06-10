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
/// results (<c>ProcessTransactionAsync</c>); not-money → ZERO state-store calls + typed transport throws on
/// the POST, result-based polling. The escape hatch rejects the library's own financial type names (J2 —
/// the F5-bypass guard) while passing genuinely unknown strings through.
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

	private static HttpResponseMessage Json(HttpStatusCode status, string json)
		=> new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

	/// <summary>First request gets the initial POST response; later requests get the accepted poll.</summary>
	private static MockHttpHandler HappyHandler()
	{
		var index = -1;
		return new MockHttpHandler(_ =>
		{
			var i = Interlocked.Increment(ref index);
			return Task.FromResult(Json(HttpStatusCode.OK, i == 0 ? InitialResponseJson : AcceptedPollJson));
		});
	}

	private static SmartConnectRegistration CreateRegistration()
	{
		return new SmartConnectRegistration
		{
			POSRegisterID = "11111111-2222-3333-4444-555555555555",
			POSBusinessName = "Demo Business",
			POSVendorName = "Ontempo"
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

	private static Task<SmartConnectTransactionResult> Invoke(SmartConnectClient client, string method, SmartConnectRegistration registration)
	{
		switch (method)
		{
			case "Status": return client.GetTerminalStatusAsync(registration);
			case "Logon": return client.LogonAsync(registration);
			case "Inquiry": return client.SettlementInquiryAsync(registration);
			case "Cutover": return client.SettlementCutoverAsync(registration);
			case "Journal": return client.GetLastTransactionResultAsync(registration);
			default: throw new ArgumentOutOfRangeException(nameof(method));
		}
	}

	[Theory]
	[InlineData("Status", "Terminal.GetStatus")]
	[InlineData("Logon", "Acquirer.Logon")]
	[InlineData("Inquiry", "Acquirer.Settlement.Inquiry")]
	[InlineData("Cutover", "Acquirer.Settlement.Cutover")]
	[InlineData("Journal", "Journal.GetTransResult")]
	public async Task NonFinancial_SendsRegistrationTripleAndCorrectType(string method, string expectedWireType)
	{
		var handler = HappyHandler();
		using var client = CreateClient(handler);

		var result = await Invoke(client, method, CreateRegistration());

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		// Literal expected body (protocol-fake rule).
		Assert.Equal(
			"POSRegisterID=11111111-2222-3333-4444-555555555555&POSBusinessName=Demo%20Business&POSVendorName=Ontempo&TransactionMode=ASYNC&TransactionType=" + Uri.EscapeDataString(expectedWireType),
			handler.Requests[0].Body);
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
		using var client = CreateClient(HappyHandler(), store);

		var result = await client.GetTerminalStatusAsync(CreateRegistration());

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
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
	public async Task NonFinancial_PollPhase_StaysResultBased()
	{
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
		Assert.Equal(SmartConnectFailureCause.PollingUrlInvalid, result.FailureCause);
	}

	[Theory]
	[InlineData(HttpStatusCode.Unauthorized)]
	[InlineData(HttpStatusCode.InternalServerError)]
	public async Task NonFinancial_NonSuccessPost_ReturnsServiceError_ParityWithJournal(HttpStatusCode status)
	{
		// (J8) 401-unpaired is the demo's literal first probe on a fresh install. Parity with the journal
		// query's behaviour doubles as proof the methods route through the shared core, not a copy.
		var handler = new MockHttpHandler(_ => Task.FromResult(Json(status, "{\"error\": \"not paired\"}")));
		using var client = CreateClient(handler);

		var statusResult = await client.GetTerminalStatusAsync(CreateRegistration());
		var journalResult = await client.GetLastTransactionResultAsync(CreateRegistration());

		Assert.Equal(SmartConnectTransactionStatus.Failed, statusResult.Status);
		Assert.Equal(SmartConnectFailureCause.ServiceError, statusResult.FailureCause);
		Assert.Equal(journalResult.Status, statusResult.Status);
		Assert.Equal(journalResult.FailureCause, statusResult.FailureCause);
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
			POSVendorName = "Ontempo",
			ClientTransactionRef = "ref-1"
		}));
	}
}
