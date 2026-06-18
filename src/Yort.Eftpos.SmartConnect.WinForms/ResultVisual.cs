namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>A resolved outcome ready to render: a caption, its severity (colour cue), and optional
/// detail text (e.g. an operation failure message).</summary>
internal readonly struct ResultVisual
{
	/// <summary>Creates a resolved outcome visual.</summary>
	public ResultVisual(string caption, ResultSeverity severity, string? detail)
	{
		Caption = caption;
		Severity = severity;
		Detail = detail;
	}

	/// <summary>The primary caption (e.g. "Approved").</summary>
	public string Caption { get; }

	/// <summary>The severity bucket controlling the colour cue.</summary>
	public ResultSeverity Severity { get; }

	/// <summary>Optional secondary detail (e.g. an operation's error message); otherwise null.</summary>
	public string? Detail { get; }
}
