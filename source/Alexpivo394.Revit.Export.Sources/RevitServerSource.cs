using System.Net.Http;
using System.Text.Json;

namespace Alexpivo394.Revit.Export.Sources;

/// <summary>
///     Дерево Revit Server через его REST API (тот же, что использует Revit Server Administrator).
/// </summary>
public sealed class RevitServerSource : ITreeSource
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly string _server;
    private readonly string _year;
    // Один клиент на всё время работы Revit: новый на каждое обновление дерева расходовал бы сокеты.
    private static readonly HttpClient SharedClient = new() { Timeout = Timeout };

    private readonly HttpClient _client;

    public RevitServerSource(string server, string year, HttpMessageHandler? handler = null)
    {
        _server = server;
        _year = year;
        _client = handler is null ? SharedClient : new HttpClient(handler) { Timeout = Timeout };
    }

    public string RootPath => "";

    public async Task<IReadOnlyList<SourceEntry>> GetChildrenAsync(string folderPath, CancellationToken ct)
    {
        string body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, RevitServerPaths.ContentsUrl(_server, _year, folderPath));
            request.Headers.Add("User-Name", Environment.UserName);
            request.Headers.Add("User-Machine-Name", Environment.MachineName);
            request.Headers.Add("Operation-GUID", Guid.NewGuid().ToString());

            using var response = await _client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new SourceException(Describe($"сервер ответил {(int)response.StatusCode} {response.ReasonPhrase}"));

            body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch (SourceException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var reason = exception is TaskCanceledException ? "сервер не ответил за 15 секунд" : exception.Message;
            throw new SourceException(Describe(reason), exception);
        }

        return Parse(body, folderPath);
    }

    private IReadOnlyList<SourceEntry> Parse(string body, string folderPath)
    {
        List<string> folderNames, modelNames;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("ожидался объект JSON");
            folderNames = Names(document.RootElement, "Folders");
            modelNames = Names(document.RootElement, "Models");
        }
        catch (Exception exception)
        {
            throw new SourceException(Describe("непонятный ответ сервера"), exception);
        }

        var folders = folderNames
            .Select(name => new SourceEntry(name, RevitServerPaths.Combine(folderPath, name), "", true));
        var models = modelNames
            .Select(name => new SourceEntry(name, folderPath, RevitServerPaths.ModelPath(_server, folderPath, name), false));

        return folders.Concat(models).ToList();
    }

    /// <summary>Имена из массива объектов <c>{ "Name": … }</c>; массива нет — пусто.</summary>
    private static List<string> Names(JsonElement json, string property)
    {
        if (!json.TryGetProperty(property, out var items) || items.ValueKind != JsonValueKind.Array) return [];
        return items.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("Name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private string Describe(string reason) => $"Не удалось получить содержимое {_server}: {reason}";
}
