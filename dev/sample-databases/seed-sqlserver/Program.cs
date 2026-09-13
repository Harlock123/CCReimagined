using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// Applies seed/sqlserver/01-schema.sql to a SQL Server instance.
//
// SQL Server has no docker-entrypoint-initdb.d convention, and the usual answer — running
// sqlcmd from mcr.microsoft.com/mssql-tools — is no help on arm64, where that image does not
// exist and Azure SQL Edge ships no client tools of its own. This needs neither: it is the
// same .NET client the generated code uses.

var connectionString = args.Length > 0
    ? args[0]
    : "Server=localhost,1433;User ID=sa;Password=ccr_Dev_Password1;" +
      "Initial Catalog=master;TrustServerCertificate=True;Connect Timeout=30";

var scriptPath = args.Length > 1
    ? args[1]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "seed", "sqlserver", "01-schema.sql");

scriptPath = Path.GetFullPath(scriptPath);

if (!File.Exists(scriptPath))
{
    Console.Error.WriteLine($"Seed script not found: {scriptPath}");
    return 1;
}

var script = await File.ReadAllTextAsync(scriptPath);

// GO is a batch separator understood by the client tools, not a T-SQL statement, so the
// script has to be split on it and each batch sent separately.
var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
    .Select(b => b.Trim())
    .Where(b => b.Length > 0)
    .ToList();

Console.WriteLine($"Applying {batches.Count} batch(es) from {Path.GetFileName(scriptPath)}");

await using var connection = new SqlConnection(connectionString);

try
{
    await connection.OpenAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not connect: {ex.Message}");
    return 1;
}

connection.InfoMessage += (_, e) => Console.WriteLine($"  {e.Message}");

for (var i = 0; i < batches.Count; i++)
{
    // USE switches database for the rest of the script, which only works if every batch
    // travels down the same connection — so one connection is reused throughout.
    await using var command = connection.CreateCommand();
    command.CommandText = batches[i];
    command.CommandTimeout = 120;

    try
    {
        await command.ExecuteNonQueryAsync();
    }
    catch (SqlException ex)
    {
        Console.Error.WriteLine($"Batch {i + 1} failed: {ex.Message}");
        Console.Error.WriteLine(batches[i].Length > 300 ? batches[i][..300] + "..." : batches[i]);
        return 1;
    }
}

Console.WriteLine("Done.");
return 0;
