using System;
using System.IO;
using System.Reflection;
using System.Data.SQLite;

namespace Orva.Infrastructure.Database
{
    public sealed class DatabaseMigrator
    {
        public const int CurrentSchemaVersion = 3;
        private readonly SqliteDatabase _database;

        public DatabaseMigrator(SqliteDatabase database)
        {
            _database = database;
        }

        public void Migrate()
        {
            using (var connection = _database.OpenConnection())
            {
                var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"));
                if (version > CurrentSchemaVersion)
                    throw new InvalidOperationException("This ORVA data file was created by a newer version of the application. Update ORVA before opening it.");

                if (version < 1)
                {
                    ApplyMigration(connection, "Orva.Infrastructure.Database.001_InitialSchema.sql", 1);
                    version = 1;
                }

                if (version < 2)
                {
                    ApplyMigration(connection, "Orva.Infrastructure.Database.002_BillingTaxConfig.sql", 2);
                    version = 2;
                }

                if (version < 3)
                {
                    ApplyMigration(connection, "Orva.Infrastructure.Database.003_PerformanceIndexes.sql", 3);
                }
            }
        }

        private static void ApplyMigration(SQLiteConnection connection, string resourceName, int targetVersion)
        {
            var sql = ReadResource(resourceName);
            using (var transaction = connection.BeginTransaction())
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.ExecuteNonQuery();
                using (var versionCommand = new SQLiteCommand("PRAGMA user_version = " + targetVersion + ";", connection, transaction))
                    versionCommand.ExecuteNonQuery();
                transaction.Commit();
            }
        }

        private static object Scalar(SQLiteConnection connection, string sql)
        {
            using (var command = new SQLiteCommand(sql, connection)) return command.ExecuteScalar();
        }

        private static string ReadResource(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) throw new InvalidOperationException("Missing embedded migration resource: " + resourceName);
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }
    }
}