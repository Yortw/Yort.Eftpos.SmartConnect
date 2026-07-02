using System.Drawing;
using Xunit;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

public class OwnerPlacementTests
{
	private static readonly Rectangle PrimaryWorkingArea = new Rectangle(0, 0, 1920, 1040);

	[Fact]
	public void CentredLocation_OwnerFullyOnScreen_CentresOnOwner()
	{
		var owner = new Rectangle(100, 100, 800, 600);
		var location = OwnerPlacement.CentredLocation(owner, new Size(400, 300), PrimaryWorkingArea);

		Assert.Equal(new Point(300, 250), location);
	}

	[Fact]
	public void CentredLocation_SecondMonitor_CentresWithinItsCoordinateSpace()
	{
		// The whole point of owner-centring on a multi-monitor POS: the dialog lands on the OWNER's
		// monitor (working areas have non-zero origins there), not the primary screen.
		var secondMonitor = new Rectangle(1920, 0, 1920, 1040);
		var owner = new Rectangle(2200, 200, 800, 600);

		var location = OwnerPlacement.CentredLocation(owner, new Size(400, 300), secondMonitor);

		Assert.Equal(new Point(2400, 350), location);
	}

	[Fact]
	public void CentredLocation_OwnerNearTopLeftEdge_ClampsIntoWorkingArea()
	{
		// A small owner hugging the corner would centre the (larger) dialog partly off-screen.
		var owner = new Rectangle(10, 10, 200, 150);
		var location = OwnerPlacement.CentredLocation(owner, new Size(400, 300), PrimaryWorkingArea);

		Assert.Equal(new Point(0, 0), location);
	}

	[Fact]
	public void CentredLocation_OwnerNearBottomRightEdge_ClampsIntoWorkingArea()
	{
		var owner = new Rectangle(1800, 950, 200, 150);
		var location = OwnerPlacement.CentredLocation(owner, new Size(400, 300), PrimaryWorkingArea);

		// Fully visible: right edge at 1920, bottom edge at 1040.
		Assert.Equal(new Point(1520, 740), location);
	}

	[Fact]
	public void CentredLocation_OwnerStraddlingMonitors_StaysWithinTheGivenWorkingArea()
	{
		// The caller picks the working area (the monitor containing most of the owner); the math must
		// never place the dialog outside it even when the owner's centre lies beyond its edge.
		var secondMonitor = new Rectangle(1920, 0, 1920, 1040);
		var ownerStraddling = new Rectangle(1520, 200, 800, 600); // centre x = 1920, exactly on the seam

		var location = OwnerPlacement.CentredLocation(ownerStraddling, new Size(400, 300), secondMonitor);

		Assert.Equal(new Point(1920, 350), location);
	}

	[Fact]
	public void CentredLocation_DialogLargerThanWorkingArea_PinsToTopLeft()
	{
		// Oversized dialog: pin to the working area's top-left so the title bar and controls stay
		// reachable (clipping the far edge is the lesser evil).
		var owner = new Rectangle(100, 100, 800, 600);
		var smallArea = new Rectangle(0, 0, 640, 480);

		var location = OwnerPlacement.CentredLocation(owner, new Size(800, 600), smallArea);

		Assert.Equal(new Point(0, 0), location);
	}
}
