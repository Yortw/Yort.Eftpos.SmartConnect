using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using Yort.Eftpos.SmartConnect.Internal;
using V1 = Yort.Eftpos.SmartConnect.SaleData.V1;

namespace Yort.Eftpos.SmartConnect.Tests;

public class SaleDataSerializerTests
{
	private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;

	[Fact]
	public void Serialize_PutsVersionAtRoot_AndDataUnderSaleData()
	{
		var json = SaleDataSerializer.Serialize(new V1.SaleData { TotalAmount = "19.99", TotalTax = "2.61" });
		var root = Root(json);

		Assert.Equal("1.0.0", root.GetProperty("version").GetString());
		var sale = root.GetProperty("saleData");
		Assert.Equal("19.99", sale.GetProperty("totalAmount").GetString());
		Assert.Equal("2.61", sale.GetProperty("totalTax").GetString());
	}

	[Fact]
	public void Serialize_CapturesRuntimeType_ThroughBaseTypedReference()
	{
		// The polymorphism trap: a base-typed reference must still serialise the V1 properties.
		SmartConnectSaleData baseTyped = new V1.SaleData { TotalAmount = "5.00", TotalTax = "0.65" };
		var sale = Root(SaleDataSerializer.Serialize(baseTyped)).GetProperty("saleData");

		Assert.Equal("5.00", sale.GetProperty("totalAmount").GetString());
	}

	[Fact]
	public void Serialize_NestedBody_DoesNotContainVersion()
	{
		var sale = Root(SaleDataSerializer.Serialize(new V1.SaleData { TotalAmount = "1.00", TotalTax = "0" })).GetProperty("saleData");
		Assert.False(sale.TryGetProperty("version", out _));
	}

	[Fact]
	public void Serialize_OmitsNullMembersAndEmptyCollections()
	{
		var sale = Root(SaleDataSerializer.Serialize(new V1.SaleData { TotalAmount = "1.00", TotalTax = "0" })).GetProperty("saleData");
		Assert.False(sale.TryGetProperty("saleId", out _));     // null -> omitted
		Assert.False(sale.TryGetProperty("lineItems", out _));  // null collection -> omitted
	}

	[Fact]
	public void Serialize_LineItemsAndCategories_SerialiseCamelCaseNested()
	{
		var data = new V1.SaleData
		{
			TotalAmount = "10.00",
			TotalTax = "1.30",
			LineItems = new List<V1.LineItem>
			{
				new V1.LineItem
				{
					ProductName = "Widget",
					Quantity = "2",
					UnitPrice = "5.00",
					UnitTax = "0.65",
					TotalPrice = "10.00",
					TotalTax = "1.30",
					Categories = new List<V1.Category>
					{
						new V1.Category { CategoryName = "Hardware" }
					}
				}
			}
		};

		var item = Root(SaleDataSerializer.Serialize(data)).GetProperty("saleData").GetProperty("lineItems")[0];
		Assert.Equal("Widget", item.GetProperty("productName").GetString());
		Assert.Equal("2", item.GetProperty("quantity").GetString());
		Assert.Equal("Hardware", item.GetProperty("categories")[0].GetProperty("categoryName").GetString());
	}

	[Fact]
	public void Serialize_DateTimeOffset_IsIso8601()
	{
		var data = new V1.SaleData
		{
			TotalAmount = "1.00",
			TotalTax = "0",
			CreatedAt = new DateTimeOffset(2026, 6, 17, 8, 30, 0, TimeSpan.Zero)
		};
		var created = Root(SaleDataSerializer.Serialize(data)).GetProperty("saleData").GetProperty("createdAt").GetString();
		Assert.StartsWith("2026-06-17T08:30:00", created);
	}

	[Fact]
	public void Serialize_HostileStringContent_RoundTripsIntact()
	{
		// Wire-correctness: JSON-escaping must preserve quotes/backslash/control char/unicode byte-for-byte.
		const string hostile = "a&b=c\"\\\né😀";
		var json = SaleDataSerializer.Serialize(new V1.SaleData { TotalAmount = "1", TotalTax = "0", CustomerName = hostile });
		Assert.Equal(hostile, Root(json).GetProperty("saleData").GetProperty("customerName").GetString());
	}

	[Fact]
	public void Serialize_ThirdPartyDerivedType_SerialisesItsOwnPropertiesAndVersion()
	{
		var json = SaleDataSerializer.Serialize(new CustomSaleData());
		var root = Root(json);
		Assert.Equal("9.9.9", root.GetProperty("version").GetString());
		Assert.Equal("custom!", root.GetProperty("saleData").GetProperty("note").GetString());
	}

	private sealed class CustomSaleData : SmartConnectSaleData
	{
		public override string Version => "9.9.9";
		public string Note { get; set; } = "custom!";
	}
}
