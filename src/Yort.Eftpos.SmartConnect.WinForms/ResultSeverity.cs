namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>The visual severity bucket for a rendered outcome — controls the colour cue. The exact
/// grouping is the contract (the design doc); concrete colours are chosen by the form.</summary>
internal enum ResultSeverity
{
	/// <summary>A successful outcome (Accepted / Succeeded). Rendered green.</summary>
	Success,

	/// <summary>An ambiguous outcome the caller must reconcile (Unknown). Rendered amber/prominent.</summary>
	Ambiguous,

	/// <summary>A negative or non-success outcome (Declined, Cancelled, Failed, DeviceOffline). Rendered red.</summary>
	Negative
}
