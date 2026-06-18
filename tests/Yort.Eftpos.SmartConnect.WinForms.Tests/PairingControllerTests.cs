using System;
using System.Threading.Tasks;
using Xunit;
using Yort.Eftpos.SmartConnect;
using Yort.Eftpos.SmartConnect.WinForms;
using Yort.Eftpos.SmartConnect.WinForms.Tests.Fakes;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

public class PairingControllerTests
{
	private static Func<string, Task<SmartConnectPairingResult>> Counting(out Counter counter, SmartConnectPairingResult result)
	{
		var c = new Counter();
		counter = c;
		return code =>
		{
			c.Calls++;
			c.LastCode = code;
			return Task.FromResult(result);
		};
	}

	private sealed class Counter
	{
		public int Calls;
		public string? LastCode;
	}

	[Fact]
	public async Task Cancel_AtFirstPrompt_ReturnsNull_AndNeverCallsCallback()
	{
		var view = new FakePairingView(new string?[] { null }, Array.Empty<bool>());
		var callback = Counting(out var counter, new SmartConnectPairingResult { Success = true });

		var result = await new PairingController().RunAsync(view, callback);

		Assert.Null(result);
		Assert.Equal(0, counter.Calls);   // cancel never pairs
	}

	[Fact]
	public async Task BlankCode_NeverCallsCallback_AndReprompts()
	{
		// First a blank code (must be ignored), then cancel.
		var view = new FakePairingView(new string?[] { "   ", null }, Array.Empty<bool>());
		var callback = Counting(out var counter, new SmartConnectPairingResult { Success = true });

		var result = await new PairingController().RunAsync(view, callback);

		Assert.Null(result);
		Assert.Equal(0, counter.Calls);   // blank code must not reach the callback
	}

	[Fact]
	public async Task SuccessfulCode_ShowsSuccess_ReturnsResult()
	{
		var view = new FakePairingView(new string?[] { "1234" }, Array.Empty<bool>());
		var expected = new SmartConnectPairingResult { Success = true };
		var callback = Counting(out var counter, expected);

		var result = await new PairingController().RunAsync(view, callback);

		Assert.Same(expected, result);
		Assert.Equal(1, counter.Calls);
		Assert.Equal("1234", counter.LastCode);   // trimmed/forwarded
		Assert.Same(expected, view.SuccessShown);
	}

	[Fact]
	public async Task ServiceRejection_ShowsNegativeFailure_RetriesThenSucceeds()
	{
		var view = new FakePairingView(new string?[] { "bad", "good" }, new[] { true });
		var calls = 0;
		Func<string, Task<SmartConnectPairingResult>> callback = code =>
		{
			calls++;
			return Task.FromResult(code == "good"
				? new SmartConnectPairingResult { Success = true }
				: new SmartConnectPairingResult { Success = false, ErrorMessage = "Invalid code" });
		};

		var result = await new PairingController().RunAsync(view, callback);

		Assert.True(result!.Success);
		Assert.Equal(2, calls);
		Assert.Single(view.Failures);
		Assert.Equal(ResultSeverity.Negative, view.Failures[0].severity);
		Assert.Equal("Invalid code", view.Failures[0].message);
	}

	// F5/F6: NotSent → amber, and the operator is told it is safe to try again (NOT the core's
	// financial-flavoured "do not blind-retry" message). Oracle is the design wording, not ex.Message.
	[Fact]
	public async Task TransportException_NotSent_RenderedAmber_SafeToRetryWording()
	{
		var view = new FakePairingView(new string?[] { "1234" }, new[] { false });
		Func<string, Task<SmartConnectPairingResult>> callback = code =>
			throw new SmartConnectTransportException(SmartConnectRequestDelivery.NotSent, new Exception("boom"));

		var result = await new PairingController().RunAsync(view, callback);

		Assert.Null(result);
		Assert.Single(view.Failures);
		Assert.Equal(ResultSeverity.Ambiguous, view.Failures[0].severity);
		Assert.Contains("safe to try again", view.Failures[0].message);
		Assert.DoesNotContain("financial", view.Failures[0].message);
	}

	// F5/F6: Unknown → amber, and the operator is told it MAY have paired (distinct from NotSent).
	[Fact]
	public async Task TransportException_Unknown_RenderedAmber_MayHavePairedWording()
	{
		var view = new FakePairingView(new string?[] { "1234" }, new[] { false });
		Func<string, Task<SmartConnectPairingResult>> callback = code =>
			throw new SmartConnectTransportException(SmartConnectRequestDelivery.Unknown, new Exception("boom"));

		var result = await new PairingController().RunAsync(view, callback);

		Assert.Null(result);
		Assert.Single(view.Failures);
		Assert.Equal(ResultSeverity.Ambiguous, view.Failures[0].severity);
		Assert.Contains("may have paired", view.Failures[0].message);
	}

	[Fact]
	public async Task TransportException_Retryable_ContinuesLoop()
	{
		var view = new FakePairingView(new string?[] { "1234", "1234" }, new[] { true });
		var attempt = 0;
		Func<string, Task<SmartConnectPairingResult>> callback = code =>
		{
			attempt++;
			if (attempt == 1)
			{
				throw new SmartConnectTransportException(SmartConnectRequestDelivery.NotSent, new Exception("boom"));
			}

			return Task.FromResult(new SmartConnectPairingResult { Success = true });
		};

		var result = await new PairingController().RunAsync(view, callback);

		Assert.True(result!.Success);   // retried after transport failure, then succeeded
		Assert.Equal(2, attempt);
	}

	[Fact]
	public async Task NonTransportException_Propagates()
	{
		var view = new FakePairingView(new string?[] { "1234" }, Array.Empty<bool>());
		Func<string, Task<SmartConnectPairingResult>> callback = code => throw new InvalidOperationException("bug");

		await Assert.ThrowsAsync<InvalidOperationException>(() => new PairingController().RunAsync(view, callback));
	}
}
