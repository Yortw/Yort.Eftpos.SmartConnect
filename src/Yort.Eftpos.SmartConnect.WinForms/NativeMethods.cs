using System;
using System.Runtime.InteropServices;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Native interop. Used to enable/disable an owner window for modal-like behaviour without a blocking <c>ShowDialog</c>.</summary>
internal static class NativeMethods
{
	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

	/// <summary>Enables or disables mouse and keyboard input to the given window.</summary>
	public static void SetWindowEnabled(IntPtr handle, bool enabled)
	{
		EnableWindow(handle, enabled);
	}
}
