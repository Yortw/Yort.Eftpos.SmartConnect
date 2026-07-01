using System;
using System.Collections.Generic;
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
		var controller = new OwnerController(owner, disableWhileBusy: true, (h, e) => calls.Add((h, e)));

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
	public void Disable_NullOwner_DoesNothingAndDoesNotThrow()
	{
		var calls = new List<(IntPtr handle, bool enabled)>();
		var controller = new OwnerController(owner: null, disableWhileBusy: true, (h, e) => calls.Add((h, e)));

		controller.Disable();
		controller.Restore();

		Assert.Empty(calls);
	}

	[Fact]
	public void Disable_WhenEnabled_DisablesThenRestoreReenables()
	{
		var calls = new List<(IntPtr handle, bool enabled)>();
		var controller = new OwnerController(new FakeWindow(), disableWhileBusy: true, (h, e) => calls.Add((h, e)));

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
		var controller = new OwnerController(new FakeWindow(), disableWhileBusy: false, (h, e) => calls.Add((h, e)));

		controller.Disable();
		controller.Restore();

		Assert.Empty(calls);
	}

	[Fact]
	public void Restore_WithoutDisable_DoesNothing()
	{
		var calls = new List<(IntPtr handle, bool enabled)>();
		var controller = new OwnerController(new FakeWindow(), disableWhileBusy: true, (h, e) => calls.Add((h, e)));

		controller.Restore();

		Assert.Empty(calls);
	}
}
