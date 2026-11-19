using System;
using System.Globalization;
using System.Data.SQLite;
using Orva.Infrastructure.Database;

namespace Orva.Infrastructure.Repositories
{
    public sealed class AppSettingsRepository
    {
        private readonly SqliteDatabase _database;
        public AppSettingsRepository(SqliteDatabase database) { _database = database; }

        public string Get(string key, string defaultValue)
        {
            using (var connection = _database.OpenConnection())
            using (var command = new SQLiteCommand("SELECT setting_value FROM app_setting WHERE setting_key=@key;", connection))
            {
                command.Parameters.AddWithValue("@key", key);
                var value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? defaultValue : Convert.ToString(value);
            }
        }

        public void Set(string key, string value)
        {
            var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            using (var connection = _database.OpenConnection())
            using (var command = new SQLiteCommand(@"
INSERT INTO app_setting(setting_key, setting_value, updated_at_utc) VALUES(@key, @value, @updated)
ON CONFLICT(setting_key) DO UPDATE SET setting_value=excluded.setting_value, updated_at_utc=excluded.updated_at_utc;", connection))
            {
                command.Parameters.AddWithValue("@key", key);
                command.Parameters.AddWithValue("@value", value ?? string.Empty);
                command.Parameters.AddWithValue("@updated", now);
                command.ExecuteNonQuery();
            }
        }
    }
}