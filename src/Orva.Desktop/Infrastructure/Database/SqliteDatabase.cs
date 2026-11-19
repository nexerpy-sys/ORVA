using System;
using System.Data;
using System.IO;
using System.Data.SQLite;

namespace Orva.Infrastructure.Database
{
    public sealed class SqliteDatabase
    {
        private readonly string _databasePath;
        private readonly object _walLock = new object();
        private volatile bool _walInitialized;

        public SqliteDatabase(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("A database path is required.", "databasePath");
            _databasePath = databasePath;
        }

        public SQLiteConnection OpenConnection()
        {
            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Version = 3,
                Pooling = true,
                DefaultTimeout = 5
            };

            var connection = new SQLiteConnection(builder.ConnectionString);
            connection.Open();
            ExecutePragma(connection, "PRAGMA foreign_keys = ON;");
            ExecutePragma(connection, "PRAGMA busy_timeout = 5000;");
            if (!_walInitialized) { lock (_walLock) { if (!_walInitialized) { ExecutePragma(connection, "PRAGMA journal_mode = WAL;"); _walInitialized = true; } } }
            ExecutePragma(connection, "PRAGMA synchronous = FULL;");
            return connection;
        }

        public string DatabasePath { get { return _databasePath; } }

        private static void ExecutePragma(SQLiteConnection connection, string sql)
        {
            using (var command = new SQLiteCommand(sql, connection)) command.ExecuteNonQuery();
        }
    }
}