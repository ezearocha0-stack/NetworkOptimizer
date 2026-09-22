using System.IO;
using System.Security.Cryptography;

namespace NetworkOptimizer.Updates;

/// <summary>
/// Verificador criptográfico de integridad SHA-256 para paquetes de actualización.
/// </summary>
public static class UpdateHashVerifier
{
    /// <summary>
    /// Calcula el hash SHA-256 de un archivo en disco como cadena hexadecimal.
    /// </summary>
    public static string ComputeSha256(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Archivo a verificar no encontrado.", filePath);
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
        return ComputeSha256(stream);
    }

    /// <summary>
    /// Calcula el hash SHA-256 a partir de un flujo de datos (Stream).
    /// </summary>
    public static string ComputeSha256(Stream stream)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verifica que el hash SHA-256 del archivo coincida exactamente con el hash esperado del manifiesto.
    /// Comparación segura ordinal e insensible a mayúsculas/minúsculas.
    /// </summary>
    public static bool VerifySha256(string filePath, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash) || !File.Exists(filePath))
        {
            return false;
        }

        var normalizedExpected = expectedHash.Trim().Replace("-", string.Empty).ToLowerInvariant();
        if (normalizedExpected.Length != 64)
        {
            return false;
        }

        try
        {
            var computed = ComputeSha256(filePath);
            return string.Equals(computed, normalizedExpected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
