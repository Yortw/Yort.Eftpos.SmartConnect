using System;
using Xunit;
using Yort.Eftpos.SmartConnect.Internal;

namespace Yort.Eftpos.SmartConnect.Tests;

public class SmartConnectRegisterIdTests
{
	// Independent oracle: Python's uuid.uuid5(uuid.NAMESPACE_DNS, "www.example.org").
	[Fact]
	public void UuidV5_MatchesRfc4122Vector()
	{
		var dnsNamespace = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
		var result = UuidV5.Create(dnsNamespace, "www.example.org");
		Assert.Equal(new Guid("74738ff5-5367-5958-9aee-98fffdcd1876"), result);
	}

	[Fact]
	public void Generate_SameInputs_ProducesSameId()
	{
		var a = SmartConnectRegisterId.Generate("AcmeHeadOffice", "POS-7");
		var b = SmartConnectRegisterId.Generate("AcmeHeadOffice", "POS-7");
		Assert.Equal(a, b);
	}

	[Fact]
	public void Generate_DifferentRegister_ProducesDifferentId()
	{
		var a = SmartConnectRegisterId.Generate("AcmeHeadOffice", "POS-7");
		var b = SmartConnectRegisterId.Generate("AcmeHeadOffice", "POS-8");
		Assert.NotEqual(a, b);
	}

	[Fact]
	public void Generate_DifferentMerchant_ProducesDifferentId()
	{
		var a = SmartConnectRegisterId.Generate("AcmeHeadOffice", "POS-7");
		var b = SmartConnectRegisterId.Generate("OtherHeadOffice", "POS-7");
		Assert.NotEqual(a, b);
	}

	// Guards against a delimiter-collision: ("ab","c") must not equal ("a","bc").
	[Fact]
	public void Generate_ConcatenationIsUnambiguous()
	{
		var a = SmartConnectRegisterId.Generate("ab", "c");
		var b = SmartConnectRegisterId.Generate("a", "bc");
		Assert.NotEqual(a, b);
	}

	[Fact]
	public void Generate_IsVersion5AndRfc4122Variant()
	{
		var id = SmartConnectRegisterId.Generate("AcmeHeadOffice", "POS-7");
		// Format: xxxxxxxx-xxxx-Vxxx-Nxxx-xxxxxxxxxxxx  (V = version, N high bits = variant)
		Assert.Equal('5', id[14]);
		Assert.Contains(id[19], "89ab");
	}

	[Theory]
	[InlineData(null, "POS-7")]
	[InlineData("", "POS-7")]
	[InlineData("   ", "POS-7")]
	[InlineData("AcmeHeadOffice", null)]
	[InlineData("AcmeHeadOffice", "")]
	[InlineData("AcmeHeadOffice", "   ")]
	public void Generate_NullOrEmptyInputs_Throws(string? merchant, string? register)
	{
		Assert.Throws<ArgumentException>(() => SmartConnectRegisterId.Generate(merchant!, register!));
	}
}
