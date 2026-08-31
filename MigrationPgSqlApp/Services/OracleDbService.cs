using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using Oracle.ManagedDataAccess.Client;
using MigrationPgSqlApp.Models;

namespace MigrationPgSqlApp.Services
{
    public class OracleDbService
    {
        private readonly SqlConverter _sqlConverter = new();

        private static long SafeConvertToInt64(object val, long defaultValue)
        {
            if (val == null || val == DBNull.Value) return defaultValue;
            try
            {
                string str = val.ToString()?.Trim();
                if (string.IsNullOrEmpty(str)) return defaultValue;

                // Handle signs
                bool isNegative = str.StartsWith("-");
                string cleanStr = isNegative ? str.Substring(1) : str;

                // Truncate decimal places if any
                int dotIdx = cleanStr.IndexOf('.');
                if (dotIdx >= 0)
                {
                    cleanStr = cleanStr.Substring(0, dotIdx);
                }

                // If the integer part is longer than 19 digits, it definitely overflows long
                if (cleanStr.Length > 19)
                {
                    return isNegative ? long.MinValue : long.MaxValue;
                }

                // Parse using decimal first to handle large numbers and floating points safely
                string parsedStr = isNegative ? "-" + cleanStr : cleanStr;
                if (decimal.TryParse(parsedStr, out decimal decVal))
                {
                    if (decVal > long.MaxValue) return long.MaxValue;
                    if (decVal < long.MinValue) return long.MinValue;
                    return (long)decVal;
                }

                // Fallback to double conversion
                if (double.TryParse(parsedStr, out double dblVal))
                {
                    if (dblVal > long.MaxValue) return long.MaxValue;
                    if (dblVal < long.MinValue) return long.MinValue;
                    return (long)dblVal;
                }

                return Convert.ToInt64(val);
            }
            catch
            {
                return defaultValue;
            }
        }

