using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Xunit;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

public class OwnerControllerTests
{
	private sealed class FakeWindow : IWin32Window
	{
		public IntPtr Handle => new IntPtr(42);
	}

	// Models an owner that is alive when Disable() runs but disposed before Restore() — accessing Handle on a
	// disposed WinForms Control throws ObjectDisposedException, which is the real-world shutdown race.
	private sealed class DisposableFakeWindow : IWin32Window
	{
		public bool Disposed { get; set; }

		public IntPtr Handle => Disposed
			? throw new ObjectDisposedException(nameof(DisposableFakeWindow))
			: new IntPtr(42);
	}

	[Fact]
	public void Restore_OwnerDisposedWhileBusy_DoesNotThrowAndReleasesState()
	{
		var calls = new List<(IntPtr handle, bool enabled)>();
		var owner = new DisposableFakeWindow();
		var controller = new OwnerController(owner, disableWhileBusy: () => true, (h, e) => calls.Add((h, e)));

		controller.Disable();   // owner alive: disable recorded against handle 42
		owner.Disposed = true;  // owner torn down while the dialog was still busy

		controller.Restore();   // must NOT throw despite Handle now throwing ObjectDisposedException

		// The disable was recorded; Restore must not have recorded a re-enable (a disposed window is already
		// re-enabled by the OS), and must have cleared its state so a later Restore/Dispose is a silent no-op.
		Assert.Single(calls);
		Assert.Equal((new IntPtr(42), false), calls[0]);

		controller.Restore();
		Assert.Single(calls);
	}

	[Fact]
	public void TwoControllersSharingAnOwner_OwnerReenabledOnlyWhenTheLastReleases()
	{
		// Two dialogs disabling the same owner: EnableWindow is absolute, so without reference-counting the
		// first Restore() would re-enable the owner while the second dialog is still busy — defeating the
		// modal-like guarantee. The owner must be disabled once (by the first) and re-enabled once (by the
		// last), with the inner disable/restore recording no native call.
		var calls = new List<(IntPtr handle, bool enabled)>();
		var owner = new FakeWindow();
		var a = new OwnerController(owner, disableWhileBusy: () => true, (h, e) => calls.Add((h, e)));
		var b = new OwnerController(owner, disableWhileBusy: () => true, (h, e) => calls.Add((h, e)));

		a.Disable();            // first disabler: actually disables
		b.Disable();            // second disabler: no native call, just holds a reference
		Assert.Equal(new[] { (new IntPtr(42), false) }, calls);

		b.Restore();            // not the last: owner must STAY disabled
		Assert.Single(calls);   // still just the one disable — no premature re-enable

		a.Restore();            // last disabler releases: now re-enable
		Assert.Equal(2, calls.Count);
		Assert.Equal((new IntPtr(42), true), calls[1]);
	}

	[Fact]
	public void DistinctOwnersWithTheSameHandleValue_AreCountedIndependently()
	{
		// The depth is keyed on the owner OBJECT, not its HWND value (which Windows reuses). Two *different*
		// owner windows that happen to report the same handle must each be disabled and restored on their own
		// — never deduped by a shared handle value, or a leaked count from one owner could silently skip
		// disabling a later, unrelated owner mid-transaction. Both FakeWindows report handle 42.
		var calls = new List<(IntPtr handle, bool enabled)>();
		var a = new OwnerController(new FakeWindow(), disableWhileBusy: () => true, (h, e) => calls.Add((h, e)));
		var b = new OwnerController(new FakeWindow(), disableWhileBusy: () => true, (h, e) => calls.Add((h, e)));

		a.Disable();
		b.Disable();
		Assert.Equal(2, calls.Count(c => !c.enabled));   // both disabled, not deduped by handle value

		a.Restore();
		b.Restore();
		Assert.Equal(2, calls.Count(c => c.enabled));     // both re-enabled independently
	}

	[Fact]
	public void Disable_NullOwner_DoesNothingAndDoesNotThrow()
	{
		var calls = new List<(IntPtr handle, bool enabled)>();
		var controller = new OwnerController(owner: null, disableWhileBusy: () => true, (h, e) => calls.Add((h, e)));

		controller.Disable();
		controller.Restore();

		Assert.Empty(calls);
	}

	[Fact]
	public void Disable_WhenEnabled_DisablesThenRestoreReenables()
	{
		var calls = new List<(IntPtr handle, bool enabled)>();
		var controller = new OwnerController(new FakeWindow(), disableWhileBusy: () => true, (h, e) => calls.Add((h, e)));

		controller.Disable();
		controller.Restore();

		Assert.Equal(2, calls.Count);
		Assert.Equal((new IntPtr(42), false), calls[0]);
		Assert.Equal((new IntPtr(42), true), calls[1]);
	}

	[Fact]
	public void Disable_WhenDisableWhileBusyFalse_NeverTouchesOwner()
	{
		var calls = new List<(IntPtr handle, bool enabled)>();
		var controller = new OwnerController(new FakeWindow(), disableWhileBusy: () => false, (h, e) => calls.Add((h, e)));

		controller.Disable();
		controller.Restore();

		Assert.Empty(calls);
	}

	[Fact]
	public void Restore_WithoutDisable_DoesNothing()
	{
		var calls = new List<(IntPtr handle, bool enabled)>();
		var controller = new OwnerController(new FakeWindow(), disableWhileBusy: () => true, (h, e) => calls.Add((h, e)));

		controller.Restore();

		Assert.Empty(calls);
	}

	[Fact]
	public void Disable_ReadsDisableWhileBusyAtCallTime_NotConstructionTime()
	{
		// The public DisableOwnerWhileBusy property is settable AFTER the dialog constructs this
		// controller — a value captured at construction makes that setter silently dead (the getter
		// reports false while the owner still gets disabled).
		var calls = new List<(IntPtr handle, bool enabled)>();
		var disableWhileBusy = true;
		var controller = new OwnerController(new FakeWindow(), () => disableWhileBusy, (h, e) => calls.Add((h, e)));

		disableWhileBusy = false; // consumer sets dialog.DisableOwnerWhileBusy = false post-construction
		controller.Disable();
		controller.Restore();

		Assert.Empty(calls);
	}

	[Fact]
	public void Disable_OwnerAlreadyDisposed_DoesNotThrowAndDoesNotMarkDisabled()
	{
		// The owner can be disposed BEFORE the first show (operator closes the till window just as a
		// transaction starts). Disable() runs inside a Progress<T>-posted callback there, so an escaping
		// ObjectDisposedException is an unhandled UI-thread exception mid-transaction. Restore() was
		// already hardened for this; Disable() gets hit first.
		var calls = new List<(IntPtr handle, bool enabled)>();
		var owner = new DisposableFakeWindow { Disposed = true };
		var controller = new OwnerController(owner, disableWhileBusy: () => true, (h, e) => calls.Add((h, e)));

		controller.Disable();  // must not throw
		controller.Restore();  // nothing was disabled, so nothing to re-enable

		Assert.Empty(calls);
	}
}
