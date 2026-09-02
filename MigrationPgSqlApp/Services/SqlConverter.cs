using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MigrationPgSqlApp.Models;

namespace MigrationPgSqlApp.Services
{
    public class SqlConverter
    {
        public string MapOracleToPgType(string oracleType, long length, int? precision, int? scale)
        {
            string type = oracleType.ToUpper().Trim();

            if (type.Contains("VARCHAR2") || type.Contains("NVARCHAR2"))
            {
                return length > 0 ? $"varchar({length})" : "varchar";
            }
            if (type == "CHAR" || type == "NCHAR")
            {
                return length > 0 ? $"char({length})" : "char";
            }
            if (type == "NUMBER")
            {
                if (scale.HasValue && scale.Value > 0)
                {
                    return precision.HasValue ? $"numeric({precision.Value},{scale.Value})" : "numeric";
                }
                else
                {
                    if (precision.HasValue)
                    {
                        if (precision.Value <= 4) return "smallint";
                        if (precision.Value <= 9) return "integer";
                        if (precision.Value <= 18) return "bigint";
                        return $"numeric({precision.Value},0)";
                    }
                    return "numeric";
                }
            }
            if (type == "FLOAT")
            {
                return "double precision";
            }
            if (type == "DATE" || type.Contains("TIMESTAMP"))
            {
                return "timestamp";
            }
            if (type == "CLOB")
            {
                return "text";
            }
            if (type == "BLOB" || type == "RAW" || type == "LONG RAW")
            {
                return "bytea";
            }

            return "varchar";
        }

        public string ConvertSqlText(string sql)
        {
            return ConvertSqlText(sql, null);
        }

        public string ConvertSqlText(string sql, ISet<string> extraNonVarcharNames)
        {
            if (string.IsNullOrEmpty(sql)) return string.Empty;

            var nonVarcharNames = ExtractNonVarcharNames(sql);
            if (extraNonVarcharNames != null)
            {
                foreach (var name in extraNonVarcharNames)
                {
                    nonVarcharNames.Add(name);
                }
            }

            // 0. Replace full-width Chinese commas with half-width English commas
            sql = sql.Replace("，", ",");

            // 1. Replace SYSDATE / SYSDATE() with CURRENT_TIMESTAMP
            sql = Regex.Replace(sql, @"\bsysdate\s*\(\s*\)", "CURRENT_TIMESTAMP", RegexOptions.IgnoreCase);
            sql = Regex.Replace(sql, @"\bsysdate\b", "CURRENT_TIMESTAMP", RegexOptions.IgnoreCase);

            // 1.0 Convert date subtraction expressions (e.g. TO_DATE(expr, fmt) - SYSDATE) to EXTRACT(EPOCH FROM (expr::timestamp - CURRENT_DATE)) / 86400
            sql = ConvertDateSubtraction(sql);

            // 1.1 Replace SYS_GUID() with upper(replace(gen_random_uuid()::text, '-', ''))
            sql = Regex.Replace(sql, @"\bsys_guid\s*\(\s*\)", "upper(replace(gen_random_uuid()::text, '-', ''))", RegexOptions.IgnoreCase);

            // 2. Replace NVL(a, b) with COALESCE(a, b)
            sql = Regex.Replace(sql, @"\bnvl\s*\(", "COALESCE(", RegexOptions.IgnoreCase);

            // 3. Remove FROM DUAL (replace with spaces to maintain length/offsets if needed)
            sql = Regex.Replace(sql, @"\bfrom\s+dual\b", "", RegexOptions.IgnoreCase);

            // 4. Translate INSTR(str, substr) to POSITION(substr IN str)
            sql = ConvertInstr(sql);

            // 5. Translate SUBSTR(str, pos, len) to SUBSTRING(str FROM pos FOR len)
            sql = ConvertSubstr(sql);

            // 5.1 Translate single-arg TO_NUMBER(expr) to (expr)::numeric
            sql = ConvertToNumber(sql);

            // 6. DECODE replacement (simplistic, for common patterns)
            sql = ConvertDecodeToCase(sql);

            // 7. Add FROM to DELETE statements: DELETE table_name -> DELETE FROM table_name
            sql = Regex.Replace(sql, @"\bdelete\s+(?!from\b)(?<tbl>""?\w+""?)", "DELETE FROM ${tbl}", RegexOptions.IgnoreCase);

            // 8. Convert Oracle START WITH ... CONNECT BY PRIOR to PostgreSQL WITH RECURSIVE CTE
            string connectByPattern = @"\bSELECT\s+(?<cols>[\w,\s*]+)\s+FROM\s+(?<tbl>\w+)\s+START\s+WITH\s+(?<startCol>\w+)\s*=\s*(?<startVal>[\w:]+)\s+CONNECT\s+BY\s+(?:PRIOR\s+(?<childCol>\w+)\s*=\s*(?<parentCol>\w+)|(?<parentCol>\w+)\s*=\s*PRIOR\s+(?<childCol>\w+))";
            sql = Regex.Replace(sql, connectByPattern, 
                @"WITH RECURSIVE cte_hier AS (
                    SELECT ${cols} FROM ${tbl} WHERE ${startCol} = ${startVal}
                    UNION ALL
                    SELECT t.${cols} FROM ${tbl} t
                    JOIN cte_hier ON cte_hier.${childCol} = t.${parentCol}
                ) SELECT ${cols} FROM cte_hier", RegexOptions.IgnoreCase);

            // 9. Convert Oracle CURSOR cursor_name IS query to PostgreSQL cursor_name CURSOR FOR query
            sql = Regex.Replace(sql, @"\bCURSOR\s+(?<name>\w+)(?:\s*\((?<args>[^)]+)\))?\s+IS\b", 
                m => {
                    var name = m.Groups["name"].Value;
                    var args = m.Groups["args"].Value;
                    if (!string.IsNullOrEmpty(args))
                        return $"{name} CURSOR({args}) FOR";
                    else
                        return $"{name} CURSOR FOR";
                }, RegexOptions.IgnoreCase);

            // 10. Automatically split numbers followed directly by a keyword (e.g. 1WHERE -> 1 WHERE)
            // Skip matches inside single-quoted string literals
            sql = Regex.Replace(sql, @"'[^']*'|(\b(?<num>\d+)(?<word>[a-zA-Z_]\w*)\b)", m => {
                if (m.Value.StartsWith("'"))
                {
                    return m.Value;
                }
                return m.Groups["num"].Value + " " + m.Groups["word"].Value;
            }, RegexOptions.IgnoreCase);

            sql = Regex.Replace(sql, @"\bEXECUTE\s+IMMEDIATE\b", "EXECUTE", RegexOptions.IgnoreCase);

            // 11. Convert A IS NULL / A IS NOT NULL to A IS NULL OR A = '' / A IS NOT NULL AND A <> ''
            // Skip matches inside single-quoted string literals
            // Note: If parameter/variable is NOT a varchar type (e.g. integer, numeric, date, timestamp, refcursor, boolean, etc.),
            // compare with empty string '' will cause PostgreSQL syntax error. So only convert to (expr IS NULL) / (expr IS NOT NULL).
            sql = Regex.Replace(sql, @"'[^']*'|(\b(?<expr>[a-zA-Z0-9_.]+(?:\([^)]*\))?)\s+IS\s+NOT\s+NULL\b)", m => {
                if (m.Value.StartsWith("'")) return m.Value;
                string expr = m.Groups["expr"].Value;
                if (IsNonVarcharExpr(expr, nonVarcharNames))
                {
                    return $"({expr} IS NOT NULL)";
                }
                return $"({expr} IS NOT NULL AND {expr} <> '')";
            }, RegexOptions.IgnoreCase);

            sql = Regex.Replace(sql, @"'[^']*'|(\b(?<expr>[a-zA-Z0-9_.]+(?:\([^)]*\))?)\s+IS\s+NULL\b)", m => {
                if (m.Value.StartsWith("'")) return m.Value;
                string expr = m.Groups["expr"].Value;
                if (IsNonVarcharExpr(expr, nonVarcharNames))
                {
                    return $"({expr} IS NULL)";
                }
                return $"({expr} IS NULL OR {expr} = '')";
            }, RegexOptions.IgnoreCase);

            // Clean up any existing (expr IS NULL OR expr = '') or (expr IS NOT NULL AND expr <> '') for non-varchar expressions
            sql = Regex.Replace(sql, @"'[^']*'|(\(\s*(?<expr>[a-zA-Z0-9_.]+)\s+IS\s+NULL\s+OR\s+\k<expr>\s*=\s*''\s*\))", m => {
                if (m.Value.StartsWith("'")) return m.Value;
                string expr = m.Groups["expr"].Value;
                if (IsNonVarcharExpr(expr, nonVarcharNames))
                {
                    return $"({expr} IS NULL)";
                }
                return m.Value;
            }, RegexOptions.IgnoreCase);

            sql = Regex.Replace(sql, @"'[^']*'|(\(\s*(?<expr>[a-zA-Z0-9_.]+)\s*=\s*''\s+OR\s+\k<expr>\s+IS\s+NULL\s*\))", m => {
                if (m.Value.StartsWith("'")) return m.Value;
                string expr = m.Groups["expr"].Value;
                if (IsNonVarcharExpr(expr, nonVarcharNames))
                {
                    return $"({expr} IS NULL)";
                }
                return m.Value;
            }, RegexOptions.IgnoreCase);

            sql = Regex.Replace(sql, @"'[^']*'|(\(\s*(?<expr>[a-zA-Z0-9_.]+)\s+IS\s+NOT\s+NULL\s+AND\s+\k<expr>\s*<>\s*''\s*\))", m => {
                if (m.Value.StartsWith("'")) return m.Value;
                string expr = m.Groups["expr"].Value;
                if (IsNonVarcharExpr(expr, nonVarcharNames))
                {
                    return $"({expr} IS NOT NULL)";
                }
                return m.Value;
            }, RegexOptions.IgnoreCase);

            // 12. Convert LIKE patterns with concatenations to use COALESCE to prevent NULL comparisons returning empty datasets in PG
            // Pattern 1: LIKE '%' || expr || '%'
            sql = Regex.Replace(sql, @"\blike\s+'%'\s*\|\|\s*(?<expr>[a-zA-Z0-9_.]+(?:\([^)]*\))?)\s*\|\|\s*'%'", m => {
                string expr = m.Groups["expr"].Value;
                if (expr.ToUpper().StartsWith("COALESCE")) return m.Value;
                return $"LIKE '%' || COALESCE({expr}::text, '') || '%'";
            }, RegexOptions.IgnoreCase);

            // Pattern 2: LIKE expr || '%'
            sql = Regex.Replace(sql, @"\blike\s+(?<expr>[a-zA-Z0-9_.]+(?:\([^)]*\))?)\s*\|\|\s*'%'", m => {
                string expr = m.Groups["expr"].Value;
                if (expr.ToUpper().StartsWith("COALESCE")) return m.Value;
                return $"LIKE COALESCE({expr}::text, '') || '%'";
            }, RegexOptions.IgnoreCase);

            // Pattern 3: LIKE '%' || expr
            sql = Regex.Replace(sql, @"\blike\s+'%'\s*\|\|\s*(?<expr>[a-zA-Z0-9_.]+(?:\([^)]*\))?)\b", m => {
                string expr = m.Groups["expr"].Value;
                if (expr.ToUpper().StartsWith("COALESCE")) return m.Value;
                return $"LIKE '%' || COALESCE({expr}::text, '')";
            }, RegexOptions.IgnoreCase);

            sql = ConvertRownum(sql);

            return sql;
        }

