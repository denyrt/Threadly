using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace Threadly.Api.IntegrationTests.Fixtures;

internal sealed class SqlQueryRecorder : DbCommandInterceptor
{
    public List<(string Sql, object?[] Parameters)> Commands { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Commands.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).ToArray()));
        return ValueTask.FromResult(result);
    }
}
