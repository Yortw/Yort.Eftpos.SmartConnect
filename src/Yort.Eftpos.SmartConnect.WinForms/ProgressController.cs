using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Translates progress reports into view calls: shows the dialog (once) on the first report
/// and updates the caption thereafter. UI-free; the view is an abstraction.</summary>
internal sealed class ProgressController
{
	private readonly IProgressView _view;
	private readonly IReadOnlyDictionary<SmartConnectPollingState, string> _stateCaptions;
	private readonly Action _onFirstShow;
	private bool _shown;

	/// <summary>Creates the controller.</summary>
	/// <param name="view">The view to drive.</param>
	/// <param name="stateCaptions">Per-state default captions.</param>
	/// <param name="onFirstShow">Invoked once, immediately before the dialog is first shown (used to
	/// disable the owner window).</param>
	public ProgressController(IProgressView view, IReadOnlyDictionary<SmartConnectPollingState, string> stateCaptions, Action onFirstShow)
	{
		_view = view;
		_stateCaptions = stateCaptions;
		_onFirstShow = onFirstShow;
	}

	/// <summary>Handles a progress report. Marshalling to the UI thread is the caller's responsibility
	/// (the wrapper passes this to a <see cref="System.Progress{T}"/> created on the UI thread).</summary>
	public void Report(SmartConnectPollingStatus status)
	{
		var caption = CaptionResolver.Resolve(status, _stateCaptions);
		if (!_shown)
		{
			_shown = true;
			_onFirstShow();
			_view.ShowBusy(caption);
		}
		else
		{
			_view.UpdateCaption(caption);
		}
	}

	/// <summary>Shows the outcome screen via the view.</summary>
	public Task ShowResultAsync(ResultVisual visual, TimeSpan? autoCloseAfter)
	{
		return _view.ShowResultAsync(visual, autoCloseAfter);
	}
}