        private string ConvertRownum(string sql)
        {
            var replacements = new List<(int Index, int Length, int Limit)>();

            // Pattern 1: AND ROWNUM <= N or AND ROWNUM = N
            var andMatch = Regex.Matches(sql, @"\b(?:AND|OR)\s+ROWNUM\s*(?:<=|=)\s*(?<limit>\d+)\b", RegexOptions.IgnoreCase);
            foreach (Match m in andMatch)
            {
                replacements.Add((m.Index, m.Length, int.Parse(m.Groups["limit"].Value)));
            }

            // Pattern 2: ROWNUM <= N AND or ROWNUM = N AND
            var andMatch2 = Regex.Matches(sql, @"\bROWNUM\s*(?:<=|=)\s*(?<limit>\d+)\s+(?:AND|OR)\b", RegexOptions.IgnoreCase);
            foreach (Match m in andMatch2)
            {
                replacements.Add((m.Index, m.Length, int.Parse(m.Groups["limit"].Value)));
            }

            // Pattern 3: WHERE ROWNUM <= N or WHERE ROWNUM = N
            var whereMatch = Regex.Matches(sql, @"\bWHERE\s+ROWNUM\s*(?:<=|=)\s*(?<limit>\d+)\b", RegexOptions.IgnoreCase);
            foreach (Match m in whereMatch)
            {
                replacements.Add((m.Index, m.Length, int.Parse(m.Groups["limit"].Value)));
            }

            // Sort replacements by Index descending to avoid index shifting
            var sortedReplacements = replacements.OrderByDescending(r => r.Index).ToList();

            foreach (var r in sortedReplacements)
            {
                sql = sql.Remove(r.Index, r.Length);
                
                // Append LIMIT before the next semicolon ';' or closing parenthesis ')'
                int endIdx = sql.IndexOf(';', r.Index);
                int closeParenIdx = sql.IndexOf(')', r.Index);
                
                int targetInsertIdx = -1;
                if (endIdx != -1 && closeParenIdx != -1)
                {
                    targetInsertIdx = Math.Min(endIdx, closeParenIdx);
                }
                else if (endIdx != -1)
                {
                    targetInsertIdx = endIdx;
                }
                else if (closeParenIdx != -1)
                {
                    targetInsertIdx = closeParenIdx;
                }

                if (targetInsertIdx != -1)
                {
                    sql = sql.Insert(targetInsertIdx, $" LIMIT {r.Limit}");
                }
            }

            // Pattern 4: Convert plain ROWNUM to ROW_NUMBER() OVER ()
            sql = Regex.Replace(sql, @"\bROWNUM\b", "ROW_NUMBER() OVER ()", RegexOptions.IgnoreCase);

            return sql;
        }

        private string ConvertSubstr(string sql)
        {
            string pattern = @"\bsubstr\s*\(";
            int searchPos = 0;
            while (searchPos < sql.Length)
            {
                var match = Regex.Match(sql.Substring(searchPos), pattern, RegexOptions.IgnoreCase);
                if (!match.Success) break;

                int startIdx = searchPos + match.Index;
                int openParenIdx = startIdx + match.Length - 1;
                int endIdx = FindClosingParenthesis(sql, openParenIdx);
                if (endIdx == -1)
                {
                    searchPos = openParenIdx + 1;
                    continue;
                }

                string argsStr = sql.Substring(openParenIdx + 1, endIdx - openParenIdx - 1);
                var args = SplitSqlArgs(argsStr);

                string replacement;
                if (args.Count == 3)
                {
                    replacement = $"SUBSTRING({args[0].Trim()} FROM {args[1].Trim()} FOR {args[2].Trim()})";
                }
                else if (args.Count == 2)
                {
                    replacement = $"SUBSTRING({args[0].Trim()} FROM {args[1].Trim()})";
                }
                else
                {
                    replacement = $"SUBSTRING({argsStr})";
                }

                sql = sql.Remove(startIdx, endIdx - startIdx + 1).Insert(startIdx, replacement);
                searchPos = startIdx + replacement.Length;
            }
            return sql;
        }

