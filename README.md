# Aisha AI .NET SDK

`Aisha.AI` is the .NET SDK for the [Aisha AI](https://aisha.group) API.

Use it in ASP.NET Core, worker services, console apps, desktop apps, and other C# projects.

```csharp
using Aisha.AI;

using var client = new AishaClient("your-api-key");
var result = await client.TtsAsync(new TtsRequest { Transcript = "Salom dunyo" });

Console.WriteLine(result["audioUrl"]);
```

## Contents

- [Features](#features)
- [Requirements](#requirements)
- [Install](#install)
- [Quick Start](#quick-start)
- [API Versions](#api-versions)
- [Server Integration Flow](#server-integration-flow)
- [Client Setup](#client-setup)
- [Text-to-Speech](#text-to-speech)
- [Speech-to-Text](#speech-to-text)
- [History](#history)
- [Parameter Reference](#parameter-reference)
- [SDK Constants](#sdk-constants)
- [Error Handling](#error-handling)
- [Development](#development)
- [Continuous Integration](#continuous-integration)
- [Official API Docs](#official-api-docs)

## Features

- Text-to-speech (TTS)
- Speech-to-text (STT)
- TTS and STT history
- Sync TTS requests
- Async TTS requests with webhook callbacks
- Save generated TTS audio to a local file path
- API version support
- Custom `HttpClient` support
- Targets `netstandard2.0` and `net8.0`

## Requirements

- .NET 8 or newer for development and tests
- Any app compatible with `netstandard2.0` or `net8.0`
- An Aisha AI API key

Create an API key at [space.aisha.group](https://space.aisha.group).

## Install

```bash
dotnet add package Aisha.AI
```

## Quick Start

```csharp
using Aisha.AI;

using var client = new AishaClient("your-api-key");

var result = await client.TtsAsync(new TtsRequest
{
    Transcript = "Salom dunyo"
});

Console.WriteLine(result["audioUrl"]);
```

Keep production API keys on your backend. Do not ship server API keys in public clients.

## API Versions

This SDK defaults to Aisha API `v1`.

| Feature | API version | SDK methods |
| --- | --- | --- |
| TTS sync and async | API v1 | `TtsAsync`, `TtsStatusAsync`, `TtsHistoryAsync` |
| STT short-audio sync | API v1 | `SttAsync`, `SttHistoryAsync` |

Choose another API version when creating the client:

```csharp
using var client = new AishaClient("your-api-key", apiVersion: "v2");
```

You can also pass `apiVersion: "2"`; the SDK normalizes it to `v2`.

## Server Integration Flow

For server-to-server traffic, Aisha uses the `X-Api-Key` header. The SDK sends
this header for every request.

Recommended backend integration flow:

1. Create an API key in Space.
2. Send a small test TTS or STT request.
3. Confirm the response shape your app needs.
4. For async TTS, store the returned `id` or `task_id`.
5. Use status and history endpoints to cover the full workflow.

## Client Setup

```csharp
using var client = new AishaClient("your-api-key");
```

Set response language for API messages:

```csharp
using var client = new AishaClient("your-api-key", language: "uz");
```

Use a custom API base URL:

```csharp
using var client = new AishaClient(
    "your-api-key",
    baseUrl: "https://back.aisha.group");
```

Pass your own `HttpClient` when your app manages HTTP clients through dependency
injection:

```csharp
using var httpClient = new HttpClient();
using var client = new AishaClient("your-api-key", httpClient: httpClient);
```

## Text-to-Speech

Basic TTS returns an audio URL:

```csharp
var result = await client.TtsAsync(new TtsRequest
{
    Transcript = "Assalomu alaykum"
});

Console.WriteLine(result["audioUrl"]);
```

Save TTS audio to a file path:

```csharp
var result = await client.TtsAsync(new TtsRequest
{
    Transcript = "Assalomu alaykum",
    OutputPath = "outputs/assalomu-alaykum.wav"
});

Console.WriteLine(result["audioUrl"]);
Console.WriteLine(result["outputPath"]);
```

For Uzbek TTS, pass `Model`, `Mood`, and `Speed`:

```csharp
var result = await client.TtsAsync(new TtsRequest
{
    Transcript = "Assalomu alaykum",
    Language = "uz",
    Model = "Gulnoza",
    Mood = "Happy",
    Speed = 1.0
});
```

Known mood values:

- `Neutral`
- `Cheerful`
- `Happy`
- `Sad`

Default model: `Gulnoza`

Speed:

- `0` uses the API default speed
- `0.5` is slower
- `1.0` is normal speed
- `2.0` is faster

The SDK sends `Model`, `Mood`, and `Speed` only when `Language` is `uz`.

Async TTS with webhook:

```csharp
var queued = await client.TtsAsync(new TtsRequest
{
    Transcript = "Bu async TTS so'rovi.",
    WebhookUrl = "https://example.com/webhooks/tts"
});

var status = await client.TtsStatusAsync(queued["id"]!);
```

## Speech-to-Text

```csharp
byte[] audio = await File.ReadAllBytesAsync("meeting.wav");
var result = await client.SttAsync(new SttRequest
{
    FileBytes = audio,
    Filename = "meeting.wav",
    Language = "uz"
});

Console.WriteLine(result["transcript"]);
```

Enable diarization when you need speaker separation:

```csharp
var result = await client.SttAsync(new SttRequest
{
    FileBytes = audio,
    Filename = "meeting.wav",
    Language = "uz",
    Diarization = true
});
```

## History

```csharp
var ttsHistory = await client.TtsHistoryAsync(new HistoryOptions { Page = 1, Limit = 10 });
var sttHistory = await client.SttHistoryAsync(new HistoryOptions { Page = 1, Limit = 10 });
```

## Parameter Reference

### `AishaClient`

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `apiKey` | `string` | Required | API key from Space. Sent as `X-Api-Key`. |
| `baseUrl` | `string` | `https://back.aisha.group` | API base URL. |
| `apiVersion` | `string` | `v1` | API version in endpoint paths. Accepts `v1` or `1`. |
| `language` | `string?` | `null` | Optional `Accept-Language` header. |
| `timeout` | `TimeSpan?` | 120 seconds | Request timeout. |
| `httpClient` | `HttpClient?` | `null` | Optional custom HTTP client. |

### `TtsRequest`

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Transcript` | `string` | Required | Text to turn into speech. Max 1000 characters. |
| `Language` | `string` | `uz` | Voice language. Common values: `uz`, `en`, `ru`. |
| `Model` | `string` | `Gulnoza` | Uzbek voice model. |
| `Mood` | `string` | `Neutral` | Uzbek voice mood. |
| `Speed` | `double` | `1.0` | Uzbek speed. Use `0`, or `0.5` to `2.0`. |
| `WebhookUrl` | `string?` | `null` | Webhook URL for async TTS. |
| `OutputPath` | `string?` | `null` | Local file path where audio should be saved. |

### `SttRequest`

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `FileBytes` | `byte[]` | Required | Audio bytes. |
| `Filename` | `string` | `audio.wav` | File name sent to the API. |
| `Language` | `string` | `uz` | Audio language. |
| `Diarization` | `bool` | `false` | Enable speaker diarization. |

## SDK Constants

```csharp
AishaClient.SupportedLanguages      // ["uz", "en", "ru"]
AishaClient.SupportedAudioFormats   // ["mp3", "wav", "ogg", "m4a"]
AishaClient.TtsMoods                // ["Neutral", "Cheerful", "Happy", "Sad"]
AishaClient.DefaultTtsModel         // "Gulnoza"
AishaClient.DefaultTtsMood          // "Neutral"
AishaClient.DefaultTtsSpeed         // 1.0
AishaClient.MinTtsSpeed             // 0.5
AishaClient.MaxTtsSpeed             // 2.0
AishaClient.MaxTtsTranscriptLength  // 1000
```

## Error Handling

```csharp
try
{
    var result = await client.TtsAsync(new TtsRequest { Transcript = "Salom" });
    Console.WriteLine(result["audioUrl"]);
}
catch (AishaConnectionException error)
{
    Console.WriteLine(error.Message);
}
catch (AishaApiException error)
{
    Console.WriteLine(error.StatusCode);
    Console.WriteLine(error.Body);
}
catch (ArgumentException error)
{
    Console.WriteLine(error.Message);
}
```

## Development

```bash
dotnet restore
dotnet format
dotnet test
```

Run real API smoke tests when `AISHA_API_TEST_KEY` is available:

```bash
RUN_AISHA_REAL_API_TESTS=1 AISHA_API_TEST_KEY="your-api-key"   dotnet test tests/Aisha.AI.Tests/Aisha.AI.Tests.csproj --filter FullyQualifiedName~RealApiTests
```

## Continuous Integration

GitHub Actions runs these checks on pull requests and pushes to `main`:

- Format check
- Build
- Unit tests on .NET 8 and .NET 10 SDKs
- NuGet package build
- Real API smoke tests when `AISHA_API_TEST_KEY` is configured

## Official API Docs

- [Text-to-Speech API](https://aisha.group/en/api-documentation/text-to-speech)
- [Speech-to-Text API](https://aisha.group/en/api-documentation/speech-to-text)
