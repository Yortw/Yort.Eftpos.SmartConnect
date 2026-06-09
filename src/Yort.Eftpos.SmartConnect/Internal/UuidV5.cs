using System;
using System.Security.Cryptography;
using System.Text;

namespace Yort.Eftpos.SmartConnect.Internal;

/// <summary>
/// RFC 4122 version-5 (SHA-1, name-based) UUID generation. Internal — exposed publicly via
/// <see cref="SmartConnectRegisterId"/>.
/// </summary>
internal static class UuidV5
{
	/// <summary>
	/// Creates a deterministic version-5 UUID from a namespace and a name, per RFC 4122.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
	public static Guid Create(Guid namespaceId, string name)
	{
		if (name == null)
		{
			throw new ArgumentNullException(nameof(name));
		}

		// RFC 4122 hashes the namespace in big-endian order; .NET lays out the first three GUID fields
		// little-endian, so swap before hashing.
		byte[] namespaceBytes = namespaceId.ToByteArray();
		SwapToRfcByteOrder(namespaceBytes);

		byte[] nameBytes = Encoding.UTF8.GetBytes(name);

		byte[] combined = new byte[namespaceBytes.Length + nameBytes.Length];
		Buffer.BlockCopy(namespaceBytes, 0, combined, 0, namespaceBytes.Length);
		Buffer.BlockCopy(nameBytes, 0, combined, namespaceBytes.Length, nameBytes.Length);

		byte[] hash;
		using (var sha1 = SHA1.Create())
		{
			hash = sha1.ComputeHash(combined);
		}

		byte[] result = new byte[16];
		Buffer.BlockCopy(hash, 0, result, 0, 16);

		// Version 5 in the high nibble of byte 6; RFC 4122 variant (10xx) in the high bits of byte 8.
		result[6] = (byte)((result[6] & 0x0F) | 0x50);
		result[8] = (byte)((result[8] & 0x3F) | 0x80);

		// Swap back to .NET's layout (the swap is its own inverse).
		SwapToRfcByteOrder(result);
		return new Guid(result);
	}

	private static void SwapToRfcByteOrder(byte[] guidBytes)
	{
		SwapBytes(guidBytes, 0, 3);
		SwapBytes(guidBytes, 1, 2);
		SwapBytes(guidBytes, 4, 5);
		SwapBytes(guidBytes, 6, 7);
	}

	private static void SwapBytes(byte[] bytes, int left, int right)
	{
		byte temp = bytes[left];
		bytes[left] = bytes[right];
		bytes[right] = temp;
	}
}
