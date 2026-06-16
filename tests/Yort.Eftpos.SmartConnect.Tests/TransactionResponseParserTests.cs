using System.Text.Json;
using Xunit;
using Yort.Eftpos.SmartConnect.Internal;

namespace Yort.Eftpos.SmartConnect.Tests;

public class TransactionResponseParserTests
{
	private const string InitialResponse = @"{
		""transactionId"": ""f363c7de"",
		""transactionStatus"": ""PENDING"",
		""data"": { ""PollingUrl"": ""https://api.smart-connect.cloud/pos/transaction/f363c7de?merchantAccessToken=tok"" }
	}";

	private const string PendingResponse = @"{
		""transactionId"": ""f363c7de"",
		""transactionStatus"": ""PENDING"",
		""data"": { ""TransactionResult"": """", ""Result"": """" }
	}";

	private const string DelayedResponse = @"{
		""transactionStatus"": ""PENDING"",
		""data"": { ""TransactionResult"": ""OK-DELAYED"", ""Result"": ""DELAYED-TRANSACTION"" }
	}";

	private const string AcceptedResponse = @"{
		""transactionId"": ""abc-123"",
		""transactionTimeStamp"": ""20260311120000000000"",
		""transactionStatus"": ""COMPLETED"",
		""data"": {
			""TransactionResult"": ""OK-ACCEPTED"", ""Result"": ""OK"",
			""AuthId"": ""AUTH123"", ""AcquirerRef"": ""ACQ456"", ""TerminalRef"": ""TERM789"",
			""CardPan"": ""....1234"", ""CardType"": ""VISA"", ""AccountType"": ""CREDIT"",
			""AmountTotal"": ""1000"", ""AmountSurcharge"": ""50"", ""AmountTip"": ""0"",
			""Receipt"": ""MERCHANT COPY\nApproved""
		}
	}";

	// Journal.GetTransResult (Layer-2 recovery) returns the SUBJECT transaction's id in data.ReferenceId,
	// while the envelope transactionId is the query's OWN id (verified live, ADR Decision 10, 2026-06-16).
	private const string JournalResponse = @"{
		""transactionId"": ""query-own-id"",
		""transactionTimeStamp"": ""20260616120000000000"",
		""transactionStatus"": ""COMPLETED"",
		""data"": {
			""TransactionResult"": ""OK-ACCEPTED"", ""Result"": ""OK"",
			""ReferenceId"": ""subject-txn-id"",
			""AmountTotal"": ""111""
		}
	}";

	private static string Completed(string transactionResult, string result) => @"{
		""transactionStatus"": ""COMPLETED"",
		""data"": { ""TransactionResult"": """ + transactionResult + @""", ""Result"": """ + result + @""" }
	}";

	[Fact]
	public void ParseInitialResponse_ExtractsTransactionIdAndPollingUrl()
	{
		var initial = TransactionResponseParser.ParseInitialResponse(InitialResponse);
		Assert.Equal("f363c7de", initial.TransactionId);
		Assert.Equal("https://api.smart-connect.cloud/pos/transaction/f363c7de?merchantAccessToken=tok", initial.PollingUrl);
	}

	[Fact]
	public void ParsePoll_Pending_ReturnsPending()
	{
		Assert.Equal(PollProgress.Pending, TransactionResponseParser.ParsePollResponse(PendingResponse).Progress);
	}

	[Fact]
	public void ParsePoll_Delayed_ReturnsDelayed()
	{
		Assert.Equal(PollProgress.Delayed, TransactionResponseParser.ParsePollResponse(DelayedResponse).Progress);
	}

	[Fact]
	public void ParsePoll_Accepted_MapsAllFields()
	{
		var poll = TransactionResponseParser.ParsePollResponse(AcceptedResponse);
		Assert.Equal(PollProgress.Completed, poll.Progress);

		var result = poll.Result!;
		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		Assert.Equal("abc-123", result.TransactionId);
		Assert.Equal("AUTH123", result.AuthId);
		Assert.Equal("VISA", result.CardType);
		Assert.Equal("....1234", result.CardPan);
		Assert.Equal("CREDIT", result.AccountType);
		Assert.Equal(1000, result.AmountTotal.ToCents());
		Assert.Equal(50, result.AmountSurcharge.ToCents());
		Assert.Equal("MERCHANT COPY\nApproved", result.Receipt);
		Assert.Equal("20260311120000000000", result.ResponseTimestamp);
	}

	// On the journal path the envelope transactionId is the query's own id; the recovered transaction's
	// id is in data.ReferenceId. The result must surface both distinctly so callers can correlate.
	[Fact]
	public void ParsePoll_SurfacesDataReferenceIdDistinctFromEnvelopeTransactionId()
	{
		var result = TransactionResponseParser.ParsePollResponse(JournalResponse).Result!;
		Assert.Equal("query-own-id", result.TransactionId);
		Assert.Equal("subject-txn-id", result.ReferenceId);
	}

	[Fact]
	public void ParsePoll_Declined_MapsDeclined()
	{
		Assert.Equal(SmartConnectTransactionStatus.Declined, TransactionResponseParser.ParsePollResponse(Completed("OK-DECLINED", "OK")).Result!.Status);
	}

	[Fact]
	public void ParsePoll_Cancelled_MapsCancelled()
	{
		Assert.Equal(SmartConnectTransactionStatus.Cancelled, TransactionResponseParser.ParsePollResponse(Completed("CANCELLED", "OK")).Result!.Status);
	}

	[Fact]
	public void ParsePoll_CancelledWithFailedInterface_MapsDeviceOffline()
	{
		Assert.Equal(SmartConnectTransactionStatus.DeviceOffline, TransactionResponseParser.ParsePollResponse(Completed("CANCELLED", "FAILED-INTERFACE")).Result!.Status);
	}

	[Fact]
	public void ParsePoll_UnknownCombination_MapsFailed()
	{
		Assert.Equal(SmartConnectTransactionStatus.Failed, TransactionResponseParser.ParsePollResponse(Completed("SOMETHING-ELSE", "OK")).Result!.Status);
	}

	// F10: a COMPLETED envelope whose data is missing/unmappable -> Unknown (terminal), never silently Accepted.
	[Fact]
	public void ParsePoll_CompletedButNoData_MapsUnknown()
	{
		var json = @"{ ""transactionStatus"": ""COMPLETED"" }";
		var poll = TransactionResponseParser.ParsePollResponse(json);
		Assert.Equal(PollProgress.Completed, poll.Progress);
		Assert.Equal(SmartConnectTransactionStatus.Unknown, poll.Result!.Status);
	}

	// F10: malformed JSON throws (the client routes this as transient and retries within the timeout).
	[Fact]
	public void ParsePoll_MalformedJson_Throws()
	{
		// JsonDocument throws JsonReaderException, a subclass of JsonException — match the base (or derived).
		Assert.ThrowsAny<JsonException>(() => TransactionResponseParser.ParsePollResponse("{ not valid json"));
	}

	[Theory]
	[InlineData("OK-ACCEPTED", "OK", SmartConnectTransactionStatus.Accepted)]
	[InlineData("OK-DECLINED", "OK", SmartConnectTransactionStatus.Declined)]
	[InlineData("CANCELLED", "OK", SmartConnectTransactionStatus.Cancelled)]
	[InlineData("CANCELLED", "FAILED-INTERFACE", SmartConnectTransactionStatus.DeviceOffline)]
	[InlineData("OK-ACCEPTED", "FAILED", SmartConnectTransactionStatus.Failed)]
	[InlineData("WHATEVER", "OK", SmartConnectTransactionStatus.Failed)]
	public void MapOutcome_MapsPerTable(string transactionResult, string result, SmartConnectTransactionStatus expected)
	{
		Assert.Equal(expected, TransactionResponseParser.MapOutcome(transactionResult, result));
	}
}
