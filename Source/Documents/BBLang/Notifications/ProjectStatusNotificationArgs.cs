using System.Text.Json.Serialization;

namespace LanguageServer;

sealed class ProjectStatusNotificationArgs
{
    [JsonPropertyName("projectType")] public required string? ProjectType { get; set; }
    [JsonPropertyName("contextFile")] public required string ContextFile { get; set; }
    [JsonPropertyName("indexedFiles")] public int IndexedFiles { get; set; }
    [JsonPropertyName("root")] public string? Root { get; set; }
}
