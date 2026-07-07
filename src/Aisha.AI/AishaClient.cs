using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Aisha.AI;

/// <summary>SDK client for the Aisha AI API.</summary>
public sealed class AishaClient : IDisposable
{
    public const string DefaultBaseUrl = "https://back.aisha.group";
    public const string DefaultApiVersion = "v1";
    public const string TtsApiVersion = "v1";
    public const string SttApiVersion = "v1";
    public const string DefaultTtsModel = "Gulnoza";
    public const string DefaultTtsMood = "Neutral";
    public const double DefaultTtsSpeed = 1.0;
    public const double MinTtsSpeed = 0.5;
    public const double MaxTtsSpeed = 2.0;
    public const int MaxTtsTranscriptLength = 1000;

    public static readonly IReadOnlyList<string> SupportedLanguages = new[] { "uz", "en", "ru" };
    public static readonly IReadOnlyList<string> SupportedAudioFormats = new[] { "mp3", "wav", "ogg", "m4a" };
    public static readonly IReadOnlyList<string> TtsMoods = new[] { "Neutral", "Cheerful", "Happy", "Sad" };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _apiKey;

    public AishaClient(
        string apiKey,
        string baseUrl = DefaultBaseUrl,
        string apiVersion = DefaultApiVersion,
        string? language = null,
        TimeSpan? timeout = null,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("AishaClient requires an apiKey from https://space.aisha.group", nameof(apiKey));
        }
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("baseUrl must not be empty", nameof(baseUrl));
        }
        if (language is not null)
        {
            ValidateLanguage(language);
        }

        _apiKey = apiKey;
        BaseUrl = baseUrl.TrimEnd('/');
        ApiVersion = NormalizeApiVersion(apiVersion);
        Language = language;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _httpClient.Timeout = timeout ?? TimeSpan.FromSeconds(120);
    }

    public string BaseUrl { get; }
    public string ApiVersion { get; }
    public string? Language { get; }

    public string ApiPath(string resource, string endpoint = "")
    {
        string cleanResource = resource.Trim('/');
        string cleanEndpoint = endpoint.Trim('/');
        return cleanEndpoint.Length == 0
            ? $"/api/{ApiVersion}/{cleanResource}/"
            : $"/api/{ApiVersion}/{cleanResource}/{cleanEndpoint}/";
    }

    public async Task<Dictionary<string, object?>> TtsAsync(TtsRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }
        ValidateTts(request);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(request.Transcript), "transcript");
        content.Add(new StringContent(request.Language), "language");
        if (request.Language == "uz")
        {
            content.Add(new StringContent(request.Model), "model");
            content.Add(new StringContent(request.Mood), "mood");
            content.Add(new StringContent(request.Speed.ToString(CultureInfo.InvariantCulture)), "speed");
        }
        if (!string.IsNullOrWhiteSpace(request.WebhookUrl))
        {
            content.Add(new StringContent(request.WebhookUrl), "webhook_notification_url");
        }

        Dictionary<string, object?> result = RequireObject(
            await SendAsync(HttpMethod.Post, ApiPath("tts", "post"), content, cancellationToken).ConfigureAwait(false),
            "TtsAsync");

        if (FirstString(result, "audio_path", "audio_url", "audioUrl") is { } audioPath)
        {
            string audioUrl = AbsoluteAudioUrl(audioPath);
            result["audioUrl"] = audioUrl;
            if (!string.IsNullOrWhiteSpace(request.OutputPath))
            {
                result["outputPath"] = await DownloadAudioAsync(audioUrl, request.OutputPath, cancellationToken).ConfigureAwait(false);
            }
        }
        return result;
    }

    public async Task<string> DownloadAudioAsync(string audioUrl, string outputPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(audioUrl))
        {
            throw new ArgumentException("download audio requires a url", nameof(audioUrl));
        }
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("download audio requires an output path", nameof(outputPath));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, audioUrl);
        AddHeaders(request);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/*"));
        request.Headers.UserAgent.ParseAdd("aisha-ai-dotnet/1.0.0");

        using HttpResponseMessage response = await SendRawAsync(request, cancellationToken).ConfigureAwait(false);
        byte[] data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AishaApiException($"Audio download failed: {(int)response.StatusCode} {response.ReasonPhrase} ({audioUrl})", (int)response.StatusCode, TryParseJson(data), audioUrl);
        }

        string? parent = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
        File.WriteAllBytes(outputPath, data);
        return outputPath;
    }

    public async Task<Dictionary<string, object?>> TtsStatusAsync(object id, CancellationToken cancellationToken = default)
    {
        string idText = id?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(idText))
        {
            throw new ArgumentException("tts status requires an id", nameof(id));
        }
        string encodedId = Uri.EscapeDataString(idText);
        Dictionary<string, object?> result = RequireObject(await SendAsync(HttpMethod.Get, ApiPath("tts", $"status/{encodedId}"), null, cancellationToken).ConfigureAwait(false), "TtsStatusAsync");
        if (FirstString(result, "audio_path", "audio_url", "audioUrl") is { } audioPath)
        {
            result["audioUrl"] = AbsoluteAudioUrl(audioPath);
        }
        return result;
    }

    public Task<Dictionary<string, object?>> TtsHistoryAsync(HistoryOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new HistoryOptions();
        ValidatePageLimit(options);
        return RequestObjectAsync($"{ApiPath("tts", "get")}?page={options.Page}&limit={options.Limit}", "TtsHistoryAsync", cancellationToken);
    }

    public async Task<Dictionary<string, object?>> SttAsync(SttRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }
        if (request.FileBytes.Length == 0)
        {
            throw new ArgumentException("stt file content must not be empty", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Filename))
        {
            throw new ArgumentException("stt filename must not be empty", nameof(request));
        }
        ValidateLanguage(request.Language);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(request.Language), "language");
        content.Add(new StringContent(request.Diarization ? "true" : "false"), "has_diarization");
        content.Add(new ByteArrayContent(request.FileBytes), "file", request.Filename);

        return RequireObject(await SendAsync(HttpMethod.Post, ApiPath("stt", "post"), content, cancellationToken).ConfigureAwait(false), "SttAsync");
    }

    public Task<Dictionary<string, object?>> SttHistoryAsync(HistoryOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new HistoryOptions();
        ValidatePageLimit(options);
        return RequestObjectAsync($"{ApiPath("stt", "get")}?page={options.Page}&limit={options.Limit}", "SttHistoryAsync", cancellationToken);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public static string NormalizeApiVersion(string apiVersion)
    {
        if (string.IsNullOrWhiteSpace(apiVersion))
        {
            throw new ArgumentException("apiVersion must not be empty", nameof(apiVersion));
        }
        string version = apiVersion.Trim().ToLowerInvariant();
        if (version.All(char.IsDigit))
        {
            version = "v" + version;
        }
        if (version.Length < 2 || version[0] != 'v' || !version.Skip(1).All(char.IsDigit))
        {
            throw new ArgumentException("apiVersion must look like 'v1', 'v2', or 1", nameof(apiVersion));
        }
        return version;
    }

    private async Task<Dictionary<string, object?>> RequestObjectAsync(string path, string endpoint, CancellationToken cancellationToken)
    {
        return RequireObject(await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false), endpoint);
    }

    private async Task<object?> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BaseUrl + path) { Content = content };
        AddHeaders(request);
        using HttpResponseMessage response = await SendRawAsync(request, cancellationToken).ConfigureAwait(false);
        byte[] data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        object? body = TryParseJson(data);
        if (!response.IsSuccessStatusCode)
        {
            throw new AishaApiException($"Aisha API request failed: {(int)response.StatusCode} {response.ReasonPhrase} ({request.RequestUri})", (int)response.StatusCode, body, request.RequestUri?.ToString() ?? string.Empty);
        }
        return body;
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AishaConnectionException($"Could not reach the Aisha API: {ex.Message} ({request.Method} {request.RequestUri})", request.RequestUri?.ToString() ?? string.Empty, ex);
        }
    }

    private void AddHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);
        if (!string.IsNullOrWhiteSpace(Language))
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", Language);
        }
    }

    private void ValidateTts(TtsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Transcript))
        {
            throw new ArgumentException("tts requires a transcript", nameof(request));
        }
        if (request.Transcript.Length > MaxTtsTranscriptLength)
        {
            throw new ArgumentException($"tts transcript must be {MaxTtsTranscriptLength} characters or fewer", nameof(request));
        }
        ValidateLanguage(request.Language);
        if (!string.IsNullOrWhiteSpace(request.WebhookUrl) && !string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw new ArgumentException("outputPath cannot be used with async webhook TTS", nameof(request));
        }
    }

    private static void ValidateLanguage(string language)
    {
        if (!SupportedLanguages.Contains(language))
        {
            throw new ArgumentException($"language must be one of: {string.Join(", ", SupportedLanguages)}", nameof(language));
        }
    }

    private static void ValidatePageLimit(HistoryOptions options)
    {
        if (options.Page < 1)
        {
            throw new ArgumentException("page must be a positive integer", nameof(options));
        }
        if (options.Limit < 1)
        {
            throw new ArgumentException("limit must be a positive integer", nameof(options));
        }
    }

    private string AbsoluteAudioUrl(string audioPath)
    {
        if (Uri.TryCreate(audioPath, UriKind.Absolute, out Uri? absolute) && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return audioPath;
        }
        return new Uri(new Uri(BaseUrl + "/"), audioPath.TrimStart('/')).ToString();
    }

    private static string? FirstString(Dictionary<string, object?> value, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (value.TryGetValue(key, out object? item) && item is string text && text.Length > 0)
            {
                return text;
            }
        }
        return null;
    }

    private static Dictionary<string, object?> RequireObject(object? value, string endpoint)
    {
        if (value is Dictionary<string, object?> dictionary)
        {
            return dictionary;
        }
        throw new InvalidOperationException($"{endpoint} returned an unexpected non-object response");
    }

    private static object? TryParseJson(byte[] data)
    {
        if (data.Length == 0)
        {
            return null;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(data);
            return ConvertJson(document.RootElement);
        }
        catch (JsonException)
        {
            return Encoding.UTF8.GetString(data);
        }
    }

    private static object? ConvertJson(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => ConvertJson(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJson).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long integer) ? integer : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}

public sealed class TtsRequest
{
    public string Transcript { get; set; } = string.Empty;
    public string Language { get; set; } = "uz";
    public string Model { get; set; } = AishaClient.DefaultTtsModel;
    public string Mood { get; set; } = AishaClient.DefaultTtsMood;
    public double Speed { get; set; } = AishaClient.DefaultTtsSpeed;
    public string? WebhookUrl { get; set; }
    public string? OutputPath { get; set; }
}

public sealed class SttRequest
{
    public byte[] FileBytes { get; set; } = Array.Empty<byte>();
    public string Filename { get; set; } = "audio.wav";
    public string Language { get; set; } = "uz";
    public bool Diarization { get; set; }
}

public sealed class HistoryOptions
{
    public int Page { get; set; } = 1;
    public int Limit { get; set; } = 10;
}

public class AishaApiException : Exception
{
    public AishaApiException(string message, int statusCode, object? body, string url, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Body = body;
        Url = url;
    }

    public int StatusCode { get; }
    public object? Body { get; }
    public string Url { get; }
}

public sealed class AishaConnectionException : AishaApiException
{
    public AishaConnectionException(string message, string url, Exception? innerException = null)
        : base(message, 0, null, url, innerException)
    {
    }
}
