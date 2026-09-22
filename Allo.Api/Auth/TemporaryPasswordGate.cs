namespace Allo.Api.Auth;

// A temporary password has to be changed before the account can do anything else.
// The client routes to the Account page, but the UI is not the boundary: a temporary
// password handed out over text or read over the phone would otherwise keep full API
// access for as long as nobody got around to changing it.
public static class TemporaryPasswordGate
{
    // Set during cookie validation, which already loads the login on every request.
    public const string MustChangePasswordItem = "Allo.MustChangePassword";

    // Marks the endpoints an unchanged temporary password may still call: enough to see
    // who you are and to set a real password, and nothing else.
    private sealed class Allowed;

    public static TBuilder AllowTemporaryPassword<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new Allowed());
        return builder;
    }

    public static async ValueTask<object?> Filter(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.Items[MustChangePasswordItem] is true
            && http.GetEndpoint()?.Metadata.GetMetadata<Allowed>() is null)
        {
            return Results.Problem(
                title: "Password change required",
                detail: "This account is still on a temporary password and must set a new one before it can be used.",
                statusCode: StatusCodes.Status403Forbidden);
        }
        return await next(context);
    }
}
