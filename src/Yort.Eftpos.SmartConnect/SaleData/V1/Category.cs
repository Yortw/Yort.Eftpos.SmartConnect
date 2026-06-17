namespace Yort.Eftpos.SmartConnect.SaleData.V1;

/// <summary>A category/department a <see cref="LineItem"/> belongs to.</summary>
public sealed class Category
{
	/// <summary>Unique category id.</summary>
	public string? CategoryId { get; set; }

	/// <summary>Category name. Required by the wire schema.</summary>
	public string? CategoryName { get; set; }

	/// <summary>Unique department id.</summary>
	public string? DepartmentId { get; set; }

	/// <summary>Department name.</summary>
	public string? DepartmentName { get; set; }
}
