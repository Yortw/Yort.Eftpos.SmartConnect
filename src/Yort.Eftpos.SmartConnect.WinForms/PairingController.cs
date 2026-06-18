using System;
using System.Threading.Tasks;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Drives the pairing interaction loop — prompt, attempt, present, retry-or-cancel — over an
/// <see cref="IPairingView"/>, invoking a caller-supplied callback to perform each attempt. UI-free.
/// Catches <see cref="SmartConnectTransportException"/> (the only failure the core's PairAsync throws)
/// and renders it as a retryable, ambiguous failure; other exceptions propagate.</summary>
internal sealed class PairingController
{
	/// <summary>Runs the loop. Returns the successful result, or null if the operator cancelled.</summary>
	public async Task<SmartConnectPairingResult?> RunAsync(IPairingView view, Func<string, Task<SmartConnectPairingResult>> pairWithCode)
	{
		while (true)
		{
			var entered = await view.GetCodeAsync().ConfigureAwait(true);
			if (entered == null)
			{
				return null;
			}

			var code = entered.Trim();
			if (code.Length == 0)
			{
				// Never send a blank code to the callback (it would trigger the core's ArgumentException).
				continue;
			}

			SmartConnectPairingResult result;
			try
			{
				view.ShowBusy();
				result = await pairWithCode(code).ConfigureAwait(true);
			}
			catch (SmartConnectTransportException ex)
			{
				// F5/F6: the core's ex.Message is financial-transaction-flavoured ("Do not blind-retry a
				// financial operation"), which is wrong for pairing — re-pairing the SAME register is
				// harmless. Compose pairing-specific guidance keyed on Delivery instead. Both deliveries are
				// amber (ambiguous): neither is a clean decline, and Unknown means it MAY have paired.
				var message = ex.Delivery == SmartConnectRequestDelivery.NotSent
					? "Couldn't reach the service, so the terminal was not paired. It is safe to try again."
					: "Couldn't confirm the pairing — the terminal may have paired. Trying again with the same register is safe, or cancel and check the terminal.";
				if (await view.ShowFailureAsync(message, ResultSeverity.Ambiguous).ConfigureAwait(true))
				{
					continue;
				}

				return null;
			}
			finally
			{
				view.HideBusy();
			}

			if (result.Success)
			{
				await view.ShowSuccessAsync(result).ConfigureAwait(true);
				return result;
			}

			var failMessage = string.IsNullOrEmpty(result.ErrorMessage) ? "Pairing failed." : result.ErrorMessage!;
			if (await view.ShowFailureAsync(failMessage, ResultSeverity.Negative).ConfigureAwait(true))
			{
				continue;
			}

			return null;
		}
	}
}
