using System.Net;
using System.Text;
using Aisha.AI;
using Xunit;

namespace Aisha.AI.Tests;

public sealed class AishaClientTests
{
    [Fact]
    public void ConstructorNormalizesApiVersionAndBuildsPath()
    {
        using var client = new AishaClient("key", apiVersion: "2");

        Assert.Equal("v2", client.ApiVersion);
        Assert.Equal("/api/v2/tts/post/", client.ApiPath("tts", "post"));
    }

    [Fact]
    public void ConstructorRejectsInvalidValues()
    {
        Assert.Throws<ArgumentException>(() => new AishaClient(""));
        Assert.Throws<ArgumentException>(() => new AishaClient("key", apiVersion: "beta"));
        Assert.Throws<ArgumentException>(() => new AishaClient("key", language: "de"));
    }

    [Fact]
    public async Task TtsSendsUzbekOptionsAndAddsAudioUrl()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            capturedRequest = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return JsonResponse(request, "{"audio_path":"/backend/tts_audio/test.wav"}");
        }));
        using var client = new AishaClient("key", baseUrl: "https://example.test", httpClient: httpClient);

        Dictionary<string, object?> result = await client.TtsAsync(new TtsRequest { Transcript = "Salom", Mood = "Happy" });

        Assert.Equal("/api/v1/tts/post/", capturedRequest?.RequestUri?.AbsolutePath);
        Assert.Equal("key", capturedRequest?.Headers.GetValues("X-Api-Key").Single());
        Assert.Contains("Salom", capturedBody);
        Assert.Contains("Happy", capturedBody);
        Assert.Equal("https://example.test/backend/tts_audio/test.wav", result["audioUrl"]);
    }

    [Fact]
    public async Task EnglishTtsDoesNotSendUzbekOnlyFields()
    {
        string? capturedBody = null;
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return JsonResponse(request, "{"audio_url":"https://cdn.test/a.wav"}");
        }));
        using var client = new AishaClient("key", httpClient: httpClient);

        Dictionary<string, object?> result = await client.TtsAsync(new TtsRequest { Transcript = "Hello", Language = "en" });

        Assert.DoesNotContain("model", capturedBody);
        Assert.DoesNotContain("mood", capturedBody);
        Assert.DoesNotContain("speed", capturedBody);
        Assert.Equal("https://cdn.test/a.wav", result["audioUrl"]);
    }

    [Fact]
    public async Task TtsValidatesEdgeCasesBeforeSending()
    {
        using var client = new AishaClient("key");

        await Assert.ThrowsAsync<ArgumentException>(() => client.TtsAsync(new TtsRequest { Transcript = "" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.TtsAsync(new TtsRequest { Transcript = new string('a', 1001) }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.TtsAsync(new TtsRequest { Transcript = "Hi", Language = "de" }));
    }

    [Fact]
    public async Task ApiErrorsThrowAishaApiException()
    {
        using var httpClient = new HttpClient(new StubHandler(request => Task.FromResult(JsonResponse(request, "{"detail":"bad key"}", HttpStatusCode.Unauthorized))));
        using var client = new AishaClient("key", httpClient: httpClient);

        AishaApiException error = await Assert.ThrowsAsync<AishaApiException>(() => client.TtsAsync(new TtsRequest { Transcript = "Salom" }));

        Assert.Equal(401, error.StatusCode);
    }

    [Fact]
    public async Task HistoryUsesPagination()
    {
        Uri? capturedUri = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedUri = request.RequestUri;
            return Task.FromResult(JsonResponse(request, "{"count":0,"results":[]}"));
        }));
        using var client = new AishaClient("key", httpClient: httpClient);

        await client.TtsHistoryAsync(new HistoryOptions { Page = 2, Limit = 5 });

        Assert.Equal("/api/v1/tts/get/", capturedUri?.AbsolutePath);
        Assert.Equal("?page=2&limit=5", capturedUri?.Query);
    }

    [Fact]
    public async Task SttSendsMultipartRequest()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            capturedRequest = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return JsonResponse(request, "{"transcript":"salom"}");
        }));
        using var client = new AishaClient("key", httpClient: httpClient);

        Dictionary<string, object?> result = await client.SttAsync(new SttRequest { FileBytes = new byte[] { 1, 2, 3 }, Filename = "test.wav" });

        Assert.Equal("/api/v1/stt/post/", capturedRequest?.RequestUri?.AbsolutePath);
        Assert.Contains("test.wav", capturedBody);
        Assert.Equal("salom", result["transcript"]);
    }

    [Fact]
    public async Task DownloadAudioWritesFile()
    {
        using var httpClient = new HttpClient(new StubHandler(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }), RequestMessage = request })));
        using var client = new AishaClient("key", httpClient: httpClient);
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outputPath = Path.Combine(dir, "audio.wav");
        try
        {
            string path = await client.DownloadAudioAsync("https://cdn.test/audio.wav", outputPath);

            Assert.Equal(outputPath, path);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(outputPath));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static HttpResponseMessage JsonResponse(HttpRequestMessage request, string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            RequestMessage = request,
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

    public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _handler(request);
    }
}