        private string ConvertInstr(string sql)
        {
            string pattern = @"\binstr\s*\(";
            int searchPos = 0;
            while (searchPos < sql.Length)
            {
                var match = Regex.Match(sql.Substring(searchPos), pattern, RegexOptions.IgnoreCase);
                if (!match.Success) break;

                int startIdx = searchPos + match.Index;
                int openParenIdx = startIdx + match.Length - 1;
                int endIdx = FindClosingParenthesis(sql, openParenIdx);
                if (endIdx == -1)
                {
                    searchPos = openParenIdx + 1;
                    continue;
                }

                string argsStr = sql.Substring(openParenIdx + 1, endIdx - openParenIdx - 1);
                var args = SplitSqlArgs(argsStr);

                string replacement;
                if (args.Count >= 2)
                {
                    replacement = $"POSITION({args[1].Trim()} IN {args[0].Trim()})";
                }
                else
                {
                    replacement = $"POSITION({argsStr})";
                }

                sql = sql.Remove(startIdx, endIdx - startIdx + 1).Insert(startIdx, replacement);
                searchPos = startIdx + replacement.Length;
            }
            return sql;
        }

        private string ConvertToNumber(string sql)
        {
            string pattern = @"\bto_number\s*\(";
            int searchPos = 0;
            while (searchPos < sql.Length)
            {
                var match = Regex.Match(sql.Substring(searchPos), pattern, RegexOptions.IgnoreCase);
                if (!match.Success) break;

                int startIdx = searchPos + match.Index;
                int openParenIdx = startIdx + match.Length - 1;
                int endIdx = FindClosingParenthesis(sql, openParenIdx);
                if (endIdx == -1)
                {
                    searchPos = openParenIdx + 1;
                    continue;
                }

                string argsStr = sql.Substring(openParenIdx + 1, endIdx - openParenIdx - 1);
                var args = SplitSqlArgs(argsStr);

                if (args.Count == 1)
                {
                    string replacement = $"({args[0].Trim()})::numeric";
                    sql = sql.Remove(startIdx, endIdx - startIdx + 1).Insert(startIdx, replacement);
                    searchPos = startIdx + replacement.Length;
                }
                else
                {
                    searchPos = endIdx + 1;
                }
            }
            return sql;
        }

        private string ConvertDecodeToCase(string sql)
        {
            // Regex to find decode(expr, search, result, ..., default)
            // Since decode can have variable arguments, we use a simple loop parser for safety
            string pattern = @"\bdecode\s*\(";
            while (true)
            {
                var match = Regex.Match(sql, pattern, RegexOptions.IgnoreCase);
                if (!match.Success) break;

                int startIdx = match.Index;
                int endIdx = FindClosingParenthesis(sql, startIdx + match.Length - 1);
                if (endIdx == -1) break;

                string argsStr = sql.Substring(startIdx + match.Length, endIdx - (startIdx + match.Length));
                string caseExpression = TranslateDecodeArgs(argsStr);

                sql = sql.Remove(startIdx, endIdx - startIdx + 1).Insert(startIdx, caseExpression);
            }
            return sql;
        }

