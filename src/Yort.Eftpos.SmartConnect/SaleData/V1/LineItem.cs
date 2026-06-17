using System.Collections.Generic;

namespace Yort.Eftpos.SmartConnect.SaleData.V1;

/// <summary>
/// A single line item within a <see cref="SaleData"/>. Monetary fields are vendor strings (encoding unspecified);
/// <c>Quantity</c> is a string as the schema declares (never coerce to int/decimal).
/// </summary>
public sealed class LineItem
{
	/// <summary>Unique line id within the sale.</summary>
	public string? LineId { get; set; }

	/// <summary>Position within the sale.</summary>
	public string? SequenceNumber { get; set; }

	/// <summary>Unique product id.</summary>
	public string? ProductId { get; set; }

	/// <summary>Product name. Required by the wire schema.</summary>
	public string? ProductName { get; set; }

	/// <summary>Product description.</summary>
	public string? ProductDescription { get; set; }

	/// <summary>Category hierarchy. Null (the default) omits the field.</summary>
	public IList<Category>? Categories { get; set; }

	/// <summary>Unique brand id.</summary>
	public string? BrandId { get; set; }

	/// <summary>Brand name.</summary>
	public string? BrandName { get; set; }

	/// <summary>Quantity. Required. Vendor string (not numeric in the schema).</summary>
	public string? Quantity { get; set; }

	/// <summary>Per-unit price, tax included. Required. Vendor string.</summary>
	public string? UnitPrice { get; set; }

	/// <summary>Per-unit tax. Required. Vendor string.</summary>
	public string? UnitTax { get; set; }

	/// <summary>Per-unit discount. Vendor string.</summary>
	public string? UnitDiscount { get; set; }

	/// <summary>Line total, tax included; negative for returns. Required. Vendor string.</summary>
	public string? TotalPrice { get; set; }

	/// <summary>Line tax total. Required. Vendor string.</summary>
	public string? TotalTax { get; set; }

	/// <summary>Line discount total. Vendor string.</summary>
	public string? TotalDiscount { get; set; }

	/// <summary>References a parent <c>LineId</c>/<c>SequenceNumber</c> when this is a modifier.</summary>
	public string? ModifierFor { get; set; }

	/// <summary>Product SKU.</summary>
	public string? SkuCode { get; set; }

	/// <summary>UPC (GS1) or EAN barcode.</summary>
	public string? Barcode { get; set; }
}
