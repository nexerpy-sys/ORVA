using System;
using System.Data.SQLite;
using System.Globalization;

namespace Orva.Services
{
    /// <summary>
    /// Shared transactional helpers for generation-safe StockData.json dirty tracking.
    /// Every stock-changing SQLite transaction must call MarkDirtyAndIncrementGeneration
    /// inside the same database transaction as the stock mutation.
    /// </summary>
    public static class StockSnapshotState
    {
        public const string DirtyKey = "stock_snapshot_dirty";
        public const string GenerationKey = "stock_snapshot_generation";
        public const string LastWrittenUtcKey = "stock_snapshot_last_written_at_utc";

        public static long MarkDirtyAndIncrementGeneration(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string utcText)
        {
            if (connection == null) throw new ArgumentNullException("connection");
            if (transaction == null) throw new ArgumentNullException("transaction");

            long nextGeneration = checked(ReadGeneration(connection, transaction) + 1L);

            UpsertSetting(connection, transaction, GenerationKey, nextGeneration.ToString(CultureInfo.InvariantCulture), utcText);
            UpsertSetting(connection, transaction, DirtyKey, "1", utcText);

            return nextGeneration;
        }

        public static long ReadGeneration(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            if (connection == null) throw new ArgumentNullException("connection");

            using (var command = new SQLiteCommand(
                "SELECT setting_value FROM app_setting WHERE setting_key = @key;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@key", GenerationKey);
                var raw = command.ExecuteScalar();
                if (raw == null || raw == DBNull.Value) return 0L;

                long parsed;
                return long.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                    ? parsed
                    : 0L;
            }
        }

        public static void UpsertSetting(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string key,
            string value,
            string utcText)
        {
            const string sql = @"
INSERT INTO app_setting(setting_key, setting_value, updated_at_utc)
VALUES(@key, @value, @updated)
ON CONFLICT(setting_key) DO UPDATE
SET setting_value = excluded.setting_value,
    updated_at_utc = excluded.updated_at_utc;";

            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@key", key);
                command.Parameters.AddWithValue("@value", value);
                command.Parameters.AddWithValue("@updated", utcText);
                command.ExecuteNonQuery();
            }
        }
    }
}
