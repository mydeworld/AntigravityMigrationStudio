using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using MigrationPgSqlApp.Models;
using Npgsql;
using NpgsqlTypes;
using Oracle.ManagedDataAccess.Client;

namespace MigrationPgSqlApp.Services
{
    public class PostgresDbService
    {
        public string BuildConnectionString(string host, int port, string database, string userId, string password)
        {
            return $"Host={host};Port={port};Database={database};Username={userId};Password={password};Timeout=15;Command Timeout=30;";
        }

        public bool TestConnection(string connStr, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                using (var conn = new NpgsqlConnection(connStr))
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

        public void ExecuteSql(string connStr, string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return;

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void EnsureSchemaExists(string connStr, string schemaName)
        {
            if (string.IsNullOrEmpty(schemaName) || schemaName.Equals("public", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            ExecuteSql(connStr, $"CREATE SCHEMA IF NOT EXISTS \"{schemaName.ToLower()}\";");
        }

        public void CreateOracleCompatibilityHelperFunctions(string connStr, string targetSchema)
        {
            List<string> schemasToCreate = new List<string> { "public" };
            if (!string.IsNullOrEmpty(targetSchema) && !targetSchema.Equals("public", StringComparison.OrdinalIgnoreCase))
            {
                schemasToCreate.Add(targetSchema.ToLower());
            }

            foreach (var schema in schemasToCreate)
            {
                string sql = $@"
CREATE OR REPLACE FUNCTION ""{schema}"".to_number(val interval)
RETURNS numeric AS $$
BEGIN
    RETURN EXTRACT(epoch FROM val) / 86400.0;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".to_number(val text)
RETURNS numeric AS $$
BEGIN
    RETURN CAST(val AS numeric);
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".to_number(val numeric)
RETURNS numeric AS $$
BEGIN
    RETURN val;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".to_char(val bigint)
RETURNS text AS $$
BEGIN
    RETURN CAST(val AS text);
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".to_char(val numeric)
RETURNS text AS $$
BEGIN
    RETURN CAST(val AS text);
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".to_char(val double precision)
RETURNS text AS $$
BEGIN
    RETURN CAST(val AS text);
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".to_char(val integer)
RETURNS text AS $$
BEGIN
    RETURN CAST(val AS text);
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".to_char(val timestamp)
RETURNS text AS $$
BEGIN
    RETURN CAST(val AS text);
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".instr(str text, substr text)
RETURNS integer AS $$
DECLARE
    pos integer;
BEGIN
    pos := POSITION(substr IN str);
    RETURN pos;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".instr(str text, substr text, start_pos numeric)
RETURNS integer AS $$
DECLARE
    pos integer;
    str_len integer;
    abs_start integer;
    s_pos integer;
BEGIN
    s_pos := CAST(start_pos AS integer);
    IF s_pos > 0 THEN
        pos := POSITION(substr IN SUBSTRING(str FROM s_pos));
        IF pos > 0 THEN
            RETURN pos + s_pos - 1;
        ELSE
            RETURN 0;
        END IF;
    ELSIF s_pos < 0 THEN
        str_len := char_length(str);
        abs_start := str_len + s_pos + 1;
        IF abs_start <= 0 THEN
            RETURN 0;
        END IF;
        FOR i IN REVERSE abs_start .. 1 LOOP
            IF SUBSTRING(str FROM i FOR char_length(substr)) = substr THEN
                RETURN i;
            END IF;
        END LOOP;
        RETURN 0;
    ELSE
        RETURN 0;
    END IF;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".instr(str text, substr text, start_pos numeric, occurrence numeric)
RETURNS integer AS $$
DECLARE
    pos integer := 0;
    found_count integer := 0;
    str_len integer;
    abs_start integer;
    i integer;
    s_pos integer;
    occ integer;
BEGIN
    s_pos := CAST(start_pos AS integer);
    occ := CAST(occurrence AS integer);
    IF occ <= 0 THEN
        RETURN 0;
    END IF;
    
    str_len := char_length(str);
    
    IF s_pos > 0 THEN
        i := s_pos;
        WHILE i <= str_len LOOP
            IF SUBSTRING(str FROM i FOR char_length(substr)) = substr THEN
                found_count := found_count + 1;
                IF found_count = occ THEN
                    RETURN i;
                END IF;
            END IF;
            i := i + 1;
        END LOOP;
        RETURN 0;
    ELSIF s_pos < 0 THEN
        abs_start := str_len + s_pos + 1;
        IF abs_start <= 0 THEN
            RETURN 0;
        END IF;
        i := abs_start;
        WHILE i >= 1 LOOP
            IF SUBSTRING(str FROM i FOR char_length(substr)) = substr THEN
                found_count := found_count + 1;
                IF found_count = occ THEN
                    RETURN i;
                END IF;
            END IF;
            i := i - 1;
        END LOOP;
    END IF;
    RETURN 0;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".text_minus_int(t text, i integer) RETURNS numeric AS $$
BEGIN
    RETURN CAST(t AS numeric) - i;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".int_minus_text(i integer, t text) RETURNS numeric AS $$
BEGIN
    RETURN i - CAST(t AS numeric);
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".text_plus_int(t text, i integer) RETURNS numeric AS $$
BEGIN
    RETURN CAST(t AS numeric) + i;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".int_plus_text(i integer, t text) RETURNS numeric AS $$
BEGIN
    RETURN i + CAST(t AS numeric);
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".text_minus_num(t text, n numeric) RETURNS numeric AS $$
BEGIN
    RETURN CAST(t AS numeric) - n;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".num_minus_text(n numeric, t text) RETURNS numeric AS $$
BEGIN
    RETURN n - CAST(t AS numeric);
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".text_plus_num(t text, n numeric) RETURNS numeric AS $$
BEGIN
    RETURN CAST(t AS numeric) + n;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".num_plus_text(n numeric, t text) RETURNS numeric AS $$
BEGIN
    RETURN n + CAST(t AS numeric);
END;
$$ LANGUAGE plpgsql IMMUTABLE;

DROP OPERATOR IF EXISTS ""{schema}"".- (text, integer);
CREATE OPERATOR ""{schema}"".- (
    leftarg = text,
    rightarg = integer,
    procedure = ""{schema}"".text_minus_int
);

DROP OPERATOR IF EXISTS ""{schema}"".- (integer, text);
CREATE OPERATOR ""{schema}"".- (
    leftarg = integer,
    rightarg = text,
    procedure = ""{schema}"".int_minus_text
);

DROP OPERATOR IF EXISTS ""{schema}"".+ (text, integer);
CREATE OPERATOR ""{schema}"".+ (
    leftarg = text,
    rightarg = integer,
    procedure = ""{schema}"".text_plus_int
);

DROP OPERATOR IF EXISTS ""{schema}"".+ (integer, text);
CREATE OPERATOR ""{schema}"".+ (
    leftarg = integer,
    rightarg = text,
    procedure = ""{schema}"".int_plus_text
);

DROP OPERATOR IF EXISTS ""{schema}"".- (text, numeric);
CREATE OPERATOR ""{schema}"".- (
    leftarg = text,
    rightarg = numeric,
    procedure = ""{schema}"".text_minus_num
);

DROP OPERATOR IF EXISTS ""{schema}"".- (numeric, text);
CREATE OPERATOR ""{schema}"".- (
    leftarg = numeric,
    rightarg = text,
    procedure = ""{schema}"".num_minus_text
);

DROP OPERATOR IF EXISTS ""{schema}"".+ (text, numeric);
CREATE OPERATOR ""{schema}"".+ (
    leftarg = text,
    rightarg = numeric,
    procedure = ""{schema}"".text_plus_num
);

DROP OPERATOR IF EXISTS ""{schema}"".+ (numeric, text);
CREATE OPERATOR ""{schema}"".+ (
    leftarg = numeric,
    rightarg = text,
    procedure = ""{schema}"".num_plus_text
);

CREATE OR REPLACE FUNCTION ""{schema}"".text_eq_int(t text, i integer) RETURNS boolean AS $$
BEGIN
    RETURN CAST(t AS numeric) = i;
EXCEPTION WHEN others THEN
    RETURN false;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".int_eq_text(i integer, t text) RETURNS boolean AS $$
BEGIN
    RETURN i = CAST(t AS numeric);
EXCEPTION WHEN others THEN
    RETURN false;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".text_neq_int(t text, i integer) RETURNS boolean AS $$
BEGIN
    RETURN CAST(t AS numeric) <> i;
EXCEPTION WHEN others THEN
    RETURN true;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".int_neq_text(i integer, t text) RETURNS boolean AS $$
BEGIN
    RETURN i <> CAST(t AS numeric);
EXCEPTION WHEN others THEN
    RETURN true;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".text_eq_num(t text, n numeric) RETURNS boolean AS $$
BEGIN
    RETURN CAST(t AS numeric) = n;
EXCEPTION WHEN others THEN
    RETURN false;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".num_eq_text(n numeric, t text) RETURNS boolean AS $$
BEGIN
    RETURN n = CAST(t AS numeric);
EXCEPTION WHEN others THEN
    RETURN false;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".text_neq_num(t text, n numeric) RETURNS boolean AS $$
BEGIN
    RETURN CAST(t AS numeric) <> n;
EXCEPTION WHEN others THEN
    RETURN true;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ""{schema}"".num_neq_text(n numeric, t text) RETURNS boolean AS $$
BEGIN
    RETURN n <> CAST(t AS numeric);
EXCEPTION WHEN others THEN
    RETURN true;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

DROP OPERATOR IF EXISTS ""{schema}"".= (text, integer);
CREATE OPERATOR ""{schema}"".= (
    leftarg = text,
    rightarg = integer,
    procedure = ""{schema}"".text_eq_int
);

DROP OPERATOR IF EXISTS ""{schema}"".= (integer, text);
CREATE OPERATOR ""{schema}"".= (
    leftarg = integer,
    rightarg = text,
    procedure = ""{schema}"".int_eq_text
);

DROP OPERATOR IF EXISTS ""{schema}"".<> (text, integer);
CREATE OPERATOR ""{schema}"".<> (
    leftarg = text,
    rightarg = integer,
    procedure = ""{schema}"".text_neq_int
);

DROP OPERATOR IF EXISTS ""{schema}"".<> (integer, text);
CREATE OPERATOR ""{schema}"".<> (
    leftarg = integer,
    rightarg = text,
    procedure = ""{schema}"".int_neq_text
);

DROP OPERATOR IF EXISTS ""{schema}"".= (text, numeric);
CREATE OPERATOR ""{schema}"".= (
    leftarg = text,
    rightarg = numeric,
    procedure = ""{schema}"".text_eq_num
);

DROP OPERATOR IF EXISTS ""{schema}"".= (numeric, text);
CREATE OPERATOR ""{schema}"".= (
    leftarg = numeric,
    rightarg = text,
    procedure = ""{schema}"".num_eq_text
);

DROP OPERATOR IF EXISTS ""{schema}"".<> (text, numeric);
CREATE OPERATOR ""{schema}"".<> (
    leftarg = text,
    rightarg = numeric,
    procedure = ""{schema}"".text_neq_num
);

DROP OPERATOR IF EXISTS ""{schema}"".<> (numeric, text);
CREATE OPERATOR ""{schema}"".<> (
    leftarg = numeric,
    rightarg = text,
    procedure = ""{schema}"".num_neq_text
);
";
                try
                {
                    ExecuteSql(connStr, sql);
                }
                catch { }
            }
        }

        public long BulkCopyData(string pgConnStr, string targetSchema, string tableName, System.Data.IDataReader reader, Action<long> progressCallback)
        {
            long rowCount = 0;
            string schemaPrefix = string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema.ToLower()}\".";
            string pgTableNameEscaped = $"\"{tableName.ToLower()}\"";

            // Build list of columns from Reader to ensure exact matching order
            List<string> columns = new List<string>();
            int fieldCount = reader.FieldCount;
            for (int i = 0; i < fieldCount; i++)
            {
                columns.Add($"\"{reader.GetName(i).ToLower()}\"");
            }
            string columnsListCsv = string.Join(", ", columns);

            using (var conn = new NpgsqlConnection(pgConnStr))
            {
                conn.Open();

                string copyQuery = $"COPY {schemaPrefix}{pgTableNameEscaped} ({columnsListCsv}) FROM STDIN (FORMAT CSV, HEADER FALSE, NULL '', ENCODING 'UTF8')";
                using (var writer = conn.BeginTextImport(copyQuery))
                {
                    while (reader.Read())
                    {
                        List<string> fields = new List<string>();
                        for (int i = 0; i < fieldCount; i++)
                        {
                            fields.Add(FormatCsvField(reader.GetValue(i)));
                        }
                        writer.WriteLine(string.Join(",", fields));
                        rowCount++;

                        if (rowCount % 1000 == 0)
                        {
                            progressCallback?.Invoke(rowCount);
                        }
                    }
                }
            }

            progressCallback?.Invoke(rowCount);
            return rowCount;
        }

        private string FormatCsvField(object val)
        {
            if (val == null || val is DBNull)
            {
                return "";
            }

            if (val is string str)
            {
                return $"\"{str.Replace("\"", "\"\"")}\"";
            }

            if (val is DateTime dt)
            {
                if (dt < new DateTime(1, 1, 1) || dt > new DateTime(9999, 12, 31))
                {
                    return "";
                }
                return $"\"{dt:yyyy-MM-dd HH:mm:ss.ffffff}\"";
            }

            if (val is byte[] bytes)
            {
                return $"\"\\x{BitConverter.ToString(bytes).Replace("-", "").ToLower()}\"";
            }

            if (val is bool b)
            {
                return b ? "TRUE" : "FALSE";
            }

            string sVal = val.ToString() ?? "";
            if (val is IFormattable formattable)
            {
                sVal = formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
            }

            if (sVal.Contains(",") || sVal.Contains("\"") || sVal.Contains("\n") || sVal.Contains("\r"))
            {
                return $"\"{sVal.Replace("\"", "\"\"")}\"";
            }
            return sVal;
        }

