using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>A reusable WinForms dialog that shows progress while a SmartConnect operation runs and
/// optionally presents its outcome. Construct it on the UI thread (it captures the current
/// <see cref="System.Threading.SynchronizationContext"/> for progress marshalling). Pass
/// <see cref="Progress"/> into a client call; the dialog auto-shows on the first report and closes on
/// <see cref="Dispose"/>. Call a <c>ShowResultAsync</c> overload to present the outcome, or omit it to
/// suppress the outcome screen.</summary>
/// <remarks>A <c>ShowResultAsync</c> call returns when the operator acknowledges the outcome (or, for the
/// timeout overloads, when the delay elapses); the dialog <em>window</em> is dismissed on <see cref="Dispose"/>
/// — the usual pattern is a <c>using</c> around the call. Calling <c>ShowResultAsync</c> again before a prior
/// call has returned pre-empts the earlier call: its awaiter completes without operator acknowledgement.</remarks>
public sealed class SmartConnectProgressDialog : IDisposable
{
	private readonly ProgressForm _form;
	private readonly DialogChrome _chrome = new DialogChrome();
	private readonly OwnerController _owner;
	private readonly ProgressController _controller;
	private readonly IProgress<SmartConnectPollingStatus> _progress;
	private readonly IDictionary<SmartConnectPollingState, string> _stateCaptions = DefaultCaptions.CreateStateCaptions();
	private bool _appearanceApplied;

	/// <summary>Creates an owner-less dialog (centres on screen).</summary>
	public SmartConnectProgressDialog()
		: this(null)
	{
	}

	/// <summary>Creates a dialog owned by <paramref name="owner"/> (centres on it; disables it while busy).</summary>
	public SmartConnectProgressDialog(IWin32Window? owner)
	{
		_form = new ProgressForm();
		// The flag is passed as a delegate, not a value: DisableOwnerWhileBusy is a settable property, so
		// it must be read when the disable happens, or setting it after construction is silently ignored.
		_owner = new OwnerController(owner, () => _chrome.DisableOwnerWhileBusy, NativeMethods.SetWindowEnabled);
		// Owner-centring at Load (first show): that is when the form's size is final. No usable owner →
		// the form's CenterScreen default stands.
		_form.Load += (_, _) => OwnerPlacement.TryApply(_form, owner);
		_controller = new ProgressController(_form, (IReadOnlyDictionary<SmartConnectPollingState, string>)_stateCaptions, OnFirstShow);
		_progress = new Progress<SmartConnectPollingStatus>(_controller.Report);
		TransactionResultCaptions = DefaultCaptions.CreateTransactionResultCaptions();
		OperationResultCaptions = DefaultCaptions.CreateOperationResultCaptions();
	}

	/// <summary>The progress sink to pass into a client operation.</summary>
	public IProgress<SmartConnectPollingStatus> Progress => _progress;

	/// <summary>The window title (default "EFTPOS").</summary>
	public string WindowTitle { get => _chrome.WindowTitle; set => _chrome.WindowTitle = value; }

	/// <summary>An optional logo image.</summary>
	public Image? Logo { get => _chrome.Logo; set => _chrome.Logo = value; }

	/// <summary>The background colour.</summary>
	public Color BackgroundColour { get => _chrome.BackgroundColour; set => _chrome.BackgroundColour = value; }

	/// <summary>The foreground (text) colour.</summary>
	public Color ForegroundColour { get => _chrome.ForegroundColour; set => _chrome.ForegroundColour = value; }

	/// <summary>The dialog font.</summary>
	public Font? Font { get => _chrome.Font; set => _chrome.Font = value; }

	/// <summary>Whether to disable the owner window while busy (default true).</summary>
	public bool DisableOwnerWhileBusy { get => _chrome.DisableOwnerWhileBusy; set => _chrome.DisableOwnerWhileBusy = value; }

	/// <summary>Overridable progress captions per polling state (pre-populated with defaults).</summary>
	public IDictionary<SmartConnectPollingState, string> StateCaptions => _stateCaptions;

	/// <summary>Overridable outcome captions per transaction status (pre-populated with defaults).</summary>
	public IDictionary<SmartConnectTransactionStatus, string> TransactionResultCaptions { get; }

	/// <summary>Overridable outcome captions per operation status (pre-populated with defaults).</summary>
	public IDictionary<SmartConnectOperationStatus, string> OperationResultCaptions { get; }

	/// <summary>Shows the outcome of a financial transaction and returns when the operator acknowledges it.</summary>
	public Task ShowResultAsync(SmartConnectTransactionResult result)
	{
		return ShowResultAsync(result, autoCloseAfter: null);
	}

	/// <summary>Shows the outcome of a financial transaction; the call returns after the given delay if the
	/// operator does not acknowledge it first (the dialog window is dismissed on <see cref="Dispose"/>).</summary>
	public Task ShowResultAsync(SmartConnectTransactionResult result, TimeSpan autoCloseAfter)
	{
		return ShowResultAsync(result, (TimeSpan?)autoCloseAfter);
	}

	/// <summary>Shows the outcome of a non-financial operation and returns when the operator acknowledges it.</summary>
	public Task ShowResultAsync(SmartConnectOperationResult result)
	{
		return ShowResultAsync(result, autoCloseAfter: null);
	}

	/// <summary>Shows the outcome of a non-financial operation; the call returns after the given delay if the
	/// operator does not acknowledge it first (the dialog window is dismissed on <see cref="Dispose"/>).</summary>
	public Task ShowResultAsync(SmartConnectOperationResult result, TimeSpan autoCloseAfter)
	{
		return ShowResultAsync(result, (TimeSpan?)autoCloseAfter);
	}

	/// <summary>Closes the dialog and re-enables the owner window.</summary>
	public void Dispose()
	{
		_owner.Restore();
		_form.Dispose();
	}

	private Task ShowResultAsync(SmartConnectTransactionResult result, TimeSpan? autoCloseAfter)
	{
		if (result == null)
		{
			throw new ArgumentNullException(nameof(result));
		}

		EnsureAppearanceAndOwner();
		var visual = ResultVisuals.ForTransaction(result.Status, (IReadOnlyDictionary<SmartConnectTransactionStatus, string>)TransactionResultCaptions);
		return _controller.ShowResultAsync(visual, autoCloseAfter);
	}

	private Task ShowResultAsync(SmartConnectOperationResult result, TimeSpan? autoCloseAfter)
	{
		if (result == null)
		{
			throw new ArgumentNullException(nameof(result));
		}

		EnsureAppearanceAndOwner();
		var visual = ResultVisuals.ForOperation(result.Status, result.ErrorMessage, (IReadOnlyDictionary<SmartConnectOperationStatus, string>)OperationResultCaptions);
		return _controller.ShowResultAsync(visual, autoCloseAfter);
	}

	private void OnFirstShow()
	{
		EnsureAppearanceAndOwner();
	}

	private void EnsureAppearanceAndOwner()
	{
		// Progress reports arrive via Progress<T>-posted callbacks, so one queued behind Dispose() runs
		// AFTER it. Without this guard a first-report-after-dispose would disable the owner after
		// Dispose already ran its Restore() — leaving the owner window disabled with nothing left to
		// re-enable it.
		if (_appearanceApplied || _form.IsDisposed)
		{
			return;
		}

		_appearanceApplied = true;
		_chrome.ApplyTo(_form, _form.LogoBox);
		_owner.Disable();
	}
}
