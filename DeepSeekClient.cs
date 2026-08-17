using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Huge;

/// <summary>
/// DeepSeek 兼容 OpenAI 的 Chat Completions 接口，支持 SSE 流式输出。
/// </summary>
public class DeepSeekClient
{
    private readonly ApiSettings _settings;

    public DeepSeekClient(ApiSettings settings)
    {
        _settings = settings;
    }

    public class ChatMessage
    {
        public string Role { get; set; } = "user"; // system / user / assistant
        public string Content { get; set; } = "";
    }

    /// <summary>
    /// 流式发起一次对话。每次解析到新内容就回调 onDelta，完成后返回完整回复。
    /// </summary>
    public async Task<string> ChatStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        Action<string> onDelta,
        CancellationToken ct)
    {
        var baseUrl = _settings.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/chat/completions";

        var requestBody = new
        {
            model = _settings.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            stream = true
        };

        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(10);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json")
        };

        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"API 请求失败 ({(int)response.StatusCode}): {errBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var fullReply = new StringBuilder();
        var lineBuffer = new StringBuilder();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var data = line["data:".Length..].Trim();
                if (data == "[DONE]") break;

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var choices = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() == 0) continue;

                    var delta = choices[0].GetProperty("delta");
                    if (delta.TryGetProperty("content", out var contentProp) &&
                        contentProp.ValueKind == JsonValueKind.String)
                    {
                        var piece = contentProp.GetString() ?? "";
                        if (piece.Length > 0)
                        {
                            fullReply.Append(piece);
                            onDelta(piece);
                        }
                    }
                }
                catch (JsonException)
                {
                    // 忽略无法解析的 SSE 行（如 keep-alive）
                }
            }
            else if (line.StartsWith("id:", StringComparison.Ordinal) ||
                     line.StartsWith("event:", StringComparison.Ordinal))
            {
                // 忽略元数据行
            }
            else if (line.Length > 0)
            {
                // 部分服务端会跨行拆分 JSON，先缓存拼接再尝试解析
                lineBuffer.Append(line);
                try
                {
                    using var doc = JsonDocument.Parse(lineBuffer.ToString());
                    var choices = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0].GetProperty("delta");
                        if (delta.TryGetProperty("content", out var contentProp) &&
                            contentProp.ValueKind == JsonValueKind.String)
                        {
                            var piece = contentProp.GetString() ?? "";
                            if (piece.Length > 0)
                            {
                                fullReply.Append(piece);
                                onDelta(piece);
                            }
                        }
                    }
                    lineBuffer.Clear();
                }
                catch (JsonException)
                {
                    // 缓冲未完整，等待下一行
                }
            }
        }

        return fullReply.ToString();
    }
}