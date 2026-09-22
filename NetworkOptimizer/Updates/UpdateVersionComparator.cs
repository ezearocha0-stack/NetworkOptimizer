namespace NetworkOptimizer.Updates;

/// <summary>
/// Comparador numérico y semántico de versiones para el sistema de autoactualización.
/// Utiliza System.Version con normalización simétrica de 4 componentes para evitar
/// anomalías en versiones de 2 o 3 partes.
/// </summary>
public static class UpdateVersionComparator
{
    /// <summary>
    /// Intenta analizar una cadena de versión normalizándola a System.Version.
    /// Soporta prefijos opcionales 'v' o 'V' y segmentos de 1 a 4 números.
    /// </summary>
    public static bool TryParseNormalized(string? versionString, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return false;
        }

        var clean = versionString.Trim().TrimStart('v', 'V');
        var parts = clean.Split('.');
        if (parts.Length == 0 || parts.Length > 4)
        {
            return false;
        }

        int major = 0, minor = 0, build = 0, rev = 0;

        if (parts.Length >= 1 && (!int.TryParse(parts[0], out major) || major < 0)) return false;
        if (parts.Length >= 2 && (!int.TryParse(parts[1], out minor) || minor < 0)) return false;
        if (parts.Length >= 3 && (!int.TryParse(parts[2], out build) || build < 0)) return false;
        if (parts.Length >= 4 && (!int.TryParse(parts[3], out rev) || rev < 0)) return false;

        version = new Version(major, minor, build, rev);
        return true;
    }

    /// <summary>
    /// Compara dos versiones. Devuelve > 0 si v1 > v2, 0 si v1 == v2, < 0 si v1 < v2.
    /// </summary>
    public static int CompareVersions(string? currentVersion, string? remoteVersion)
    {
        if (!TryParseNormalized(currentVersion, out var current) || current == null)
        {
            throw new ArgumentException($"Versión actual no válida: '{currentVersion}'", nameof(currentVersion));
        }

        if (!TryParseNormalized(remoteVersion, out var remote) || remote == null)
        {
            throw new ArgumentException($"Versión remota no válida: '{remoteVersion}'", nameof(remoteVersion));
        }

        return current.CompareTo(remote);
    }

    /// <summary>
    /// Determina si la versión remota es estrictamente superior a la versión actual.
    /// Si la versión remota es igual, inferior o inválida, devuelve false.
    /// </summary>
    public static bool IsNewerVersion(string? currentVersion, string? remoteVersion)
    {
        if (!TryParseNormalized(currentVersion, out var current) || current == null)
        {
            return false;
        }

        if (!TryParseNormalized(remoteVersion, out var remote) || remote == null)
        {
            return false;
        }

        return remote > current;
    }
}
