using System.Reflection;
using System.Text;

namespace HaPcRemote.Service.Views;

public static class EmbeddedResourceHelper
{
    private static readonly Assembly CurrentAssembly = typeof(EmbeddedResourceHelper).Assembly;
    private static readonly Dictionary<string, string> ResourceCache = new();

    /// <summary>
    /// Loads an embedded resource file from the Views folder.
    /// </summary>
    /// <param name="fileName">The filename (e.g., "artwork-detail.html")</param>
    /// <returns>The file contents as a string</returns>
    /// <exception cref="FileNotFoundException">If the resource is not found</exception>
    public static string LoadResource(string fileName)
    {
        var cacheKey = $"Views/{fileName}";

        if (ResourceCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var resourceName = $"HaPcRemote.Service.Views.{fileName}";
        using var stream = CurrentAssembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource not found: {resourceName}");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var content = reader.ReadToEnd();
        ResourceCache[cacheKey] = content;
        return content;
    }

    /// <summary>
    /// Loads a template file and replaces placeholders.
    /// </summary>
    /// <param name="fileName">The filename (e.g., "artwork-detail.html")</param>
    /// <param name="replacements">Dictionary of placeholder-value pairs (e.g., "{{GAME_NAME}}" -> "Portal 2")</param>
    /// <returns>The template with replacements applied</returns>
    public static string LoadTemplate(string fileName, Dictionary<string, string> replacements)
    {
        var content = LoadResource(fileName);

        if (replacements == null || replacements.Count == 0)
            return content;

        var result = new StringBuilder(content);
        foreach (var kvp in replacements)
            result.Replace(kvp.Key, kvp.Value);

        return result.ToString();
    }
}
