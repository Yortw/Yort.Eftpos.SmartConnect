namespace Yort.Eftpos.SmartConnect;

/// <summary>The outcome of a pairing attempt.</summary>
public sealed class SmartConnectPairingResult
{
	/// <summary><see langword="true"/> if pairing succeeded.</summary>
	public bool Success { get; set; }

	/// <summary>The error text returned by the service when <see cref="Success"/> is <see langword="false"/>; otherwise <see langword="null"/>.</summary>
	public string? ErrorMessage { get; set; }
}
