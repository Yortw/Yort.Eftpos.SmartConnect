using Xunit;

namespace Yort.Eftpos.SmartConnect.Tests;

public class SmartConnectTransactionStatusTests
{
	// Invariant guard (money safety): a default/zero-initialised status must never read as Accepted.
	[Fact]
	public void Default_IsUnknown_AndNeverAccepted()
	{
		Assert.Equal(SmartConnectTransactionStatus.Unknown, default(SmartConnectTransactionStatus));
		Assert.NotEqual(SmartConnectTransactionStatus.Accepted, default(SmartConnectTransactionStatus));
	}
}
