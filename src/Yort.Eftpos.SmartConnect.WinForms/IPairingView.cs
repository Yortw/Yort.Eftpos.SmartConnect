using System.Threading.Tasks;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>The view surface the <see cref="PairingController"/> drives. Implemented by the internal pairing form.</summary>
internal interface IPairingView
{
	/// <summary>Prompts for a pairing code; returns the entered code, or null if the operator cancelled.</summary>
	Task<string?> GetCodeAsync();

	/// <summary>Shows the busy state while a pairing attempt is in flight.</summary>
	void ShowBusy();

	/// <summary>Hides the busy state.</summary>
	void HideBusy();

	/// <summary>Shows a failure with its severity; returns true if the operator chose to retry, false to cancel.</summary>
	Task<bool> ShowFailureAsync(string message, ResultSeverity severity);

	/// <summary>Shows the success state and returns when the operator acknowledges it.</summary>
	Task ShowSuccessAsync(SmartConnectPairingResult result);
}
