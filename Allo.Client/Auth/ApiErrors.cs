using System.Net.Http.Json;

namespace Allo.Client.Auth;

public static class ApiErrors
{
    private sealed record ValidationProblem(Dictionary<string, string[]>? Errors);

    // The messages from a validation problem response, joined, or a generic fallback.
    public static async Task<string> ReadAsync(HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>();
            var messages = problem?.Errors?.Values.SelectMany(m => m).ToList();
            if (messages is { Count: > 0 })
            {
                return string.Join(" ", messages);
            }
        }
        catch (Exception)
        {
        }
        return "Something went wrong. Try again.";
    }
}