        public string BuildConnectionString(string host, int port, string serviceNameOrSid, bool isSid, string userId, string password)
        {
            string connectData = isSid ? $"(SID={serviceNameOrSid})" : $"(SERVICE_NAME={serviceNameOrSid})";
            return $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA={connectData}));User Id={userId};Password={password};";
        }

        public bool TestConnection(string connStr, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                using (var conn = new OracleConnection(connStr))
                {
                    conn.Open();
                    return true;
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public List<DbTable> LoadTables(string connStr)
        {
            var tables = new List<DbTable>();
            var tableMap = new Dictionary<string, DbTable>(StringComparer.OrdinalIgnoreCase);

            using (var conn = new OracleConnection(connStr))
            {
                conn.Open();

                // 1. Get Table Names
                string tableSql = "SELECT TABLE_NAME FROM USER_TABLES ORDER BY TABLE_NAME";
                using (var cmd = new OracleCommand(tableSql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string tableName = reader.GetString(0);
                        var table = new DbTable { Name = tableName };
                        tables.Add(table);
                        tableMap[tableName] = table;
                    }
                }

                // 2. Get Columns
                string columnSql = @"
                    SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, DATA_LENGTH, DATA_PRECISION, DATA_SCALE, NULLABLE, DATA_DEFAULT 
                    FROM USER_TAB_COLUMNS 
                    ORDER BY TABLE_NAME, COLUMN_ID";
                using (var cmd = new OracleCommand(columnSql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string tableName = reader.GetString(0);
                        if (!tableMap.TryGetValue(tableName, out var table)) continue;

                        string colName = reader.GetString(1);
                        string dataType = reader.GetString(2);
                        long dataLength = SafeConvertToInt64(reader.GetValue(3), 0);
                        
                        int? precision = null;
                        if (!reader.IsDBNull(4)) precision = Convert.ToInt32(reader.GetValue(4));
                        
                        int? scale = null;
                        if (!reader.IsDBNull(5)) scale = Convert.ToInt32(reader.GetValue(5));
                        
                        bool isNullable = reader.GetString(6) == "Y";
                        
                        string? dataDefault = null;
                        if (!reader.IsDBNull(7))
                        {
                            dataDefault = reader.GetValue(7).ToString();
                        }

                        var col = new ColumnInfo
                        {
                            Name = colName,
                            OracleType = dataType,
                            Length = dataLength,
                            Precision = precision,
                            Scale = scale,
                            IsNullable = isNullable,
                            DefaultValue = dataDefault,
                            PgType = _sqlConverter.MapOracleToPgType(dataType, dataLength, precision, scale)
                        };
                        table.Columns.Add(col);
                    }
                }

                // 3. Get Primary Keys
                string pkSql = @"
                    SELECT cols.table_name, cols.column_name, cons.constraint_name
                    FROM user_constraints cons
                    JOIN user_cons_columns cols ON cons.constraint_name = cols.constraint_name
                    WHERE cons.constraint_type = 'P'
                    ORDER BY cols.table_name, cols.position";
                using (var cmd = new OracleCommand(pkSql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string tableName = reader.GetString(0);
                        if (!tableMap.TryGetValue(tableName, out var table)) continue;

                        string colName = reader.GetString(1);
                        string constraintName = reader.GetString(2);

                        if (table.PrimaryKey == null)
                        {
                            table.PrimaryKey = new PrimaryKeyInfo
                            {
                                ConstraintName = constraintName,
                                ColumnNames = new List<string>()
                            };
                        }
                        table.PrimaryKey.ColumnNames.Add(colName);

                        // Mark column model as PK
                        var col = table.Columns.Find(c => c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
                        if (col != null) col.IsPrimaryKey = true;
                    }
                }

                // 4. Get Indexes
                string idxSql = @"
                    SELECT i.table_name, i.index_name, c.column_name, i.uniqueness
                    FROM user_indexes i
                    JOIN user_ind_columns c ON i.index_name = c.index_name AND i.table_name = c.table_name
                    ORDER BY i.table_name, i.index_name, c.column_position";
                using (var cmd = new OracleCommand(idxSql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    string lastIndexName = string.Empty;
                    IndexInfo? currentIdx = null;

                    while (reader.Read())
                    {
                        string tableName = reader.GetString(0);
                        if (!tableMap.TryGetValue(tableName, out var table)) continue;

                        string indexName = reader.GetString(1);
                        string colName = reader.GetString(2);
                        bool isUnique = reader.GetString(3) == "UNIQUE";

                        if (indexName != lastIndexName)
                        {
                            currentIdx = new IndexInfo
                            {
                                IndexName = indexName,
                                IsUnique = isUnique,
                                ColumnNames = new List<string>()
                            };
                            table.Indexes.Add(currentIdx);
                            lastIndexName = indexName;
                        }
                        currentIdx?.ColumnNames.Add(colName);
                    }
                }

                // 5. Get Foreign Keys
                string fkSql = @"
                    SELECT 
                        a.table_name, a.column_name, a.constraint_name, 
                        c_pk.table_name r_table_name, b.column_name r_column_name
                    FROM user_cons_columns a
                    JOIN user_constraints c ON a.constraint_name = c.constraint_name
                    JOIN user_constraints c_pk ON c.r_constraint_name = c_pk.constraint_name
                    JOIN user_cons_columns b ON c_pk.constraint_name = b.constraint_name AND a.position = b.position
                    WHERE c.constraint_type = 'R'
                    ORDER BY a.table_name, a.constraint_name, a.position";
                using (var cmd = new OracleCommand(fkSql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string tableName = reader.GetString(0);
                        if (!tableMap.TryGetValue(tableName, out var table)) continue;

                        var fk = new ForeignKeyInfo
                        {
                            ColumnName = reader.GetString(1),
                            ConstraintName = reader.GetString(2),
                            ReferenceTable = reader.GetString(3),
                            ReferenceColumn = reader.GetString(4)
                        };
                        table.ForeignKeys.Add(fk);
                    }
                }

                // 6. Quick row count query
                foreach (var table in tables)
                {
                    try
                    {
                        using (var cmd = new OracleCommand($"SELECT COUNT(1) FROM \"{table.Name}\"", conn))
                        {
                            cmd.CommandTimeout = 5; // short timeout to keep UI responsive
                            table.RowCount = Convert.ToInt64(cmd.ExecuteScalar());
                        }
                    }
                    catch
                    {
                        table.RowCount = -1; // Unknown or timeout
                    }
                }
            }

            return tables;
        }

        public List<DbView> LoadViews(string connStr)
        {
            var views = new List<DbView>();
            using (var conn = new OracleConnection(connStr))
            {
                conn.Open();
                string viewSql = "SELECT VIEW_NAME, TEXT FROM USER_VIEWS ORDER BY VIEW_NAME";
                using (var cmd = new OracleCommand(viewSql, conn))
                {
                    cmd.InitialLOBFetchSize = -1; // Retrieve full LONG column
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string viewName = reader.GetString(0);
                            string text = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                            views.Add(new DbView { Name = viewName, Definition = text });
                        }
                    }
                }
            }
            return views;
        }

        public List<DbProcedure> LoadProcedures(string connStr)
        {
            var procedures = new List<DbProcedure>();
            using (var conn = new OracleConnection(connStr))
            {
                conn.Open();

                // 1. Get object names and types
                string listSql = @"
                    SELECT OBJECT_NAME, OBJECT_TYPE 
                    FROM USER_OBJECTS 
                    WHERE OBJECT_TYPE IN ('PROCEDURE', 'FUNCTION', 'PACKAGE', 'PACKAGE BODY')
                    ORDER BY OBJECT_NAME";
                
                using (var cmd = new OracleCommand(listSql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string name = reader.GetString(0);
                        string type = reader.GetString(1);
                        procedures.Add(new DbProcedure { Name = name, ObjectType = type });
                    }
                }

                // 2. Load source codes
                foreach (var proc in procedures)
                {
                    try
                    {
                        string srcSql = @"
                            SELECT TEXT 
                            FROM USER_SOURCE 
                            WHERE NAME = :name AND TYPE = :type
                            ORDER BY LINE";
                        using (var cmd = new OracleCommand(srcSql, conn))
                        {
                            cmd.Parameters.Add(new OracleParameter("name", proc.Name));
                            cmd.Parameters.Add(new OracleParameter("type", proc.ObjectType));

                            StringBuilder sb = new StringBuilder();
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    sb.Append(reader.GetString(0));
                                }
                            }
                            proc.SourceCode = sb.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        proc.SourceCode = $"-- Error loading source: {ex.Message}";
                    }
                }
            }
            return procedures;
        }

        public List<DbSequence> LoadSequences(string connStr)
        {
            var sequences = new List<DbSequence>();
            using (var conn = new OracleConnection(connStr))
            {
                conn.Open();
                string seqSql = "SELECT SEQUENCE_NAME, MIN_VALUE, MAX_VALUE, INCREMENT_BY, LAST_NUMBER FROM USER_SEQUENCES ORDER BY SEQUENCE_NAME";
                using (var cmd = new OracleCommand(seqSql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string name = reader.GetString(0);
                        long minVal = reader.IsDBNull(1) ? 1 : SafeConvertToInt64(reader.GetValue(1), 1);
                        long maxVal = reader.IsDBNull(2) ? 999999999999999999 : SafeConvertToInt64(reader.GetValue(2), 999999999999999999);
                        long increment = reader.IsDBNull(3) ? 1 : SafeConvertToInt64(reader.GetValue(3), 1);
                        long lastNum = reader.IsDBNull(4) ? 1 : SafeConvertToInt64(reader.GetValue(4), 1);

                        sequences.Add(new DbSequence
                        {
                            Name = name,
                            MinValue = minVal,
                            MaxValue = maxVal,
                            IncrementBy = increment,
                            LastNumber = lastNum
                        });
                    }
                }
            }
            return sequences;
        }

        public List<DbTrigger> LoadTriggers(string connStr)
        {
            var triggers = new List<DbTrigger>();
            using (var conn = new OracleConnection(connStr))
            {
                conn.Open();
                string trigSql = @"
                    SELECT TRIGGER_NAME, TABLE_NAME, TRIGGER_TYPE, TRIGGERING_EVENT, TRIGGER_BODY 
                    FROM USER_TRIGGERS 
                    ORDER BY TRIGGER_NAME";
                using (var cmd = new OracleCommand(trigSql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string name = reader.GetString(0);
                        string tableName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        string triggerType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        string eventType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                        string triggerBody = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);

                        triggers.Add(new DbTrigger
                        {
                            Name = name,
                            TableName = tableName,
                            TriggerType = triggerType,
                            TriggeringEvent = eventType,
                            TriggerBody = triggerBody
                        });
                    }
                }
            }
            return triggers;
        }

        public List<DbIndex> LoadIndexes(string connStr)
        {
            var indexes = new List<DbIndex>();
            var indexMap = new Dictionary<string, DbIndex>(StringComparer.OrdinalIgnoreCase);

            using (var conn = new OracleConnection(connStr))
            {
                conn.Open();
                string idxSql = @"
                    SELECT i.index_name, i.table_name, c.column_name, i.uniqueness
                    FROM user_indexes i
                    JOIN user_ind_columns c ON i.index_name = c.index_name AND i.table_name = c.table_name
                    LEFT JOIN user_constraints cons ON i.index_name = cons.constraint_name AND cons.constraint_type = 'P'
                    WHERE cons.constraint_name IS NULL
                    ORDER BY i.index_name, c.column_position";

                using (var cmd = new OracleCommand(idxSql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string indexName = reader.GetString(0);
                        string tableName = reader.GetString(1);
                        string columnName = reader.GetString(2);
                        bool isUnique = reader.GetString(3) == "UNIQUE";

                        if (!indexMap.TryGetValue(indexName, out var idx))
                        {
                            idx = new DbIndex
                            {
                                Name = indexName,
                                TableName = tableName,
                                IsUnique = isUnique,
                                ColumnNames = new List<string>()
                            };
                            indexes.Add(idx);
                            indexMap[indexName] = idx;
                        }
                        idx.ColumnNames.Add(columnName);
                      }
                  }
              }
              return indexes;
        }

        public Tuple<OracleConnection, OracleDataReader> GetTableDataReader(string connStr, string tableName)
        {
            var conn = new OracleConnection(connStr);
            try
            {
                conn.Open();
                var cmd = new OracleCommand($"SELECT * FROM \"{tableName}\"", conn);
                var reader = cmd.ExecuteReader(CommandBehavior.CloseConnection);
                return new Tuple<OracleConnection, OracleDataReader>(conn, reader);
            }
            catch
            {
                conn.Close();
                conn.Dispose();
                throw;
            }
        }
    }
}
