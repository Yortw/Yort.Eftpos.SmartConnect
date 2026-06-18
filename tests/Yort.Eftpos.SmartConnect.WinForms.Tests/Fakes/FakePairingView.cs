using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Yort.Eftpos.SmartConnect;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests.Fakes;

/// <summary>Scripts a sequence of operator interactions so the controller's loop can be driven
/// deterministically. Each GetCodeAsync call dequeues the next scripted code (null = cancel); each
/// ShowFailureAsync dequeues the next retry decision (true = retry).</summary>
internal sealed class FakePairingView : IPairingView
{
	private readonly Queue<string?> _codes;
	private readonly Queue<bool> _retryDecisions;

	public FakePairingView(IEnumerable<string?> codes, IEnumerable<bool> retryDecisions)
	{
		_codes = new Queue<string?>(codes);
		_retryDecisions = new Queue<bool>(retryDecisions);
	}

	public int BusyShownCount { get; private set; }
	public List<(string message, ResultSeverity severity)> Failures { get; } = new();
	public SmartConnectPairingResult? SuccessShown { get; private set; }

	public Task<string?> GetCodeAsync()
	{
		return Task.FromResult(_codes.Count > 0 ? _codes.Dequeue() : null);
	}

	public void ShowBusy()
	{
		BusyShownCount++;
	}

	public void HideBusy()
	{
	}

	public Task<bool> ShowFailureAsync(string message, ResultSeverity severity)
	{
		Failures.Add((message, severity));
		return Task.FromResult(_retryDecisions.Count > 0 && _retryDecisions.Dequeue());
	}

	public Task ShowSuccessAsync(SmartConnectPairingResult result)
	{
		SuccessShown = result;
		return Task.CompletedTask;
	}
}
