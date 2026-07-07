using Aisha.AI;
using Xunit;

namespace Aisha.AI.Tests;

public sealed class RealApiTests
{
    private static AishaClient? CreateClientOrNull()
    {
        bool shouldRun = Environment.GetEnvironmentVariable("RUN_AISHA_REAL_API_TESTS") == "1";
        string? apiKey = Environment.GetEnvironmentVariable("AISHA_API_TEST_KEY");
        return shouldRun && !string.IsNullOrWhiteSpace(apiKey) ? new AishaClient(apiKey) : null;
    }

    [Fact]
    public async Task ShortTtsReturnsAudioUrl()
    {
        using AishaClient? client = CreateClientOrNull();
        if (client is null) { return; }

        Dictionary<string, object?> result = await client.TtsAsync(new TtsRequest { Transcript = "Salom dunyo!" });

        Assert.True(result["audioUrl"] is string url && url.Length > 0);
    }

    [Fact]
    public async Task TtsWithOptionsReturnsAudioUrl()
    {
        using AishaClient? client = CreateClientOrNull();
        if (client is null) { return; }

        Dictionary<string, object?> result = await client.TtsAsync(new TtsRequest { Transcript = "Bugun kayfiyat yaxshi.", Mood = "Happy", Speed = 1.0 });

        Assert.True(result["audioUrl"] is string url && url.Length > 0);
    }

    [Fact]
    public async Task TtsCanDownloadOutputPath()
    {
        using AishaClient? client = CreateClientOrNull();
        if (client is null) { return; }

        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outputPath = Path.Combine(dir, "tts.wav");
        try
        {
            Dictionary<string, object?> result = await client.TtsAsync(new TtsRequest { Transcript = "Faylga saqlash sinovi.", OutputPath = outputPath });

            Assert.Equal(outputPath, result["outputPath"]);
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HistoryAndValidationEdgeCases()
    {
        using AishaClient? client = CreateClientOrNull();
        if (client is null) { return; }

        Dictionary<string, object?> history = await client.TtsHistoryAsync(new HistoryOptions { Page = 1, Limit = 1 });
        Assert.True(history.ContainsKey("results"));

        await Assert.ThrowsAsync<ArgumentException>(() => client.TtsAsync(new TtsRequest { Transcript = "" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.TtsAsync(new TtsRequest { Transcript = "Hi", Language = "de" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.TtsAsync(new TtsRequest { Transcript = new string('a', 1001) }));
    }
}
