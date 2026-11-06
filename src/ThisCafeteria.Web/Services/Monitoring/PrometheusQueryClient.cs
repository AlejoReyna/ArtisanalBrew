using System.Globalization;
using System.Text.Json;

namespace ThisCafeteria.Web.Services.Monitoring;

/// <summary>Runs one fixed PromQL expression and returns its single scalar result.</summary>
public interface IPrometheusQueryClient
{
    /// <returns>The value, or <c>null</c> when the expression matched no series.</returns>
    /// <exception cref="MonitoringUnavailableException">The source did not answer usefully within the time limit.</exception>
    Task<double?> QueryScalarAsync(string expression, CancellationToken cancellationToken);
}

public sealed class MonitoringUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Minimal Prometheus HTTP API client. Only <c>/api/v1/query</c> is ever called, with
/// expressions that live in code (<see cref="InfrastructureSummaryService"/>), never with
/// anything derived from a request.
/// </summary>
public sealed class PrometheusQueryClient(HttpClient http) : IPrometheusQueryClient
{
    public async Task<double?> QueryScalarAsync(string expression, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(
                $"api/v1/query?query={Uri.EscapeDataString(expression)}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new MonitoringUnavailableException($"Prometheus answered HTTP {(int)response.StatusCode}.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            return ParseScalar(document.RootElement);
        }
        catch (MonitoringUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout surfaces as a cancellation that the caller did not request.
            throw new MonitoringUnavailableException("Prometheus did not answer within the time limit.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            throw new MonitoringUnavailableException("Prometheus could not be read.", exception);
        }
    }

    internal static double? ParseScalar(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("status", out var status) ||
            status.GetString() != "success" ||
            !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Array)
        {
            throw new MonitoringUnavailableException("Prometheus returned an unexpected response.");
        }

        if (result.GetArrayLength() == 0)
        {
            return null;
        }

        // Instant vector: [{ "metric": {...}, "value": [ <ts>, "<number>" ] }]
        var sample = result[0];
        if (sample.ValueKind != JsonValueKind.Object ||
            !sample.TryGetProperty("value", out var value) ||
            value.ValueKind != JsonValueKind.Array ||
            value.GetArrayLength() != 2 ||
            value[1].ValueKind != JsonValueKind.String ||
            !double.TryParse(value[1].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            double.IsNaN(number) ||
            double.IsInfinity(number))
        {
            return null;
        }

        return number;
    }
}
