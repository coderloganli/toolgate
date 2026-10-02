using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ToolGate.Core.Domain;

namespace ToolGate.Core.Invocation;

/// <summary>
/// Calls the tool's HTTP endpoint. GET and DELETE carry arguments as query parameters; other methods send them
/// as a JSON object body.
/// </summary>
public sealed class HttpToolInvoker(HttpClient httpClient) : IToolInvoker
{
    public async Task<InvocationResult> InvokeAsync(
        ToolDefinition tool,
        IReadOnlyDictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        var method = new HttpMethod(tool.HttpMethod.ToUpperInvariant());
        using var request = new HttpRequestMessage(method, BuildUri(tool.EndpointUrl, method, arguments));
        if (!CarriesQuery(method))
        {
            request.Content = JsonContent.Create(arguments);
        }

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new InvocationResult(response.IsSuccessStatusCode, (int)response.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            return new InvocationResult(false, null, $"Upstream request failed: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new InvocationResult(false, null, "Upstream request timed out.");
        }
    }

    public static Uri BuildUri(string endpointUrl, HttpMethod method, IReadOnlyDictionary<string, JsonElement> arguments)
    {
        if (!CarriesQuery(method) || arguments.Count == 0)
        {
            return new Uri(endpointUrl);
        }

        var query = new StringBuilder();
        foreach (var (name, value) in arguments)
        {
            if (query.Length > 0)
            {
                query.Append('&');
            }

            var text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
            query.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(text));
        }

        var separator = endpointUrl.Contains('?') ? '&' : '?';
        return new Uri($"{endpointUrl}{separator}{query}");
    }

    private static bool CarriesQuery(HttpMethod method) => method == HttpMethod.Get || method == HttpMethod.Delete;
}