        public void TruncateTable(string connStr, string targetSchema, string tableName)
        {
            string schemaPrefix = string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema.ToLower()}\".";
            string pgTableNameEscaped = $"\"{tableName.ToLower()}\"";
            ExecuteSql(connStr, $"TRUNCATE TABLE {schemaPrefix}{pgTableNameEscaped} RESTART IDENTITY CASCADE;");
        }

        // =========================================================================
        // PostgreSQL Source Database Metadata Extraction Methods
        // =========================================================================

        public List<DbTable> LoadTables(string connStr, string schemaName)
        {
            var tables = new List<DbTable>();
            string schema = string.IsNullOrEmpty(schemaName) ? "public" : schemaName.ToLower();

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();

                // 1. Get Table List and Row Counts
                string sqlTables = @"
                    SELECT t.table_name, COALESCE(s.n_live_tup, 0) AS row_count
                    FROM information_schema.tables t
                    LEFT JOIN pg_stat_user_tables s ON s.schemaname = t.table_schema AND s.relname = t.table_name
                    WHERE t.table_schema = @schema AND t.table_type = 'BASE TABLE'
                    ORDER BY t.table_name;";

                using (var cmd = new NpgsqlCommand(sqlTables, conn))
                {
                    cmd.Parameters.AddWithValue("schema", schema);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tables.Add(new DbTable
                            {
                                Name = reader.GetString(0),
                                RowCount = reader.GetInt64(1)
                            });
                        }
                    }
                }

                // 2. Fetch Columns for each table
                foreach (var table in tables)
                {
                    string sqlCols = @"
                        SELECT column_name, data_type, udt_name, 
                               character_maximum_length, numeric_precision, numeric_scale, 
                               is_nullable, column_default
                        FROM information_schema.columns
                        WHERE table_schema = @schema AND table_name = @tableName
                        ORDER BY ordinal_position;";

                    using (var cmd = new NpgsqlCommand(sqlCols, conn))
                    {
                        cmd.Parameters.AddWithValue("schema", schema);
                        cmd.Parameters.AddWithValue("tableName", table.Name);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string colName = reader.GetString(0);
                                string dataType = reader.GetString(1);
                                string udtName = reader.GetString(2);
                                long length = reader.IsDBNull(3) ? 0 : Convert.ToInt64(reader.GetValue(3));
                                int? precision = reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4));
                                int? scale = reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5));
                                bool isNullable = reader.GetString(6).Equals("YES", StringComparison.OrdinalIgnoreCase);
                                string? defaultVal = reader.IsDBNull(7) ? null : reader.GetString(7);

                                string fullPgType = dataType;
                                if (dataType.Equals("character varying", StringComparison.OrdinalIgnoreCase) || dataType.Equals("varchar", StringComparison.OrdinalIgnoreCase))
                                {
                                    fullPgType = length > 0 ? $"varchar({length})" : "varchar";
                                }
                                else if (dataType.Equals("character", StringComparison.OrdinalIgnoreCase) || dataType.Equals("char", StringComparison.OrdinalIgnoreCase))
                                {
                                    fullPgType = length > 0 ? $"char({length})" : "char";
                                }
                                else if (dataType.Equals("numeric", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (precision.HasValue && scale.HasValue && scale.Value > 0)
                                        fullPgType = $"numeric({precision.Value},{scale.Value})";
                                    else if (precision.HasValue)
                                        fullPgType = $"numeric({precision.Value},0)";
                                    else
                                        fullPgType = "numeric";
                                }

                                table.Columns.Add(new ColumnInfo
                                {
                                    Name = colName,
                                    OracleType = udtName,
                                    PgType = fullPgType,
                                    Length = length,
                                    Precision = precision,
                                    Scale = scale,
                                    IsNullable = isNullable,
                                    DefaultValue = defaultVal
                                });
                            }
                        }
                    }

                    // 3. Fetch Primary Key
                    string sqlPk = @"
                        SELECT kcu.column_name, tc.constraint_name
                        FROM information_schema.table_constraints tc
                        JOIN information_schema.key_column_usage kcu 
                          ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                        WHERE tc.constraint_type = 'PRIMARY KEY' 
                          AND tc.table_schema = @schema 
                          AND tc.table_name = @tableName
                        ORDER BY kcu.ordinal_position;";

                    using (var cmd = new NpgsqlCommand(sqlPk, conn))
                    {
                        cmd.Parameters.AddWithValue("schema", schema);
                        cmd.Parameters.AddWithValue("tableName", table.Name);
                        using (var reader = cmd.ExecuteReader())
                        {
                            List<string> pkCols = new List<string>();
                            string pkName = "";
                            while (reader.Read())
                            {
                                string colName = reader.GetString(0);
                                pkName = reader.GetString(1);
                                pkCols.Add(colName);
                                var col = table.Columns.FirstOrDefault(c => c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
                                if (col != null) col.IsPrimaryKey = true;
                            }
                            if (pkCols.Count > 0)
                            {
                                table.PrimaryKey = new PrimaryKeyInfo { ConstraintName = pkName, ColumnNames = pkCols };
                            }
                        }
                    }

                    // 4. Fetch Foreign Keys
                    string sqlFk = @"
                        SELECT tc.constraint_name, kcu.column_name, ccu.table_name AS ref_table, ccu.column_name AS ref_column
                        FROM information_schema.table_constraints tc
                        JOIN information_schema.key_column_usage kcu ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                        JOIN information_schema.constraint_column_usage ccu ON ccu.constraint_name = tc.constraint_name AND ccu.table_schema = tc.table_schema
                        WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_schema = @schema AND tc.table_name = @tableName;";

                    using (var cmd = new NpgsqlCommand(sqlFk, conn))
                    {
                        cmd.Parameters.AddWithValue("schema", schema);
                        cmd.Parameters.AddWithValue("tableName", table.Name);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                table.ForeignKeys.Add(new ForeignKeyInfo
                                {
                                    ConstraintName = reader.GetString(0),
                                    ColumnName = reader.GetString(1),
                                    ReferenceTable = reader.GetString(2),
                                    ReferenceColumn = reader.GetString(3)
                                });
                            }
                        }
                    }
                }
            }
            return tables;
        }

        public List<DbView> LoadViews(string connStr, string schemaName)
        {
            var views = new List<DbView>();
            string schema = string.IsNullOrEmpty(schemaName) ? "public" : schemaName.ToLower();

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                string sql = @"
                    SELECT table_name, view_definition
                    FROM information_schema.views
                    WHERE table_schema = @schema
                    ORDER BY table_name;";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("schema", schema);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string viewName = reader.GetString(0);
                            string viewDef = reader.IsDBNull(1) ? "" : reader.GetString(1);

                            views.Add(new DbView
                            {
                                Name = viewName,
                                Definition = viewDef,
                                ConvertedDefinition = $"CREATE OR REPLACE VIEW \"{viewName.ToLower()}\" AS\n{viewDef};"
                            });
                        }
                    }
                }
            }
            return views;
        }

        public List<DbProcedure> LoadProcedures(string connStr, string schemaName)
        {
            var procs = new List<DbProcedure>();
            string schema = string.IsNullOrEmpty(schemaName) ? "public" : schemaName.ToLower();

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                string sql = @"
                    SELECT p.proname,
                           pg_get_functiondef(p.oid) AS func_def,
                           CASE WHEN p.prokind = 'p' THEN 'PROCEDURE' ELSE 'FUNCTION' END AS obj_type
                    FROM pg_proc p
                    JOIN pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = @schema
                      AND p.prokind IN ('f', 'p')
                    ORDER BY p.proname;";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("schema", schema);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string name = reader.GetString(0);
                            string def = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            string type = reader.GetString(2);

                            procs.Add(new DbProcedure
                            {
                                Name = name,
                                ObjectType = type,
                                SourceCode = def,
                                ConvertedSourceCode = def
                            });
                        }
                    }
                }
            }
            return procs;
        }

        public List<DbSequence> LoadSequences(string connStr, string schemaName)
        {
            var seqs = new List<DbSequence>();
            string schema = string.IsNullOrEmpty(schemaName) ? "public" : schemaName.ToLower();

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                string sql = @"
                    SELECT sequence_name, minimum_value, maximum_value, increment
                    FROM information_schema.sequences
                    WHERE sequence_schema = @schema
                    ORDER BY sequence_name;";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("schema", schema);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string name = reader.GetString(0);
                            long min = Convert.ToInt64(reader.GetValue(1));
                            long max = Convert.ToInt64(reader.GetValue(2));
                            long inc = Convert.ToInt64(reader.GetValue(3));

                            seqs.Add(new DbSequence
                            {
                                Name = name,
                                MinValue = min,
                                MaxValue = max,
                                IncrementBy = inc,
                                LastNumber = 1,
                                ConvertedDdl = $"CREATE SEQUENCE \"{name.ToLower()}\" START WITH 1 INCREMENT BY {inc} MINVALUE {min} MAXVALUE {max};"
                            });
                        }
                    }
                }
            }
            return seqs;
        }

        public List<DbIndex> LoadIndexes(string connStr, string schemaName)
        {
            var idxs = new List<DbIndex>();
            string schema = string.IsNullOrEmpty(schemaName) ? "public" : schemaName.ToLower();

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                string sql = @"
                    SELECT indexname, tablename, indexdef
                    FROM pg_indexes
                    WHERE schemaname = @schema AND indexname NOT LIKE '%_pkey'
                    ORDER BY indexname;";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("schema", schema);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string idxName = reader.GetString(0);
                            string tableName = reader.GetString(1);
                            string def = reader.GetString(2);

                            idxs.Add(new DbIndex
                            {
                                Name = idxName,
                                TableName = tableName,
                                ConvertedDdl = def.EndsWith(";") ? def : def + ";"
                            });
                        }
                    }
                }
            }
            return idxs;
        }

        public List<DbTrigger> LoadTriggers(string connStr, string schemaName)
        {
            var trigs = new List<DbTrigger>();
            string schema = string.IsNullOrEmpty(schemaName) ? "public" : schemaName.ToLower();

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                string sql = @"
                    SELECT trigger_name, event_object_table, action_timing, event_manipulation, action_statement
                    FROM information_schema.triggers
                    WHERE trigger_schema = @schema
                    ORDER BY trigger_name;";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("schema", schema);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string trigName = reader.GetString(0);
                            string tblName = reader.GetString(1);
                            string timing = reader.GetString(2);
                            string eventManip = reader.GetString(3);
                            string stmt = reader.GetString(4);

                            trigs.Add(new DbTrigger
                            {
                                Name = trigName,
                                TableName = tblName,
                                TriggerType = timing,
                                TriggeringEvent = eventManip,
                                TriggerBody = stmt,
                                ConvertedTriggerBody = stmt
                            });
                        }
                    }
                }
            }
            return trigs;
        }

        public Tuple<NpgsqlConnection, NpgsqlDataReader> GetTableDataReader(string connStr, string schemaName, string tableName)
        {
            string schemaPrefix = string.IsNullOrEmpty(schemaName) ? "" : $"\"{schemaName.ToLower()}\".";
            var conn = new NpgsqlConnection(connStr);
            conn.Open();
            var cmd = new NpgsqlCommand($"SELECT * FROM {schemaPrefix}\"{tableName.ToLower()}\"", conn);
            var reader = cmd.ExecuteReader(CommandBehavior.CloseConnection);
            return new Tuple<NpgsqlConnection, NpgsqlDataReader>(conn, reader);
        }
    }
}
