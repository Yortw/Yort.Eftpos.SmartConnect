using Xunit;

namespace Yort.Eftpos.SmartConnect.Tests;

public class AmountConversionTests
{
	[Fact]
	public void Request_SettingAmountTotalDecimal_StoresWholeCents()
	{
		var request = new SmartConnectTransactionRequest { AmountTotal = 12.34m };
		Assert.Equal(1234, request.AmountTotalCents);
	}

	[Fact]
	public void Request_GettingAmountTotal_ReturnsDollarsFromCents()
	{
		var request = new SmartConnectTransactionRequest { AmountTotalCents = 750 };
		Assert.Equal(7.50m, request.AmountTotal);
	}

	[Fact]
	public void Request_SettingAmountTotalDecimal_RoundsHalfAwayFromZero()
	{
		// 2.005 * 100 = 200.5 -> away-from-zero -> 201 (banker's rounding would give 200; assert the financial choice).
		var request = new SmartConnectTransactionRequest { AmountTotal = 2.005m };
		Assert.Equal(201, request.AmountTotalCents);
	}

	[Fact]
	public void Request_SettingAmountCashDecimal_StoresWholeCents()
	{
		var request = new SmartConnectTransactionRequest { AmountCash = 5.00m };
		Assert.Equal(500, request.AmountCashCents);
	}

	[Fact]
	public void Result_GettingAmountTotal_ReturnsDollarsFromCents()
	{
		var result = new SmartConnectTransactionResult { AmountTotalCents = 1000 };
		Assert.Equal(10.00m, result.AmountTotal);
	}

	[Fact]
	public void Result_GettingAmountTip_ReturnsDollarsFromCents()
	{
		var result = new SmartConnectTransactionResult { AmountTipCents = 250 };
		Assert.Equal(2.50m, result.AmountTip);
	}
}