        private int FindClosingParenthesis(string text, int openPos)
        {
            int closePos = openPos;
            int counter = 1;
            bool inQuotes = false;
            char quoteChar = '\0';

            while (counter > 0 && closePos < text.Length - 1)
            {
                closePos++;
                char c = text[closePos];
                if (inQuotes)
                {
                    if (c == quoteChar)
                    {
                        if (closePos + 1 < text.Length && text[closePos + 1] == quoteChar)
                        {
                            closePos++; // Skip escaped quote
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                }
                else
                {
                    if (c == '\'' || c == '"')
                    {
                        inQuotes = true;
                        quoteChar = c;
                    }
                    else if (c == '(') counter++;
                    else if (c == ')') counter--;
                }
            }
            return counter == 0 ? closePos : -1;
        }

        private string TranslateDecodeArgs(string argsStr)
        {
            // Split arguments by comma, respecting parentheses (nested functions)
            List<string> args = SplitSqlArgs(argsStr);
            if (args.Count < 3) return $"COALESCE({argsStr})"; // Fail safe

            string expr = args[0].Trim();
            StringBuilder sb = new StringBuilder();
            sb.Append("CASE ").Append(expr).Append(" ");

            int i = 1;
            for (; i < args.Count - 1; i += 2)
            {
                string search = args[i].Trim();
                string result = args[i + 1].Trim();
                sb.Append($"WHEN {search} THEN {result} ");
            }

            // If there's an odd number of arguments, the last one is the default else
            if (i < args.Count)
            {
                string defaultVal = args[i].Trim();
                sb.Append($"ELSE {defaultVal} ");
            }
            sb.Append("END");

            return sb.ToString();
        }

        private List<string> SplitSqlArgs(string argsStr)
        {
            List<string> args = new List<string>();
            StringBuilder current = new StringBuilder();
            int parenDepth = 0;
            bool inQuotes = false;
            char quoteChar = '\0';

            for (int idx = 0; idx < argsStr.Length; idx++)
            {
                char c = argsStr[idx];
                if (inQuotes)
                {
                    current.Append(c);
                    if (c == quoteChar)
                    {
                        // Check for escaped quote ('' in SQL)
                        if (idx + 1 < argsStr.Length && argsStr[idx + 1] == quoteChar)
                        {
                            current.Append(argsStr[idx + 1]);
                            idx++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                }
                else
                {
                    if (c == '\'' || c == '"')
                    {
                        inQuotes = true;
                        quoteChar = c;
                        current.Append(c);
                    }
                    else if (c == '(')
                    {
                        parenDepth++;
                        current.Append(c);
                    }
                    else if (c == ')')
                    {
                        parenDepth--;
                        current.Append(c);
                    }
                    else if (c == ',' && parenDepth == 0)
                    {
                        args.Add(current.ToString());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
            }
            args.Add(current.ToString());
            return args;
        }

        public string GenerateTableDdl(DbTable table, string targetSchema)
        {
            StringBuilder sb = new StringBuilder();
            string schemaPrefix = string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema}\".";
            string tableNameEscaped = $"\"{table.Name.ToLower()}\"";

            sb.AppendLine($"CREATE TABLE {schemaPrefix}{tableNameEscaped} (");

            List<string> colDefs = new List<string>();
            foreach (var col in table.Columns)
            {
                string colNameEscaped = $"\"{col.Name.ToLower()}\"";
                string colDef = $"    {colNameEscaped} {col.PgType}";

                if (!col.IsNullable)
                {
                    colDef += " NOT NULL";
                }

                if (!string.IsNullOrEmpty(col.DefaultValue))
                {
                    // Clean Oracle default value (e.g. remove sysdate/dual or clean spaces)
                    string def = ConvertSqlText(col.DefaultValue).Trim();
                    if (def.EndsWith("\n") || def.EndsWith("\r")) def = def.Trim();
                    colDef += $" DEFAULT {def}";
                }

                colDefs.Add(colDef);
            }

            // Primary Key constraint inside table definition
            if (table.PrimaryKey != null && table.PrimaryKey.ColumnNames.Count > 0)
            {
                List<string> pkCols = new List<string>();
                foreach (var c in table.PrimaryKey.ColumnNames)
                {
                    pkCols.Add($"\"{c.ToLower()}\"");
                }
                string pkName = string.IsNullOrEmpty(table.PrimaryKey.ConstraintName) 
                    ? $"pk_{table.Name.ToLower()}" 
                    : table.PrimaryKey.ConstraintName.ToLower();
                colDefs.Add($"    CONSTRAINT \"{pkName}\" PRIMARY KEY ({string.Join(", ", pkCols)})");
            }

            sb.AppendLine(string.Join(",\n", colDefs));
            sb.AppendLine(");");

            return sb.ToString();
        }

        public string GenerateIndexDdl(DbTable table, string targetSchema)
        {
            StringBuilder sb = new StringBuilder();
            string schemaPrefix = string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema}\".";
            string tableNameEscaped = $"\"{table.Name.ToLower()}\"";

            foreach (var idx in table.Indexes)
            {
                // Skip primary key indexes if they have the same name as the constraint, to prevent duplicates
                if (table.PrimaryKey != null && 
                    idx.IndexName.Equals(table.PrimaryKey.ConstraintName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string uniqueStr = idx.IsUnique ? "UNIQUE " : "";
                List<string> idxCols = new List<string>();
                foreach (var c in idx.ColumnNames)
                {
                    idxCols.Add($"\"{c.ToLower()}\"");
                }

                sb.AppendLine($"CREATE {uniqueStr}INDEX \"{idx.IndexName.ToLower()}\" ON {schemaPrefix}{tableNameEscaped} ({string.Join(", ", idxCols)});");
            }

            return sb.ToString();
        }

        public string GenerateForeignKeyDdl(DbTable table, string targetSchema)
        {
            StringBuilder sb = new StringBuilder();
            string schemaPrefix = string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema}\".";
            string tableNameEscaped = $"\"{table.Name.ToLower()}\"";

            foreach (var fk in table.ForeignKeys)
            {
                string fkName = fk.ConstraintName.ToLower();
                string colName = $"\"{fk.ColumnName.ToLower()}\"";
                string refTable = $"\"{fk.ReferenceTable.ToLower()}\"";
                string refCol = $"\"{fk.ReferenceColumn.ToLower()}\"";

                sb.AppendLine($"ALTER TABLE {schemaPrefix}{tableNameEscaped} ADD CONSTRAINT \"{fkName}\" FOREIGN KEY ({colName}) REFERENCES {schemaPrefix}{refTable} ({refCol});");
            }

            return sb.ToString();
        }

        public string CleanSchemaPrefixes(string sql, string targetSchema = "", string sourceSchema = "")
        {
            if (string.IsNullOrEmpty(sql)) return string.Empty;

            HashSet<string> schemasToClean = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "kltewpt" };
            if (!string.IsNullOrEmpty(sourceSchema))
            {
                schemasToClean.Add(sourceSchema.Trim());
            }

            string targetPrefix = string.IsNullOrEmpty(targetSchema) || targetSchema.Equals("public", StringComparison.OrdinalIgnoreCase)
                ? ""
                : $"\"{targetSchema.ToLower()}\".";

            foreach (var sch in schemasToClean)
            {
                if (string.IsNullOrEmpty(sch)) continue;

                string pattern = $@"(?<!['\w])(?:""?{Regex.Escape(sch)}""?\.)(?<obj>""?\w+""?)";
                sql = Regex.Replace(sql, pattern, m => {
                    string obj = m.Groups["obj"].Value;
                    return $"{targetPrefix}{obj}";
                }, RegexOptions.IgnoreCase);
            }

            return sql;
        }

        public string GenerateViewDdl(DbView view, string targetSchema, string sourceSchema = "")
        {
            StringBuilder sb = new StringBuilder();
            string schemaPrefix = string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema}\".";
            string viewNameEscaped = $"\"{view.Name.ToLower()}\"";

            string cleanedDef = CleanSchemaPrefixes(view.Definition, targetSchema, sourceSchema);
            string convertedDef = ConvertSqlText(cleanedDef).Trim();
            if (convertedDef.EndsWith(";"))
            {
                convertedDef = convertedDef.Substring(0, convertedDef.Length - 1).Trim();
            }
            if (convertedDef.EndsWith("/"))
            {
                convertedDef = convertedDef.Substring(0, convertedDef.Length - 1).Trim();
            }

            // Replace CREATE VIEW view_name or CREATE OR REPLACE VIEW view_name in the text if it is present, 
            // or just prepend it.
            if (!Regex.IsMatch(convertedDef, @"\bcreate\s+view\b", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(convertedDef, @"\bcreate\s+or\s+replace\s+view\b", RegexOptions.IgnoreCase))
            {
                sb.AppendLine($"CREATE OR REPLACE VIEW {schemaPrefix}{viewNameEscaped} AS");
                sb.AppendLine(convertedDef);
                sb.Append(";");
            }
            else
            {
                // If it already has CREATE VIEW, just output it
                sb.Append(convertedDef);
                if (!convertedDef.EndsWith(";")) sb.Append(";");
            }

            return sb.ToString();
        }

        public string ConvertProcedure(DbProcedure proc, string targetSchema, string sourceSchema = "")
        {
            string code = proc.SourceCode;
            if (string.IsNullOrEmpty(code)) return string.Empty;

            // Strip/replace source schema qualification in signature and body
            code = CleanSchemaPrefixes(code, targetSchema, sourceSchema);

            if (proc.ObjectType.Contains("PACKAGE"))
            {
                StringBuilder packageSb = new StringBuilder();
                packageSb.AppendLine($"-- ==========================================================================");
                packageSb.AppendLine($"-- ORACLE {proc.ObjectType}: {proc.Name}");
                packageSb.AppendLine($"-- Note: PostgreSQL does not natively support packages.");
                packageSb.AppendLine($"-- Packaged subprograms should be migrated as stand-alone functions/procedures");
                packageSb.AppendLine($"-- (e.g. named as package_name$subprogram_name).");
                packageSb.AppendLine($"-- ==========================================================================");
                packageSb.AppendLine();
                packageSb.AppendLine(ConvertSqlText(proc.SourceCode));
                return packageSb.ToString();
            }

            // 1. Basic SQL function/text translations
            code = ConvertSqlText(code);

            // 2. Translate common Oracle package calls like DBMS_OUTPUT.PUT_LINE
            code = Regex.Replace(code, @"\bDBMS_OUTPUT\.PUT_LINE\s*\((.*?)\)\s*;", "RAISE NOTICE '%', $1;", RegexOptions.IgnoreCase);

            // 3. Replace data types in stored procedure body/signatures
            code = Regex.Replace(code, @"\bVARCHAR2\b", "varchar", RegexOptions.IgnoreCase);
            code = Regex.Replace(code, @"\bNVARCHAR2\b", "varchar", RegexOptions.IgnoreCase);
            code = Regex.Replace(code, @"\bNUMBER\s*(\([^\)]+\))", "numeric$1", RegexOptions.IgnoreCase);
            code = Regex.Replace(code, @"\bNUMBER\b(?!\s*\()", "integer", RegexOptions.IgnoreCase);
            code = Regex.Replace(code, @"\bBINARY_INTEGER\b", "integer", RegexOptions.IgnoreCase);
            code = Regex.Replace(code, @"\bPLS_INTEGER\b", "integer", RegexOptions.IgnoreCase);
            code = Regex.Replace(code, @"\bDATE\b", "timestamp", RegexOptions.IgnoreCase);
            code = Regex.Replace(code, @"\bCLOB\b", "text", RegexOptions.IgnoreCase);
            code = Regex.Replace(code, @"\bBLOB\b", "bytea", RegexOptions.IgnoreCase);
            code = Regex.Replace(code, @"\bSYS_REFCURSOR\b", "refcursor", RegexOptions.IgnoreCase);

            // 4. Convert IN OUT parameter syntax
            code = Regex.Replace(code, @"\bIN\s+OUT\b", "INOUT", RegexOptions.IgnoreCase);

            // 5. Parse declaration signature and perform lowercasing
            string typePattern = proc.ObjectType.Contains("FUNCTION") ? "FUNCTION" : "PROCEDURE";
            var matches = Regex.Matches(code, $@"\b(?:CREATE\s+(?:OR\s+REPLACE\s+)?{typePattern}\s+|{typePattern}\s+)(?:""?\w+""?\.)?""?(?<name>\w+)""?", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                string procName = match.Groups["name"].Value;
                int afterNameIdx = match.Index + match.Length;
                
                // Now parse parameters if they exist
                string paramsStr = "";
                int afterParamsIdx = afterNameIdx;
                
                string searchArea = code.Substring(afterNameIdx);
                int firstCharOffset = 0;
                while (firstCharOffset < searchArea.Length)
                {
                    if (char.IsWhiteSpace(searchArea[firstCharOffset]))
                    {
                        firstCharOffset++;
                    }
                    else if (firstCharOffset + 1 < searchArea.Length && searchArea[firstCharOffset] == '-' && searchArea[firstCharOffset + 1] == '-')
                    {
                        // Skip line comment
                        firstCharOffset += 2;
                        while (firstCharOffset < searchArea.Length && searchArea[firstCharOffset] != '\n' && searchArea[firstCharOffset] != '\r')
                        {
                            firstCharOffset++;
                        }
                    }
                    else if (firstCharOffset + 1 < searchArea.Length && searchArea[firstCharOffset] == '/' && searchArea[firstCharOffset + 1] == '*')
                    {
                        // Skip block comment
                        firstCharOffset += 2;
                        while (firstCharOffset + 1 < searchArea.Length && !(searchArea[firstCharOffset] == '*' && searchArea[firstCharOffset + 1] == '/'))
                        {
                            firstCharOffset++;
                        }
                        firstCharOffset += 2;
                    }
                    else
                    {
                        break;
                    }
                }
                
                if (firstCharOffset < searchArea.Length && searchArea[firstCharOffset] == '(')
                {
                    int openParenIdx = afterNameIdx + firstCharOffset;
                    int closeParenIdx = FindClosingParenthesis(code, openParenIdx);
                    if (closeParenIdx != -1)
                    {
                        paramsStr = code.Substring(openParenIdx, closeParenIdx - openParenIdx + 1);
                        afterParamsIdx = closeParenIdx + 1;
                    }
                }
                
                string matchedKeyword = "";
                int foundKeywordPos = FindIsOrAsKeyword(code, afterParamsIdx, out matchedKeyword);
                int isAsIdx = -1;
                int isAsLen = 2;
                string returnsClause = "";
                if (foundKeywordPos != -1)
                {
                    isAsIdx = foundKeywordPos;
                    isAsLen = matchedKeyword.Length;
                    string betweenParamsAndIsAs = code.Substring(afterParamsIdx, isAsIdx - afterParamsIdx).Trim();
                    
                    if (proc.ObjectType.Contains("FUNCTION"))
                    {
                        var returnMatch = Regex.Match(betweenParamsAndIsAs, @"\bRETURN\s+(?<type>[a-zA-Z0-9_().%]+)", RegexOptions.IgnoreCase);
                        if (returnMatch.Success)
                        {
                            string retType = returnMatch.Groups["type"].Value.Trim().ToLower();
                            if (retType == "varchar2" || retType == "nvarchar2") retType = "varchar";
                             else if (retType == "number") retType = "integer";
                            else if (retType == "date") retType = "timestamp";
                            else if (retType == "sys_refcursor") retType = "refcursor";
                            returnsClause = $"\nRETURNS {retType}";
                        }
                    }
                }
                
                if (isAsIdx != -1)
                {
                    string rest = code.Substring(isAsIdx + isAsLen);
                    int beginIdx = rest.IndexOf("BEGIN", StringComparison.OrdinalIgnoreCase);
                    if (beginIdx == -1) continue; // Not the actual declaration, keep looking!
                    
                    // Parameters list conversion
                    List<string> parameterNames = new List<string>();
                    if (!string.IsNullOrEmpty(paramsStr) && paramsStr.Length > 2)
                    {
                        string content = paramsStr.Substring(1, paramsStr.Length - 2);
                        // Clean block and line comments to avoid issues with trailing/commented parameters
                        content = Regex.Replace(content, @"/\*.*?\*/", "", RegexOptions.Singleline);
                        content = Regex.Replace(content, @"--.*?(\r?\n|$)", "$1", RegexOptions.Multiline);
                        content = content.Trim().Trim(',').Trim();
                        var parts = SplitSqlArgs(content);
                        List<string> convertedParams = new List<string>();
                        foreach (var part in parts)
                        {
                            string p = part.Trim();
                            if (string.IsNullOrEmpty(p)) continue;

                            var pMatch = Regex.Match(p, @"^(\w+)\s+(?:(IN\s+OUT|INOUT|IN|OUT)\s+)?([\w%_.]+)(?:\s+(?:DEFAULT|:=)\s+(.*))?$", RegexOptions.IgnoreCase);
                            if (pMatch.Success)
                            {
                                string pName = pMatch.Groups[1].Value.ToLower();
                                string pMode = pMatch.Groups[2].Value.ToUpper();
                                string pType = pMatch.Groups[3].Value.ToLower();
                                string pDefault = pMatch.Groups[4].Value;

                                parameterNames.Add(pMatch.Groups[1].Value);

                                if (pType == "varchar2" || pType == "nvarchar2") pType = "varchar";
                                 else if (pType == "number") pType = "integer";
                                else if (pType == "date") pType = "timestamp";
                                else if (pType == "sys_refcursor") pType = "refcursor";

                                string newParam = pName;
                                string finalMode = pMode;
                                if (pType == "refcursor")
                                {
                                    finalMode = "INOUT";
                                }
                                else if (!string.IsNullOrEmpty(pMode))
                                {
                                    if (pMode == "IN OUT" || pMode == "INOUT") finalMode = "INOUT";
                                    else finalMode = pMode.ToUpper();
                                }

                                if (!string.IsNullOrEmpty(finalMode))
                                {
                                    newParam += " " + finalMode;
                                }
                                newParam += " " + pType;
                                if (!string.IsNullOrEmpty(pDefault))
                                {
                                    newParam += " DEFAULT " + pDefault;
                                }
                                convertedParams.Add(newParam);
                            }
                            else
                            {
                                convertedParams.Add(p.ToLower());
                            }
                        }
                        paramsStr = "(" + string.Join(", ", convertedParams) + ")";
                        // Clean up any duplicate INOUT keywords
                        paramsStr = Regex.Replace(paramsStr, @"\b(INOUT|IN|OUT)(\s+\1)+\b", "$1", RegexOptions.IgnoreCase);
                        paramsStr = Regex.Replace(paramsStr, @"\b(INOUT|IN|OUT)\s+(INOUT|IN|OUT)\b", "INOUT", RegexOptions.IgnoreCase);
                    }
                    
                    string declareSection = rest.Substring(0, beginIdx).Trim();
                    string bodySection = rest.Substring(beginIdx);

                    if (!string.IsNullOrEmpty(declareSection))
                    {
                        declareSection = "DECLARE\n    " + declareSection;
                    }

                    foreach (var param in parameterNames)
                    {
                        if (!string.IsNullOrEmpty(declareSection))
                        {
                            declareSection = Regex.Replace(declareSection, $@"\b{param}\b", param.ToLower(), RegexOptions.IgnoreCase);
                        }
                        bodySection = Regex.Replace(bodySection, $@"\b{param}\b", param.ToLower(), RegexOptions.IgnoreCase);
                    }

                    bodySection = Regex.Replace(bodySection, $@"\b{proc.Name}\b", proc.Name.ToLower(), RegexOptions.IgnoreCase);

                    // Strip the outer block's end label
                    bodySection = Regex.Replace(bodySection, $@"\bEND\s+{proc.Name}\b", "END", RegexOptions.IgnoreCase);

                    // Prepend CALL to standalone procedure calls
                    bodySection = AddCallToProcedures(bodySection);

                    string schemaPrefix = string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema.ToLower()}\".";
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine($"CREATE OR REPLACE {typePattern} {schemaPrefix}\"{procName.ToLower()}\" {paramsStr}");
                    
                    if (!string.IsNullOrEmpty(returnsClause))
                    {
                        sb.AppendLine(returnsClause);
                    }
                    sb.AppendLine("LANGUAGE plpgsql");
                    sb.AppendLine("AS $$");
                    if (!string.IsNullOrEmpty(declareSection))
                    {
                        sb.AppendLine(declareSection);
                    }
                    sb.AppendLine(bodySection.Trim());
                    
                    string cleanBody = sb.ToString().TrimEnd();
                    if (cleanBody.EndsWith("/"))
                    {
                        cleanBody = cleanBody.Substring(0, cleanBody.Length - 1).TrimEnd();
                    }
                    if (!cleanBody.EndsWith(";"))
                    {
                        cleanBody += ";";
                    }
                    cleanBody += "\n$$;";

                    return cleanBody;
                }
            }

            // Fallback - check if it starts with PROCEDURE or FUNCTION (without CREATE OR REPLACE) and prepend
            string trimmedCode = code.Trim();
            if (trimmedCode.StartsWith("PROCEDURE", StringComparison.OrdinalIgnoreCase) ||
                trimmedCode.StartsWith("FUNCTION", StringComparison.OrdinalIgnoreCase))
            {
                string schemaPrefix = string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema.ToLower()}\".";
                var typeMatch = Regex.Match(trimmedCode, $@"^(PROCEDURE|FUNCTION)\b", RegexOptions.IgnoreCase);
                if (typeMatch.Success)
                {
                    string matchedType = typeMatch.Groups[1].Value.ToUpper();
                    string replaced = "CREATE OR REPLACE " + matchedType + " " + schemaPrefix + trimmedCode.Substring(typeMatch.Length).Trim();
                    return replaced;
                }
            }

            return code; // Fallback to raw converted code if signature couldn't be parsed
        }

        public string GenerateSequenceDdl(DbSequence seq, string targetSchema)
        {
            if (seq == null) return string.Empty;

            StringBuilder sb = new StringBuilder();
            string schemaPrefix = string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema}\".";
            string seqNameEscaped = $"\"{seq.Name.ToLower()}\"";

            sb.AppendLine($"CREATE SEQUENCE {schemaPrefix}{seqNameEscaped}");
            sb.AppendLine($"    START WITH {seq.LastNumber}");
            sb.AppendLine($"    INCREMENT BY {seq.IncrementBy}");
            sb.AppendLine($"    MINVALUE {seq.MinValue}");
            sb.AppendLine($"    MAXVALUE {seq.MaxValue};");

            return sb.ToString();
        }

        public string ConvertTrigger(DbTrigger trigger, string targetSchema, string sourceSchema = "")
        {
            if (trigger == null) return string.Empty;

            StringBuilder sb = new StringBuilder();
            string schemaPrefix = string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema}\".";
            string tableNameClean = CleanSchemaPrefixes(trigger.TableName, "", sourceSchema).Trim('"');
            string tableNameEscaped = $"\"{tableNameClean.ToLower()}\"";
            string triggerNameClean = CleanSchemaPrefixes(trigger.Name, "", sourceSchema).Trim('"');
            string triggerName = triggerNameClean.ToLower();
            string funcName = $"{triggerName}_func";

            string body = trigger.TriggerBody;
            body = Regex.Replace(body, @":NEW\.", "NEW.", RegexOptions.IgnoreCase);
            body = Regex.Replace(body, @":OLD\.", "OLD.", RegexOptions.IgnoreCase);
            body = CleanSchemaPrefixes(body, targetSchema, sourceSchema);
            body = ConvertSqlText(body);

            bool isBefore = trigger.TriggerType.ToUpper().Contains("BEFORE");
            string returnStmt = isBefore ? "    RETURN NEW;\n" : "    RETURN NULL;\n";

            int lastEnd = body.LastIndexOf("END", StringComparison.OrdinalIgnoreCase);
            if (lastEnd != -1)
            {
                body = body.Insert(lastEnd, returnStmt);
            }
            else
            {
                body = body.TrimEnd() + "\n" + returnStmt;
            }

            sb.AppendLine($"CREATE OR REPLACE FUNCTION {schemaPrefix}\"{funcName}\"()");
            sb.AppendLine("RETURNS TRIGGER AS $$");

            string trimmedBody = body.Trim();
            if (!trimmedBody.StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase) && 
                !trimmedBody.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("BEGIN");
                sb.AppendLine(body);
                sb.AppendLine("END;");
            }
            else
            {
                sb.AppendLine(body);
            }
            sb.AppendLine("$$ LANGUAGE plpgsql;");
            sb.AppendLine();

