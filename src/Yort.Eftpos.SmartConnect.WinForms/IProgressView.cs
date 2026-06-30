using System;
using System.Threading.Tasks;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>The view surface the <see cref="ProgressController"/> drives. Implemented by the internal progress form</summary>
internal interface IProgressView
{
	/// <summary>Shows the busy state with the given caption (called once, on the first report).</summary>
	void ShowBusy(string caption);

	/// <summary>Updates the busy caption on a subsequent report.</summary>
	void UpdateCaption(string caption);

	/// <summary>Switches to the outcome state, returning when the operator acknowledges (OK) or the
	/// optional timeout elapses.</summary>
	Task ShowResultAsync(ResultVisual visual, TimeSpan? autoCloseAfter);
}
