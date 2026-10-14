using Npgsql;

namespace TurboTest.EFCore.Internal;

/// <summary>
/// Helper extensions for Npgsql operations
/// </summary>
internal static class NpgsqlExtensions
{
    public static async Task<int> ExecuteNonQueryAsync(this NpgsqlConnection connection, string sql)
    {
        using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync();
    }

    public static async Task<object?> ExecuteScalarAsync(this NpgsqlConnection connection, string sql)
    {
        using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}