            string timing = isBefore ? "BEFORE" : "AFTER";
            string events = trigger.TriggeringEvent.ToUpper();

            sb.AppendLine($"CREATE TRIGGER \"{triggerName}\"");
            sb.AppendLine($"    {timing} {events} ON {schemaPrefix}{tableNameEscaped}");
            sb.AppendLine("    FOR EACH ROW");
            sb.AppendLine($"    EXECUTE FUNCTION {schemaPrefix}\"{funcName}\"();");

            return sb.ToString();
        }

        public string GenerateIndexDdl(DbIndex idx, string targetSchema, string sourceSchema = "")
        {
            if (idx == null) return string.Empty;

            StringBuilder sb = new StringBuilder();
            string schemaPrefix = string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema}\".";
            string tableNameClean = CleanSchemaPrefixes(idx.TableName, "", sourceSchema).Trim('"');
            string tableNameEscaped = $"\"{tableNameClean.ToLower()}\"";
            string uniqueStr = idx.IsUnique ? "UNIQUE " : "";
            string idxNameClean = CleanSchemaPrefixes(idx.Name, "", sourceSchema).Trim('"');

            List<string> idxCols = new List<string>();
            foreach (var c in idx.ColumnNames)
            {
                idxCols.Add($"\"{c.ToLower()}\"");
            }

