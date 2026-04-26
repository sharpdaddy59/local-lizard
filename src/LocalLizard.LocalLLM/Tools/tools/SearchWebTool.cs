using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalLizard.LocalLLM.Tools.Tools;

/// <summary>
/// Search the web using Brave Search API.
/// Disabled if no API key is configured.
/// </summary>
public sealed class SearchWebTool : ITool
{
    private readonly string? _apiKey;
    private readonly HttpClient _http;

    public string Name => "search_web";

    public string Description =>
        "Search the web for current information. Argument: q (search query). " +
        "Example: q=weather in Dallas Texas";

    public bool IsEnabled => !string.IsNullOrEmpty(_apiKey);
    public bool IsDisabled => !IsEnabled;

    public SearchWebTool(string? apiKey) : this(apiKey, new HttpClient()) { }

    public SearchWebTool(string? apiKey, HttpClient http)
    {
        _apiKey = apiKey;
        _http = http;
        _http.BaseAddress = new Uri("https://api.search.brave.com/res/v1/");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        if (!string.IsNullOrEmpty(apiKey))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Subscription-Token", apiKey);
    }

    public async Task<string> RunAsync(JsonElement arguments, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return "Search is not configured. Tell the user to set up a Brave Search API key.";

        // Parse query from arguments JSON
        string? query = null;
        if (arguments.TryGetProperty("q", out var qEl))
            query = qEl.GetString();

        if (string.IsNullOrWhiteSpace(query))
            return "Error: search_web requires a q argument with the search query.";

        return await SearchAsync(query, ct);
    }

    private async Task<string> SearchAsync(string query, CancellationToken ct)
    {
        try
        {
            var url = $"web/search?q={Uri.EscapeDataString(query)}&count=5";
            var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<BraveSearchResponse>(ct);
            if (result?.Web?.Results is null || result.Web.Results.Count == 0)
                return "No results found.";

            var sb = new System.Text.StringBuilder();
            foreach (var r in result.Web.Results)
            {
                sb.AppendLine($"- {r.Title}");
                sb.AppendLine($"  {r.Description}");
                sb.AppendLine($"  URL: {r.Url}");
                sb.AppendLine();
            }

            return ToolCallParser.TruncateResult(sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return $"Search failed: {ex.Message}";
        }
    }

    // ---- Brave Search API response types ----

    private sealed class BraveSearchResponse
    {
        [JsonPropertyName("web")]
        public WebResults? Web { get; set; }
    }

    private sealed class WebResults
    {
        [JsonPropertyName("results")]
        public List<SearchResult>? Results { get; set; }
    }

    private sealed class SearchResult
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }
}
