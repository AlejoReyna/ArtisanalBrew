using Microsoft.EntityFrameworkCore;

namespace ThisCafeteria.Infrastructure.Persistence;

internal static class DbUpdateExceptionDetails
{
    public static string GetRootMessage(DbUpdateException exception)
    {
        var current = exception.InnerException;
        while (current?.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current?.Message ?? exception.Message;
    }
}
