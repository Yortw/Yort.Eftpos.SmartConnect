using System.Collections.Generic;
using Xunit;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

public class ResultVisualsTests
{
	private static readonly Dictionary<SmartConnectTransactionStatus, string> TxnCaptions = new()
	{
		[SmartConnectTransactionStatus.Unknown] = "Outcome unknown",
		[SmartConnectTransactionStatus.Accepted] = "Approved",
		[SmartConnectTransactionStatus.Declined] = "Declined",
		[SmartConnectTransactionStatus.Cancelled] = "Cancelled",
		[SmartConnectTransactionStatus.DeviceOffline] = "Terminal offline",
		[SmartConnectTransactionStatus.Failed] = "Failed",
	};

	private static readonly Dictionary<SmartConnectOperationStatus, string> OpCaptions = new()
	{
		[SmartConnectOperationStatus.Unknown] = "Outcome unknown",
		[SmartConnectOperationStatus.Succeeded] = "Completed",
		[SmartConnectOperationStatus.Failed] = "Failed",
	};

	[Theory]
	[InlineData(SmartConnectTransactionStatus.Accepted, ResultSeverity.Success)]
	[InlineData(SmartConnectTransactionStatus.Unknown, ResultSeverity.Ambiguous)]
	[InlineData(SmartConnectTransactionStatus.Declined, ResultSeverity.Negative)]
	[InlineData(SmartConnectTransactionStatus.Cancelled, ResultSeverity.Negative)]
	[InlineData(SmartConnectTransactionStatus.DeviceOffline, ResultSeverity.Negative)]
	[InlineData(SmartConnectTransactionStatus.Failed, ResultSeverity.Negative)]
	internal void ForTransaction_MapsSeverityForEveryStatus(SmartConnectTransactionStatus status, ResultSeverity expected)
	{
		var visual = ResultVisuals.ForTransaction(status, TxnCaptions);
		Assert.Equal(expected, visual.Severity);
		Assert.Equal(TxnCaptions[status], visual.Caption);
	}

	[Theory]
	[InlineData(SmartConnectOperationStatus.Succeeded, ResultSeverity.Success)]
	[InlineData(SmartConnectOperationStatus.Unknown, ResultSeverity.Ambiguous)]
	[InlineData(SmartConnectOperationStatus.Failed, ResultSeverity.Negative)]
	internal void ForOperation_MapsSeverityForEveryStatus(SmartConnectOperationStatus status, ResultSeverity expected)
	{
		var visual = ResultVisuals.ForOperation(status, errorMessage: null, OpCaptions);
		Assert.Equal(expected, visual.Severity);
		Assert.Equal(OpCaptions[status], visual.Caption);
	}

	[Fact]
	public void ForOperation_Failed_CarriesErrorMessageAsDetail()
	{
		var visual = ResultVisuals.ForOperation(SmartConnectOperationStatus.Failed, "Acquirer rejected", OpCaptions);
		Assert.Equal("Acquirer rejected", visual.Detail);
	}

	[Fact]
	public void ForOperation_NonFailed_HasNoDetail()
	{
		var visual = ResultVisuals.ForOperation(SmartConnectOperationStatus.Succeeded, "ignored", OpCaptions);
		Assert.Null(visual.Detail);
	}

	// F2: a consumer-removed caption key must degrade to the enum name, never throw.
	[Fact]
	public void ForTransaction_MissingCaptionKey_FallsBackToEnumName()
	{
		var empty = new Dictionary<SmartConnectTransactionStatus, string>();
		var visual = ResultVisuals.ForTransaction(SmartConnectTransactionStatus.Declined, empty);
		Assert.Equal("Declined", visual.Caption);
		Assert.Equal(ResultSeverity.Negative, visual.Severity);
	}

	[Fact]
	public void ForOperation_MissingCaptionKey_FallsBackToEnumName()
	{
		var empty = new Dictionary<SmartConnectOperationStatus, string>();
		var visual = ResultVisuals.ForOperation(SmartConnectOperationStatus.Succeeded, errorMessage: null, empty);
		Assert.Equal("Succeeded", visual.Caption);
	}
}
