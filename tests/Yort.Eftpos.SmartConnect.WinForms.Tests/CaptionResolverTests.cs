using System.Collections.Generic;
using System.Linq;
using Xunit;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

public class CaptionResolverTests
{
	[Fact]
	public void Resolve_PrefersLibraryMessageWhenPresent()
	{
		var captions = DefaultCaptions.CreateStateCaptions();
		var status = new SmartConnectPollingStatus { State = SmartConnectPollingState.Polling, Message = "Insert card" };

		Assert.Equal("Insert card", CaptionResolver.Resolve(status, captions));
	}

	// Negative/invariant: when the library supplies a message, the default caption must NOT be used.
	[Fact]
	public void Resolve_DoesNotUseDefaultWhenMessagePresent()
	{
		var captions = DefaultCaptions.CreateStateCaptions();
		var status = new SmartConnectPollingStatus { State = SmartConnectPollingState.Polling, Message = "Insert card" };

		Assert.NotEqual(captions[SmartConnectPollingState.Polling], CaptionResolver.Resolve(status, captions));
	}

	[Fact]
	public void Resolve_FallsBackToStateCaptionWhenMessageNull()
	{
		var captions = DefaultCaptions.CreateStateCaptions();
		var status = new SmartConnectPollingStatus { State = SmartConnectPollingState.Delayed, Message = null };

		Assert.Equal(captions[SmartConnectPollingState.Delayed], CaptionResolver.Resolve(status, captions));
	}

	[Fact]
	public void Resolve_TreatsEmptyMessageAsAbsent()
	{
		var captions = DefaultCaptions.CreateStateCaptions();
		var status = new SmartConnectPollingStatus { State = SmartConnectPollingState.BackingOff, Message = "" };

		Assert.Equal(captions[SmartConnectPollingState.BackingOff], CaptionResolver.Resolve(status, captions));
	}

	// F10: a whitespace-only message is treated as absent (would otherwise show a blank caption).
	[Fact]
	public void Resolve_TreatsWhitespaceMessageAsAbsent()
	{
		var captions = DefaultCaptions.CreateStateCaptions();
		var status = new SmartConnectPollingStatus { State = SmartConnectPollingState.BackingOff, Message = "   " };

		Assert.Equal(captions[SmartConnectPollingState.BackingOff], CaptionResolver.Resolve(status, captions));
	}

	// F2: a removed state key degrades to the enum name rather than throwing.
	[Fact]
	public void Resolve_MissingStateKey_FallsBackToStateName()
	{
		var empty = new Dictionary<SmartConnectPollingState, string>();
		var status = new SmartConnectPollingStatus { State = SmartConnectPollingState.Delayed, Message = null };

		Assert.Equal("Delayed", CaptionResolver.Resolve(status, empty));
	}

	[Fact]
	public void Resolve_RespectsCustomisedCaption()
	{
		var captions = DefaultCaptions.CreateStateCaptions();
		captions[SmartConnectPollingState.NetworkError] = "Custom retry text";
		var status = new SmartConnectPollingStatus { State = SmartConnectPollingState.NetworkError, Message = null };

		Assert.Equal("Custom retry text", CaptionResolver.Resolve(status, captions));
	}

	[Fact]
	public void DefaultCaptionMaps_CoverEveryEnumValue()
	{
		var states = DefaultCaptions.CreateStateCaptions();
		Assert.True(System.Enum.GetValues<SmartConnectPollingState>().All(states.ContainsKey));

		var txn = DefaultCaptions.CreateTransactionResultCaptions();
		Assert.True(System.Enum.GetValues<SmartConnectTransactionStatus>().All(txn.ContainsKey));

		var op = DefaultCaptions.CreateOperationResultCaptions();
		Assert.True(System.Enum.GetValues<SmartConnectOperationStatus>().All(op.ContainsKey));
	}
}
