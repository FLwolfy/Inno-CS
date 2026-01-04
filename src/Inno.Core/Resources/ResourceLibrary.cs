using System.Collections.Concurrent;
using System.Reflection;

namespace Inno.Core.Resources;

public static class ResourceLibrary
{
    private static readonly ConcurrentDictionary<(Assembly, string), byte[]> CACHE = new();

    /// <summary>
    /// Load embedded resource as raw bytes.
    /// nameOrSuffix can be full manifest name, or a suffix matched by EndsWith.
    /// </summary>
    public static ResourceBin LoadEmbedded(
        string nameOrSuffix,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase,
        bool endsWithMatch = true,
        bool useCache = true
    ) {
        if (string.IsNullOrWhiteSpace(nameOrSuffix))
            throw new ArgumentException("Resource name must not be null/empty.", nameof(nameOrSuffix));

        var asm = Assembly.GetCallingAssembly();
        
        if (useCache && CACHE.TryGetValue((asm, nameOrSuffix), out var cached))
            return new ResourceBin(nameOrSuffix, cached);

        var manifestName = ResolveManifestName(asm, nameOrSuffix, comparison, endsWithMatch);

        using var s = asm.GetManifestResourceStream(manifestName)
                      ?? throw new FileNotFoundException($"Embedded resource stream '{manifestName}' not found in assembly '{asm.FullName}'.");

        using var ms = new MemoryStream();
        s.CopyTo(ms);

        var bytes = ms.ToArray();
        if (useCache) CACHE[(asm, nameOrSuffix)] = bytes;
        return new ResourceBin(nameOrSuffix, bytes);
    }

    /// <summary>
    /// Decode loaded binaries/bytes into specific type.
    /// </summary>
    /// <typeparam name="T">the given type to be decoded into</typeparam>
    public static T DecodeBinaries<T>(ResourceBin binaries) where T : notnull
    {
        return ResourceDecoderRegistry.Decode<T>(binaries.sourceBytes, binaries.sourceName);
    }
    
    private static string ResolveManifestName(Assembly asm, string nameOrSuffix, StringComparison comparison, bool endsWithMatch)
    {
        var names = asm.GetManifestResourceNames();

        if (!endsWithMatch)
        {
            var exact = names.FirstOrDefault(n => string.Equals(n, nameOrSuffix, comparison));
            if (exact == null)
                throw new FileNotFoundException($"Embedded resource '{nameOrSuffix}' not found in assembly '{asm.FullName}'.");
            return exact;
        }

        var matches = names.Where(n => n.EndsWith(nameOrSuffix, comparison)).ToArray();
        if (matches.Length == 0)
            throw new FileNotFoundException($"Embedded resource '{nameOrSuffix}' not found in assembly '{asm.FullName}'.");

        if (matches.Length == 1)
            return matches[0];

        throw new AmbiguousMatchException(
            $"Embedded resource suffix '{nameOrSuffix}' is ambiguous. Matches: {string.Join(", ", matches)}");
    }
    
    public static void ClearCache() => CACHE.Clear();

}