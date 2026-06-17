using System.Text.Json.Serialization;
using Xunit;
using V1 = Yort.Eftpos.SmartConnect.SaleData.V1;

namespace Yort.Eftpos.SmartConnect.Tests;

public class SaleDataModelTests
{
	[Fact]
	public void V1SaleData_VersionIs100_AndIsASmartConnectSaleData()
	{
		var sale = new V1.SaleData { TotalAmount = "19.99", TotalTax = "2.61" };

		Assert.Equal("1.0.0", sale.Version);
		Assert.IsAssignableFrom<SmartConnectSaleData>(sale);
		Assert.Null(sale.LineItems); // null by default -> omitted from the wire when unset
	}

	[Fact]
	public void Version_IsJsonIgnored_OnTheBase()
	{
		// Pins that Version is excluded from the nested saleData body (it is composed at the envelope root).
		var prop = typeof(SmartConnectSaleData).GetProperty(nameof(SmartConnectSaleData.Version));
		Assert.NotEmpty(prop!.GetCustomAttributes(typeof(JsonIgnoreAttribute), true));
	}
}