            sb.AppendLine($"CREATE {uniqueStr}INDEX \"{idxNameClean.ToLower()}\" ON {schemaPrefix}{tableNameEscaped} ({string.Join(", ", idxCols)});");
            return sb.ToString();
        }

        private string AddCallToProcedures(string body)
        {
            var plsqlReserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "IF", "ELSIF", "ELSE", "END", "BEGIN", "DECLARE",
                "WHILE", "LOOP", "FOR", "SELECT", "INSERT", "UPDATE",
                "DELETE", "MERGE", "RETURN", "COMMIT", "ROLLBACK",
                "SAVEPOINT", "RAISE", "EXECUTE", "CLOSE", "OPEN", "FETCH",
                "EXCEPTION", "WHEN", "GOTO", "NULL", "CALL", "AND", "OR", "CASE",
                "VALUES", "PERFORM", "GET", "USING", "INTO", "FROM", "WHERE", "JOIN"
            };

            StringBuilder sb = new StringBuilder();
            int pos = 0;
            while (pos < body.Length)
            {
                Match m = Regex.Match(body.Substring(pos), @"\b(?<name>[a-zA-Z_][a-zA-Z0-9_]*)\s*\(", RegexOptions.IgnoreCase);
                if (!m.Success)
                {
                    sb.Append(body.Substring(pos));
                    break;
                }

                int matchPosInBody = pos + m.Index;
                sb.Append(body.Substring(pos, matchPosInBody - pos));

                string candidateName = m.Groups["name"].Value;
                int openParenIdx = matchPosInBody + m.Length - 1;
                int closeParenIdx = FindClosingParenthesis(body, openParenIdx);

                if (closeParenIdx != -1)
                {
                    int afterParen = closeParenIdx + 1;
                    while (afterParen < body.Length && char.IsWhiteSpace(body[afterParen]))
                    {
                        afterParen++;
                    }

                    if (afterParen < body.Length && body[afterParen] == ';' && !plsqlReserved.Contains(candidateName))
                    {
                        bool isStandalone = IsStatementStart(body, matchPosInBody);
                        if (isStandalone)
                        {
                            string args = body.Substring(openParenIdx + 1, closeParenIdx - openParenIdx - 1);
                            sb.Append($"CALL {candidateName.ToLower()}({args})");
                            pos = closeParenIdx + 1;
                            continue;
                        }
                    }
                }

                sb.Append(candidateName + "(");
                pos = openParenIdx + 1;
            }

            return sb.ToString();
        }

        private bool IsStatementStart(string text, int namePos)
        {
            int i = namePos - 1;
            bool inBlockComment = false;
            bool inLineComment = false;

            while (i >= 0)
            {
                char c = text[i];
                if (inLineComment)
                {
                    if (c == '\n' || c == '\r')
                    {
                        inLineComment = false;
                    }
                    i--;
                    continue;
                }
                if (inBlockComment)
                {
                    if (c == '/' && i - 1 >= 0 && text[i - 1] == '*')
                    {
                        inBlockComment = false;
                        i -= 2;
                    }
                    else
                    {
                        i--;
                    }
                    continue;
                }

                if (c == '/' && i - 1 >= 0 && text[i - 1] == '*')
                {
                    inBlockComment = true;
                    i -= 2;
                    continue;
                }
                if (c == '\n' || c == '\r')
                {
                    int lineStart = i;
                    while (lineStart >= 0 && text[lineStart] != '\n' && text[lineStart] != '\r')
                    {
                        if (text[lineStart] == '-' && lineStart - 1 >= 0 && text[lineStart - 1] == '-')
                        {
                            inLineComment = true;
                            break;
                        }
                        lineStart--;
                    }
                }

                if (char.IsWhiteSpace(c))
                {
                    i--;
                    continue;
                }

                if (c == ';')
                {
                    return true;
                }

                int wordEnd = i;
                int wordStart = i;
                while (wordStart >= 0 && char.IsLetterOrDigit(text[wordStart]))
                {
                    wordStart--;
                }
                if (wordEnd > wordStart)
                {
                    string word = text.Substring(wordStart + 1, wordEnd - wordStart).ToUpper();
                    if (word == "BEGIN" || word == "THEN" || word == "ELSE" || word == "LOOP" || word == "EXCEPTION")
                    {
                        return true;
                    }
                }

                return false;
            }

            return true;
        }

        private int FindIsOrAsKeyword(string text, int startPos, out string matchedKeyword)
        {
            matchedKeyword = "";
            bool inLineComment = false;
            bool inBlockComment = false;
            bool inString = false;
            char stringQuote = '\0';

            for (int i = startPos; i < text.Length; i++)
            {
                char c = text[i];
                if (inLineComment)
                {
                    if (c == '\n' || c == '\r')
                    {
                        inLineComment = false;
                    }
                    continue;
                }
                if (inBlockComment)
                {
                    if (c == '*' && i + 1 < text.Length && text[i + 1] == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    continue;
                }
                if (inString)
                {
                    if (c == stringQuote)
                    {
                        if (i + 1 < text.Length && text[i + 1] == stringQuote)
                        {
                            i++; // Escaped quote
                        }
                        else
                        {
                            inString = false;
                        }
                    }
                    continue;
                }

                // Check for start of comments or strings
                if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
                if (c == '\'' || c == '"')
                {
                    inString = true;
                    stringQuote = c;
                    continue;
                }

                // Check for IS or AS keyword
                if (char.IsWhiteSpace(c) || c == '(' || c == ')' || c == ',' || c == ';')
                {
                    continue;
                }

                // Match "IS" or "AS" as full words
                if (i + 1 < text.Length)
                {
                    string word2 = text.Substring(i, 2).ToUpper();
                    if ((word2 == "IS" || word2 == "AS") && 
                        (i == 0 || !char.IsLetterOrDigit(text[i - 1])) && 
                        (i + 2 >= text.Length || !char.IsLetterOrDigit(text[i + 2])))
                    {
                        matchedKeyword = text.Substring(i, 2);
                        return i;
                    }
                }
            }
            return -1;
        }

        public HashSet<string> ExtractNonVarcharNames(string sql)
        {
            var nonVarcharNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var varcharNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(sql)) return nonVarcharNames;

            // 1. Match procedure / function parameter declarations
            var procFuncMatches = Regex.Matches(sql, @"\b(?:PROCEDURE|FUNCTION)\s+(?:""?\w+""?\.)?""?(?<name>\w+)""?\s*\((?<params>[^)]+)\)", RegexOptions.IgnoreCase);
            foreach (Match m in procFuncMatches)
            {
                string paramsStr = m.Groups["params"].Value;
                var parts = SplitSqlArgs(paramsStr);
                foreach (var part in parts)
                {
                    string p = part.Trim();
                    p = Regex.Replace(p, @"/\*.*?\*/", "", RegexOptions.Singleline);
                    p = Regex.Replace(p, @"--.*$", "", RegexOptions.Multiline).Trim();
                    if (string.IsNullOrEmpty(p)) continue;

                    var pMatch = Regex.Match(p, @"^(\w+)\s+(?:(IN\s+OUT|INOUT|IN|OUT)\s+)?([\w%_.]+)", RegexOptions.IgnoreCase);
                    if (pMatch.Success)
                    {
                        string pName = pMatch.Groups[1].Value;
                        string pType = pMatch.Groups[3].Value;

                        if (IsVarcharType(pType))
                        {
                            varcharNames.Add(pName);
                        }
                        else
                        {
                            nonVarcharNames.Add(pName);
                        }
                    }
                }
            }

            // 2. Match local variable declarations in PL/SQL blocks
            var varMatches = Regex.Matches(sql, @"\b(?<varName>[a-zA-Z_]\w*)\s+(?<varType>[a-zA-Z_][a-zA-Z0-9_%]*)(?:\s*\([^)]*\))?\s*(?:NOT\s+NULL)?\s*(?::=|DEFAULT|;)", RegexOptions.IgnoreCase);
            var reservedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "PROCEDURE", "FUNCTION", "BEGIN", "END", "IF", "THEN", "ELSE", "ELSIF",
                "LOOP", "WHILE", "FOR", "SELECT", "INSERT", "UPDATE", "DELETE", "RETURN", "RAISE",
                "DECLARE", "EXCEPTION", "WHEN", "OR", "AND", "NOT", "IN", "IS", "AS", "INTO", "FROM", "WHERE", "CALL"
            };

            foreach (Match m in varMatches)
            {
                string varName = m.Groups["varName"].Value;
                string varType = m.Groups["varType"].Value;

                if (reservedKeywords.Contains(varName) || reservedKeywords.Contains(varType))
                    continue;

                if (IsVarcharType(varType))
                {
                    varcharNames.Add(varName);
                }
                else
                {
                    if (!varcharNames.Contains(varName))
                    {
                        nonVarcharNames.Add(varName);
                    }
                }
            }

            return nonVarcharNames;
        }

        private bool IsVarcharType(string typeStr)
        {
            if (string.IsNullOrWhiteSpace(typeStr)) return false;
            string t = typeStr.Trim().ToUpper();
            int parenIdx = t.IndexOf('(');
            if (parenIdx > 0) t = t.Substring(0, parenIdx).Trim();

            if (t.EndsWith("%TYPE"))
            {
                string baseName = t.Substring(0, t.Length - 5).Trim();
                if (baseName.EndsWith("_ID") || baseName.EndsWith("ID") || baseName.EndsWith("_NO") || 
                    baseName.EndsWith("_NUM") || baseName.EndsWith("_DATE") || baseName.EndsWith("_TIME") ||
                    baseName.EndsWith("_AMT") || baseName.EndsWith("_AMOUNT") || baseName.EndsWith("_QTY") ||
                    baseName.EndsWith("_COUNT") || baseName.EndsWith("_SEQ") || baseName.EndsWith("_PRICE"))
                {
                    return false;
                }
            }

            return t == "VARCHAR" || t == "VARCHAR2" || 
                   t == "NVARCHAR" || t == "NVARCHAR2" || 
                   t == "CHAR" || t == "NCHAR" || 
                   t == "TEXT" || t == "CLOB" || t == "NCLOB" ||
                   t == "STRING" || t == "CHARACTER" || t == "LONG";
        }

        private bool IsNonVarcharExpr(string expr, ISet<string> nonVarcharNames)
        {
            if (string.IsNullOrWhiteSpace(expr)) return false;

            string cleaned = expr.Trim();
            while (cleaned.StartsWith("(") && cleaned.EndsWith(")") && FindClosingParenthesis(cleaned, 0) == cleaned.Length - 1)
            {
                cleaned = cleaned.Substring(1, cleaned.Length - 2).Trim();
            }

            if (nonVarcharNames != null && nonVarcharNames.Contains(cleaned))
            {
                return true;
            }

            int dotIdx = cleaned.LastIndexOf('.');
            if (dotIdx >= 0 && dotIdx < cleaned.Length - 1)
            {
                string memberName = cleaned.Substring(dotIdx + 1);
                if (nonVarcharNames != null && nonVarcharNames.Contains(memberName))
                {
                    return true;
                }
            }

            if (Regex.IsMatch(cleaned, @"^-?\d+(?:\.\d+)?$"))
            {
                return true;
            }

            string upperExpr = cleaned.ToUpper();
            if (upperExpr.StartsWith("TO_NUMBER") || upperExpr.StartsWith("COUNT") ||
                upperExpr.StartsWith("SUM") || upperExpr.StartsWith("AVG") ||
                upperExpr.StartsWith("MAX") || upperExpr.StartsWith("MIN") ||
                upperExpr.StartsWith("LENGTH") || upperExpr.StartsWith("POSITION") ||
                upperExpr.StartsWith("ROUND") || upperExpr.StartsWith("TRUNC") ||
                upperExpr.StartsWith("ABS"))
            {
                return true;
            }

            if (upperExpr.EndsWith("::INTEGER") || upperExpr.EndsWith("::NUMERIC") ||
                upperExpr.EndsWith("::BIGINT") || upperExpr.EndsWith("::SMALLINT") ||
                upperExpr.EndsWith("::TIMESTAMP") || upperExpr.EndsWith("::DATE") ||
                upperExpr.EndsWith("::REFCURSOR") || upperExpr.EndsWith("::BOOLEAN"))
            {
                return true;
            }

            return false;
        }

        private string ConvertDateSubtraction(string sql)
        {
            if (string.IsNullOrEmpty(sql)) return string.Empty;

            // 1. First, find any TO_NUMBER(...) wrapping date subtractions and strip TO_NUMBER
            string toNumPattern = @"\bTO_NUMBER\s*\(";
            int pos = 0;
            while (pos < sql.Length)
            {
                var match = Regex.Match(sql.Substring(pos), toNumPattern, RegexOptions.IgnoreCase);
                if (!match.Success) break;

                int startIdx = pos + match.Index;
                int openParenIdx = startIdx + match.Length - 1;
                int closeParenIdx = FindClosingParenthesis(sql, openParenIdx);
                if (closeParenIdx == -1)
                {
                    pos = openParenIdx + 1;
                    continue;
                }

                string inner = sql.Substring(openParenIdx + 1, closeParenIdx - openParenIdx - 1);
                if (Regex.IsMatch(inner, @"\b(?:TO_DATE|SYSDATE|CURRENT_TIMESTAMP|CURRENT_DATE)\b", RegexOptions.IgnoreCase))
                {
                    // Strip TO_NUMBER(...) wrapper
                    sql = sql.Remove(startIdx, closeParenIdx - startIdx + 1).Insert(startIdx, inner);
                    pos = startIdx; // Re-check from startIdx
                }
                else
                {
                    pos = closeParenIdx + 1;
                }
            }

            // 2. Handle date subtraction multiplied by 24*60*60 (or 86400) -> EXTRACT(EPOCH FROM (date1 - date2))
            string multPattern = @"(?:\(\s*)?(?:TO_DATE\s*\(\s*(?<e1>[a-zA-Z0-9_.]+)\s*,\s*'[^']+'\s*\)|(?<e1>CURRENT_TIMESTAMP|CURRENT_DATE|SYSDATE(?:\(\))?|""?\w+""?\.""?\w+""?|""?\w+""?))\s*-\s*(?:TO_DATE\s*\(\s*(?<e2>[a-zA-Z0-9_.]+)\s*,\s*'[^']+'\s*\)|(?<e2>CURRENT_TIMESTAMP|CURRENT_DATE|SYSDATE(?:\(\))?|""?\w+""?\.""?\w+""?|""?\w+""?))(?:\s*\))?\s*\*\s*(?:24\s*\*\s*60\s*\*\s*60|86400)";
            sql = Regex.Replace(sql, multPattern, m => {
                string e1 = FormatDateOperand(m.Groups["e1"].Value);
                string e2 = FormatDateOperand(m.Groups["e2"].Value);
                return $"EXTRACT(EPOCH FROM ({e1} - {e2}))";
            }, RegexOptions.IgnoreCase);

            // 3. Handle plain date subtraction (NOT multiplied by 86400, and NOT inside EXTRACT)
            string plainPattern = @"(?:TO_DATE\s*\(\s*(?<e1>[a-zA-Z0-9_.]+)\s*,\s*'[^']+'\s*\)|(?<e1>CURRENT_TIMESTAMP|CURRENT_DATE|SYSDATE(?:\(\))?))\s*-\s*(?:TO_DATE\s*\(\s*(?<e2>[a-zA-Z0-9_.]+)\s*,\s*'[^']+'\s*\)|(?<e2>CURRENT_TIMESTAMP|CURRENT_DATE|SYSDATE(?:\(\))?))";
            sql = Regex.Replace(sql, plainPattern, m => {
                int idx = m.Index;
                string leftText = sql.Substring(0, idx).ToUpper();
                if (leftText.EndsWith("EXTRACT(EPOCH FROM (") || leftText.EndsWith("EXTRACT(EPOCH FROM ( "))
                {
                    return m.Value;
                }

                string e1 = FormatDateOperand(m.Groups["e1"].Value);
                string e2 = FormatDateOperand(m.Groups["e2"].Value);
                return $"EXTRACT(EPOCH FROM ({e1} - {e2})) / 86400";
            }, RegexOptions.IgnoreCase);

            return sql;
        }

        private string FormatDateOperand(string op)
        {
            if (string.IsNullOrWhiteSpace(op)) return "CURRENT_TIMESTAMP";
            string trimmed = op.Trim();
            if (trimmed.Equals("SYSDATE", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("SYSDATE()", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("CURRENT_DATE", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase))
            {
                return "CURRENT_TIMESTAMP";
            }
            if (trimmed.EndsWith("::timestamp", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
            return $"{trimmed}::timestamp";
        }
    }
}
