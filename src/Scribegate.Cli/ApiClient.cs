using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Scribegate.Cli;

public class ApiClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string Host { get; }

    public ApiClient() : this(null, null) { }

    public ApiClient(string? hostOverride, string? tokenOverride)
    {
        var config = CliConfig.Load();
        var host = hostOverride is not null
            ? CliConfig.NormalizeHost(hostOverride)
            : config.ResolvedHost;
        Host = host;

        _http = new HttpClient
        {
            BaseAddress = new Uri(host),
        };

        var token = tokenOverride ?? config.Token ?? Environment.GetEnvironmentVariable("SCRIBEGATE_TOKEN");
        if (!string.IsNullOrEmpty(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<T?> GetAsync<T>(string path)
    {
        var response = await _http.GetAsync(path);
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
    }

    public async Task<T?> PostAsync<T>(string path, object body)
    {
        var response = await _http.PostAsJsonAsync(path, body, _jsonOptions);
        await EnsureSuccess(response);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return default;
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
    }

    public async Task<T?> PutAsync<T>(string path, object body)
    {
        var response = await _http.PutAsJsonAsync(path, body, _jsonOptions);
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
    }

    public async Task DeleteAsync(string path)
    {
        var response = await _http.DeleteAsync(path);
        await EnsureSuccess(response);
    }

    public async Task PostAsync(string path, object? body = null)
    {
        var response = body is not null
            ? await _http.PostAsJsonAsync(path, body, _jsonOptions)
            : await _http.PostAsync(path, null);
        await EnsureSuccess(response);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new CliException($"HTTP {(int)response.StatusCode}: {body}");
        }
    }
}

public class CliException(string message) : Exception(message);
