using Xunit;

namespace Yort.Eftpos.SmartConnect.Tests;

/// <summary>
/// Pins the environment base URLs. These are wire contract — the class's own remark calls shipping against
/// the wrong environment "a common and costly mistake" — so an accidental edit (a typo, a copy/paste, a
/// dev-to-prod slip) must fail a test rather than reach a terminal. This is a regression tripwire against
/// accidental change, not vendor validation; the authoritative source is https://smartconnectdev.shift4.co.nz.
/// </summary>
public class SmartConnectEnvironmentsTests
{
	[Fact]
	public void Production_IsTheExpectedAbsoluteUrl()
	{
		Assert.Equal("https://api.smart-connect.cloud/POS", SmartConnectEnvironments.Production.AbsoluteUri);
	}

	[Fact]
	public void Development_IsTheExpectedAbsoluteUrl()
	{
		Assert.Equal("https://api-dev.smart-connect.cloud/POS", SmartConnectEnvironments.Development.AbsoluteUri);
	}

	[Fact]
	public void ProductionAndDevelopment_AreDistinct()
	{
		Assert.NotEqual(SmartConnectEnvironments.Production, SmartConnectEnvironments.Development);
	}
}
