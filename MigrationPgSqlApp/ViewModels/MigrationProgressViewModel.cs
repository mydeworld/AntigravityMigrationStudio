using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MigrationPgSqlApp.Models;
using MigrationPgSqlApp.Services;

namespace MigrationPgSqlApp.ViewModels
{
    public class MigrationProgressViewModel : ObservableObject
    {
        private readonly OracleDbService _oracleService;
        private readonly PostgresDbService _pgService;
        private readonly SqlConverter _sqlConverter = new();
        private CancellationTokenSource? _cts;

        public MigrationProgressViewModel(OracleDbService oracleService, PostgresDbService pgService)
        {
            _oracleService = oracleService;
            _pgService = pgService;

            CancelCommand = new RelayCommand(CancelMigration, () => IsRunning);
            ShowFailuresCommand = new RelayCommand(ShowFailures);
        }

        public IRelayCommand ShowFailuresCommand { get; }

        // Configuration Options
        private bool _migrateSchema = true;
        public bool MigrateSchema
        {
            get => _migrateSchema;
            set => SetProperty(ref _migrateSchema, value);
        }

        private bool _migrateData = true;
        public bool MigrateData
        {
            get => _migrateData;
            set => SetProperty(ref _migrateData, value);
        }

        private bool _migrateProcedures = true;
        public bool MigrateProcedures
        {
            get => _migrateProcedures;
            set => SetProperty(ref _migrateProcedures, value);
        }

        private bool _migrateSequences = true;
        public bool MigrateSequences
        {
            get => _migrateSequences;
            set => SetProperty(ref _migrateSequences, value);
        }

        private bool _migrateIndexes = true;
        public bool MigrateIndexes
        {
            get => _migrateIndexes;
            set => SetProperty(ref _migrateIndexes, value);
        }

        private bool _migrateTriggers = true;
        public bool MigrateTriggers
        {
            get => _migrateTriggers;
            set => SetProperty(ref _migrateTriggers, value);
        }

        private bool _truncateBeforeCopy = true;
        public bool TruncateBeforeCopy
        {
            get => _truncateBeforeCopy;
            set => SetProperty(ref _truncateBeforeCopy, value);
        }

        // Migration status states
        private bool _isRunning = false;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    CancelCommand.NotifyCanExecuteChanged();
                }
            }
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private int _successCount;
        public int SuccessCount
        {
            get => _successCount;
            set => SetProperty(ref _successCount, value);
        }

        private int _failureCount;
        public int FailureCount
        {
            get => _failureCount;
            set => SetProperty(ref _failureCount, value);
        }

        private ObservableCollection<DbObjectBase> _migratingObjects = new();
        public ObservableCollection<DbObjectBase> MigratingObjects
        {
            get => _migratingObjects;
            set => SetProperty(ref _migratingObjects, value);
        }

        private string _statusText = "Ready to start";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string _logOutput = string.Empty;
        public string LogOutput
        {
            get => _logOutput;
            set => SetProperty(ref _logOutput, value);
        }

        public IRelayCommand CancelCommand { get; }

        private void AppendLog(string message)
        {
            string timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string line = $"[{timeStamp}] {message}\n";
            LogOutput += line;
            try
            {
                System.IO.File.AppendAllText(@"d:\AIProject\Antigravity\MigrationPgSql3\migration.log", line, System.Text.Encoding.UTF8);
            }
            catch {}
        }

        private void CancelMigration()
        {
            if (IsRunning)
            {
                AppendLog("Cancellation requested...");
                _cts?.Cancel();
            }
        }

        public async Task StartMigrationAsync(
            SourceDatabaseType sourceDbType,
            string sourceConnStr, 
            string sourceSchema,
            string pgConnStr, 
            string targetSchema,
            List<DbTable> tables, 
            List<DbView> views, 
            List<DbProcedure> procedures,
            List<DbSequence> sequences,
            List<DbTrigger> triggers,
            List<DbIndex> indexes)
        {
            IsRunning = true;
            Progress = 0;
            LogOutput = string.Empty;
            SuccessCount = 0;
            FailureCount = 0;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Clear historical logs on disk
            try
            {
                if (System.IO.File.Exists(@"d:\AIProject\Antigravity\MigrationPgSql3\migration.log"))
                    System.IO.File.Delete(@"d:\AIProject\Antigravity\MigrationPgSql3\migration.log");
                if (System.IO.File.Exists(@"d:\AIProject\Antigravity\MigrationPgSql3\failed_objects.log"))
                    System.IO.File.Delete(@"d:\AIProject\Antigravity\MigrationPgSql3\failed_objects.log");
                if (System.IO.File.Exists(@"d:\AIProject\Antigravity\MigrationPgSql3\debug_failed_codes.txt"))
                    System.IO.File.Delete(@"d:\AIProject\Antigravity\MigrationPgSql3\debug_failed_codes.txt");
            }
            catch {}

            // Reset and populate list
            var selectedTables = tables.Where(t => t.IsSelected).ToList();
            var selectedViews = views.Where(v => v.IsSelected).ToList();
            var selectedProcs = procedures.Where(p => p.IsSelected).ToList();
            var selectedSeqs = sequences.Where(s => s.IsSelected).ToList();
            var selectedTrigs = triggers.Where(t => t.IsSelected).ToList();
            var selectedIdxs = indexes.Where(i => i.IsSelected).ToList();

            foreach (var item in selectedTables) item.Status = "Pending";
            foreach (var item in selectedViews) item.Status = "Pending";
            foreach (var item in selectedProcs) item.Status = "Pending";
            foreach (var item in selectedSeqs) item.Status = "Pending";
            foreach (var item in selectedTrigs) item.Status = "Pending";
            foreach (var item in selectedIdxs) item.Status = "Pending";

            MigratingObjects.Clear();
            foreach (var item in selectedTables) MigratingObjects.Add(item);
            foreach (var item in selectedSeqs) MigratingObjects.Add(item);
            foreach (var item in selectedIdxs) MigratingObjects.Add(item);
            foreach (var item in selectedViews) MigratingObjects.Add(item);
            foreach (var item in selectedProcs) MigratingObjects.Add(item);
            foreach (var item in selectedTrigs) MigratingObjects.Add(item);

            AppendLog("Starting migration process...");
            AppendLog($"Source Database Type: {sourceDbType}");
            AppendLog($"Target PostgreSQL Schema: {targetSchema}");

            try
            {
                await Task.Run(() =>
                {
                    // 1. Ensure schema exists
                    token.ThrowIfCancellationRequested();
                    _pgService.EnsureSchemaExists(pgConnStr, targetSchema);
                    if (sourceDbType == SourceDatabaseType.Oracle)
                    {
                        _pgService.CreateOracleCompatibilityHelperFunctions(pgConnStr, targetSchema);
                    }

                    var selectedTables = tables.Where(t => t.IsSelected).ToList();
                    var selectedViews = views.Where(v => v.IsSelected).ToList();
                    var selectedProcs = procedures.Where(p => p.IsSelected).ToList();
                    var selectedSeqs = sequences.Where(s => s.IsSelected).ToList();
                    var selectedTrigs = triggers.Where(t => t.IsSelected).ToList();
                    var selectedIdxs = indexes.Where(i => i.IsSelected).ToList();

                    int totalSteps = 0;
                    if (MigrateSchema)
                    {
                        totalSteps += selectedTables.Count; // Tables creation
                        totalSteps += selectedViews.Count;  // Views creation
                    }
                    if (MigrateSequences)
                    {
                        totalSteps += selectedSeqs.Count;   // Sequences creation
                    }
                    if (MigrateIndexes)
                    {
                        totalSteps += selectedIdxs.Count;   // Indexes creation
                    }
                    if (MigrateData)
                    {
                        totalSteps += selectedTables.Count; // Data copy steps
                    }
                    if (MigrateProcedures)
                    {
                        totalSteps += selectedProcs.Count;  // Stored procedure steps
                    }
                    if (MigrateTriggers)
                    {
                        totalSteps += selectedTrigs.Count;  // Triggers creation
                    }
                    if (MigrateSchema)
                    {
                        totalSteps += selectedTables.Count; // Foreign Keys step
                    }

                    if (totalSteps == 0)
                    {
                        AppendLog("No elements selected for migration.");
                        return;
                    }

                    int currentStep = 0;

                    // Helper to update progress safely
                    void UpdateProgress(string statusMsg)
                    {
                        currentStep++;
                        Progress = (double)currentStep / totalSteps * 100;
                        StatusText = $"{statusMsg} ({currentStep}/{totalSteps})";
                    }

                    // ----------------------------------------------------
                    // STEP 1: CREATE TABLES
                    // ----------------------------------------------------
                    if (MigrateSchema)
                    {
                        AppendLog("--- Creating Database Tables ---");
                        foreach (var table in selectedTables)
                        {
                            token.ThrowIfCancellationRequested();
                            table.Status = "Migrating";
                            AppendLog($"Creating table {table.Name.ToLower()}...");

                            try
                            {
                                string tableDdl = _sqlConverter.GenerateTableDdl(table, targetSchema);
                                // Drop table if exists to be safe
                                string dropSql = $"DROP TABLE IF EXISTS {(string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema.ToLower()}\".")}\"{table.Name.ToLower()}\" CASCADE;";
                                _pgService.ExecuteSql(pgConnStr, dropSql);
                                _pgService.ExecuteSql(pgConnStr, tableDdl);

                                table.Status = "Success";
                                AppendLog($"Table {table.Name.ToLower()} created successfully.");
                                SuccessCount++;
                            }
                            catch (Exception ex)
                            {
                                table.Status = "Failed";
                                table.ErrorMessage = ex.Message;
                                AppendLog($"[ERROR] Failed to create table {table.Name.ToLower()}: {ex.Message}");
                                FailureCount++;
                            }
                            UpdateProgress($"Creating table {table.Name.ToLower()}");
                        }
                    }

                    // ----------------------------------------------------
                    // STEP 1.5: CREATE SEQUENCES
                    // ----------------------------------------------------
                    if (MigrateSequences)
                    {
                        AppendLog("--- Creating Database Sequences ---");
                        foreach (var seq in selectedSeqs)
                        {
                            token.ThrowIfCancellationRequested();
                            seq.Status = "Migrating";
                            AppendLog($"Creating sequence {seq.Name.ToLower()}...");

                            try
                            {
                                string seqDdl = string.IsNullOrEmpty(seq.ConvertedDdl)
                                    ? _sqlConverter.GenerateSequenceDdl(seq, targetSchema)
                                    : seq.ConvertedDdl;

                                string dropSql = $"DROP SEQUENCE IF EXISTS {(string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema.ToLower()}\".")}\"{seq.Name.ToLower()}\" CASCADE;";
                                _pgService.ExecuteSql(pgConnStr, dropSql);
                                _pgService.ExecuteSql(pgConnStr, seqDdl);

                                seq.Status = "Success";
                                AppendLog($"Sequence {seq.Name.ToLower()} created successfully.");
                                SuccessCount++;
                            }
                            catch (Exception ex)
                            {
                                seq.Status = "Failed";
                                seq.ErrorMessage = ex.Message;
                                AppendLog($"[ERROR] Failed to create sequence {seq.Name.ToLower()}: {ex.Message}");
                                FailureCount++;
                            }
                            UpdateProgress($"Creating sequence {seq.Name.ToLower()}");
                        }
                    }

                    // ----------------------------------------------------
                    // STEP 2: COPY DATA (BULK COPY)
                    // ----------------------------------------------------
                    if (MigrateData)
                    {
                        AppendLog("--- Migrating Table Data (Bulk Copy) ---");
                        foreach (var table in selectedTables)
                        {
                            token.ThrowIfCancellationRequested();
                            
                            if (MigrateSchema && table.Status == "Failed")
                            {
                                AppendLog($"Skipping data copy for {table.Name.ToLower()} because table creation failed.");
                                UpdateProgress($"Skipping data copy for {table.Name.ToLower()}");
                                continue;
                            }

                            table.Status = "Migrating";
                            AppendLog($"Copying data for table {table.Name.ToLower()} (Est. Rows: {table.RowCount})...");

                            try
                            {
                                if (TruncateBeforeCopy)
                                {
                                    AppendLog($"Truncating PostgreSQL table {table.Name.ToLower()}...");
                                    _pgService.TruncateTable(pgConnStr, targetSchema, table.Name);
                                }

                                if (sourceDbType == SourceDatabaseType.Oracle)
                                {
                                    var connectionAndReader = _oracleService.GetTableDataReader(sourceConnStr, table.Name);
                                    using (var oracleConn = connectionAndReader.Item1)
                                    using (var reader = connectionAndReader.Item2)
                                    {
                                        long count = _pgService.BulkCopyData(pgConnStr, targetSchema, table.Name, reader, (rows) =>
                                        {
                                            table.MigratedCount = rows;
                                            AppendLog($"   Table {table.Name.ToLower()}: imported {rows} rows...");
                                        });
                                        table.MigratedCount = count;
                                        table.Status = "Success";
                                        AppendLog($"Table {table.Name.ToLower()}: Completed bulk import of {count} rows.");
                                        SuccessCount++;
                                    }
                                }
                                else
                                {
                                    var connectionAndReader = _pgService.GetTableDataReader(sourceConnStr, sourceSchema, table.Name);
                                    using (var pgSrcConn = connectionAndReader.Item1)
                                    using (var reader = connectionAndReader.Item2)
                                    {
                                        long count = _pgService.BulkCopyData(pgConnStr, targetSchema, table.Name, reader, (rows) =>
                                        {
                                            table.MigratedCount = rows;
                                            AppendLog($"   Table {table.Name.ToLower()}: imported {rows} rows...");
                                        });
                                        table.MigratedCount = count;
                                        table.Status = "Success";
                                        AppendLog($"Table {table.Name.ToLower()}: Completed bulk import of {count} rows.");
                                        SuccessCount++;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                table.Status = "Failed";
                                table.ErrorMessage = ex.Message;
                                AppendLog($"[ERROR] Failed to copy data for table {table.Name.ToLower()}: {ex.Message}");
                                FailureCount++;
                            }
                            UpdateProgress($"Migrating data for {table.Name.ToLower()}");
                        }
                    }

                    // ----------------------------------------------------
                    // STEP 3: CREATE INDEXES
                    // ----------------------------------------------------
                    if (MigrateIndexes)
                    {
                        AppendLog("--- Creating Non-PK Indexes ---");
                        foreach (var idx in selectedIdxs)
                        {
                            token.ThrowIfCancellationRequested();
                            idx.Status = "Migrating";

                            string idxNameClean = _sqlConverter.CleanSchemaPrefixes(idx.Name, "", sourceSchema).Trim('"');
                            string tblNameClean = _sqlConverter.CleanSchemaPrefixes(idx.TableName, "", sourceSchema).Trim('"');
                            
                            var matchingTable = selectedTables.FirstOrDefault(t => t.Name.Equals(tblNameClean, StringComparison.OrdinalIgnoreCase));
                            if (matchingTable?.PrimaryKey != null && 
                                idxNameClean.Equals(matchingTable.PrimaryKey.ConstraintName, StringComparison.OrdinalIgnoreCase))
                            {
                                idx.Status = "Success";
                                AppendLog($"Skipping primary key constraint index {idxNameClean.ToLower()} ON {tblNameClean.ToLower()} (auto-managed by PK constraint).");
                                SuccessCount++;
                                UpdateProgress($"Skipping PK index {idxNameClean.ToLower()}");
                                continue;
                            }

                            AppendLog($"Creating index {idxNameClean.ToLower()} ON {tblNameClean.ToLower()}...");
                            try
                            {
                                string indexDdl = string.IsNullOrEmpty(idx.ConvertedDdl)
                                    ? _sqlConverter.GenerateIndexDdl(idx, targetSchema, sourceSchema)
                                    : _sqlConverter.CleanSchemaPrefixes(idx.ConvertedDdl, targetSchema, sourceSchema);

                                if (!string.IsNullOrWhiteSpace(indexDdl))
                                {
                                    string dropSql = $"DROP INDEX IF EXISTS {(string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema.ToLower()}\".")}\"{idxNameClean.ToLower()}\" CASCADE;";
                                    try
                                    {
                                        _pgService.ExecuteSql(pgConnStr, dropSql);
                                    }
                                    catch
                                    {
                                        // Ignore drop errors if owned by constraint
                                    }
                                    _pgService.ExecuteSql(pgConnStr, indexDdl);

                                    idx.Status = "Success";
                                    AppendLog($"Index {idxNameClean.ToLower()} created successfully.");
                                    SuccessCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                if (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) || 
                                    ex.Message.Contains("cannot drop index", StringComparison.OrdinalIgnoreCase))
                                {
                                    idx.Status = "Success";
                                    AppendLog($"Index {idxNameClean.ToLower()} creation skipped or constraint index: {ex.Message}");
                                    SuccessCount++;
                                }
                                else
                                {
                                    idx.Status = "Failed";
                                    idx.ErrorMessage = ex.Message;
                                    AppendLog($"[WARNING] Failed to create index {idxNameClean.ToLower()}: {ex.Message}");
                                    FailureCount++;
                                }
                            }
                            UpdateProgress($"Creating index {idxNameClean.ToLower()}");
                        }
                    }

                    // ----------------------------------------------------
                    // STEP 4: CREATE VIEWS
                    // ----------------------------------------------------
                    if (MigrateSchema)
                    {
                        AppendLog("--- Creating Database Views ---");
                        foreach (var view in selectedViews)
                        {
                            token.ThrowIfCancellationRequested();
                            view.Status = "Migrating";
                            AppendLog($"Creating view {view.Name.ToLower()}...");

                            if (string.IsNullOrWhiteSpace(view.Definition))
                            {
                                view.Status = "Failed";
                                view.ErrorMessage = "Oracle view definition is empty or could not be read.";
                                AppendLog($"[ERROR] Failed to create view {view.Name.ToLower()}: {view.ErrorMessage}");
                                FailureCount++;
                                UpdateProgress($"Creating view {view.Name.ToLower()}");
                                continue;
                            }

                            try
                            {
                                string viewDdl = string.IsNullOrEmpty(view.ConvertedDefinition) 
                                    ? _sqlConverter.GenerateViewDdl(view, targetSchema, sourceSchema) 
                                    : _sqlConverter.CleanSchemaPrefixes(view.ConvertedDefinition, targetSchema, sourceSchema);

                                _pgService.ExecuteSql(pgConnStr, viewDdl);
                                view.Status = "Success";
                                AppendLog($"View {view.Name.ToLower()} created successfully.");
                                SuccessCount++;
                            }
                            catch (Exception ex)
                            {
                                view.Status = "Failed";
                                view.ErrorMessage = ex.Message;
                                AppendLog($"[ERROR] Failed to create view {view.Name.ToLower()}: {ex.Message}");
                                FailureCount++;

                                try
                                {
                                    StringBuilder debugSb = new StringBuilder();
                                    debugSb.AppendLine("==================================================");
                                    debugSb.AppendLine("[TYPE]  VIEW");
                                    debugSb.AppendLine($"[NAME]  {view.Name}");
                                    debugSb.AppendLine($"[ERROR] {ex.Message}");
                                    debugSb.AppendLine("==================================================");
                                    debugSb.AppendLine("--- ORIGINAL ORACLE CODE ---");
                                    debugSb.AppendLine(view.Definition);
                                    debugSb.AppendLine();
                                    debugSb.AppendLine("--- CONVERTED POSTGRESQL DDL ---");
                                    string errDdl = string.IsNullOrEmpty(view.ConvertedDefinition) 
                                        ? _sqlConverter.GenerateViewDdl(view, targetSchema, sourceSchema) 
                                        : _sqlConverter.CleanSchemaPrefixes(view.ConvertedDefinition, targetSchema, sourceSchema);
                                    debugSb.AppendLine(errDdl);
                                    debugSb.AppendLine();
                                    debugSb.AppendLine(new string('=', 50));
                                    debugSb.AppendLine();
                                    System.IO.File.AppendAllText(@"d:\AIProject\Antigravity\MigrationPgSql3\debug_failed_codes.txt", debugSb.ToString(), Encoding.UTF8);
                                }
                                catch {}
                            }
                            UpdateProgress($"Creating view {view.Name.ToLower()}");
                        }
                    }

                    // ----------------------------------------------------
                    // STEP 5: CREATE PROCEDURES & FUNCTIONS
                    // ----------------------------------------------------
                    if (MigrateProcedures)
                    {
                        AppendLog("--- Creating Stored Procedures & Functions ---");
                        foreach (var proc in selectedProcs)
                        {
                            token.ThrowIfCancellationRequested();
                            proc.Status = "Migrating";
                            AppendLog($"Creating {proc.ObjectType.ToLower()} {proc.Name.ToLower()}...");

                            try
                            {
                                string procDdl = string.IsNullOrEmpty(proc.ConvertedSourceCode) 
                                    ? _sqlConverter.ConvertProcedure(proc, targetSchema, sourceSchema) 
                                    : _sqlConverter.CleanSchemaPrefixes(proc.ConvertedSourceCode, targetSchema, sourceSchema);

                                string procName = _sqlConverter.CleanSchemaPrefixes(proc.Name, "", sourceSchema).Trim('"').ToLower();
                                string schemaPrefix = string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema.ToLower()}\".";
                                
                                string dropSql = $"DROP {proc.ObjectType.ToUpper()} IF EXISTS {schemaPrefix}\"{procName}\" CASCADE;";
                                try
                                {
                                    _pgService.ExecuteSql(pgConnStr, dropSql);
                                }
                                catch
                                {
                                    // Ignore drop errors
                                }

                                _pgService.ExecuteSql(pgConnStr, procDdl);
                                proc.Status = "Success";
                                AppendLog($"{proc.ObjectType} {proc.Name.ToLower()} created successfully.");
                                SuccessCount++;
                            }
                            catch (Exception ex)
                            {
                                proc.Status = "Failed";
                                proc.ErrorMessage = ex.Message;
                                AppendLog($"[ERROR] Failed to create {proc.ObjectType.ToLower()} {proc.Name.ToLower()}: {ex.Message}");
                                FailureCount++;

                                try
                                {
                                    StringBuilder debugSb = new StringBuilder();
                                    debugSb.AppendLine("==================================================");
                                    debugSb.AppendLine($"[TYPE]  {proc.ObjectType}");
                                    debugSb.AppendLine($"[NAME]  {proc.Name}");
                                    debugSb.AppendLine($"[ERROR] {ex.Message}");
                                    debugSb.AppendLine("==================================================");
                                    debugSb.AppendLine("--- ORIGINAL ORACLE CODE ---");
                                    debugSb.AppendLine(proc.SourceCode);
                                    debugSb.AppendLine();
                                    debugSb.AppendLine("--- CONVERTED POSTGRESQL DDL ---");
                                    string errDdl = string.IsNullOrEmpty(proc.ConvertedSourceCode) 
                                        ? _sqlConverter.ConvertProcedure(proc, targetSchema, sourceSchema) 
                                        : _sqlConverter.CleanSchemaPrefixes(proc.ConvertedSourceCode, targetSchema, sourceSchema);
                                    debugSb.AppendLine(errDdl);
                                    debugSb.AppendLine();
                                    debugSb.AppendLine(new string('=', 50));
                                    debugSb.AppendLine();
                                    System.IO.File.AppendAllText(@"d:\AIProject\Antigravity\MigrationPgSql3\debug_failed_codes.txt", debugSb.ToString(), Encoding.UTF8);
                                }
                                catch {}
                            }
                            UpdateProgress($"Creating procedure {proc.Name.ToLower()}");
                        }
                    }

                    // ----------------------------------------------------
                    // STEP 5.5: CREATE TRIGGERS
                    // ----------------------------------------------------
                    if (MigrateTriggers)
                    {
                        AppendLog("--- Creating Database Triggers ---");
                        foreach (var trig in selectedTrigs)
                        {
                            token.ThrowIfCancellationRequested();
                            trig.Status = "Migrating";
                            AppendLog($"Creating trigger {trig.Name.ToLower()} ON {trig.TableName.ToLower()}...");

                            try
                            {
                                string trigDdl = string.IsNullOrEmpty(trig.ConvertedTriggerBody)
                                    ? _sqlConverter.ConvertTrigger(trig, targetSchema, sourceSchema)
                                    : _sqlConverter.CleanSchemaPrefixes(trig.ConvertedTriggerBody, targetSchema, sourceSchema);

                                string trigNameClean = _sqlConverter.CleanSchemaPrefixes(trig.Name, "", sourceSchema).Trim('"').ToLower();
                                string trigTableClean = _sqlConverter.CleanSchemaPrefixes(trig.TableName, "", sourceSchema).Trim('"').ToLower();

                                string dropSql = $"DROP TRIGGER IF EXISTS \"{trigNameClean}\" ON {(string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema.ToLower()}\".")}\"{trigTableClean}\" CASCADE;";
                                _pgService.ExecuteSql(pgConnStr, dropSql);

                                string dropFuncSql = $"DROP FUNCTION IF EXISTS {(string.IsNullOrEmpty(targetSchema) ? "" : $"\"{targetSchema.ToLower()}\".")}\"{trigNameClean}_func\"() CASCADE;";
                                _pgService.ExecuteSql(pgConnStr, dropFuncSql);

                                _pgService.ExecuteSql(pgConnStr, trigDdl);

                                trig.Status = "Success";
                                AppendLog($"Trigger {trig.Name.ToLower()} created successfully.");
                                SuccessCount++;
                            }
                            catch (Exception ex)
                            {
                                trig.Status = "Failed";
                                trig.ErrorMessage = ex.Message;
                                AppendLog($"[ERROR] Failed to create trigger {trig.Name.ToLower()}: {ex.Message}");
                                FailureCount++;
                            }
                            UpdateProgress($"Creating trigger {trig.Name.ToLower()}");
                        }
                    }

                    // ----------------------------------------------------
                    // STEP 6: CREATE FOREIGN KEYS
                    // ----------------------------------------------------
                    if (MigrateSchema)
                    {
                        AppendLog("--- Creating Table Foreign Keys ---");
                        foreach (var table in selectedTables)
                        {
                            token.ThrowIfCancellationRequested();
                            if (table.Status == "Failed" && MigrateSchema)
                            {
                                UpdateProgress($"Skipping foreign keys for {table.Name.ToLower()}");
                                continue;
                            }

                            AppendLog($"Creating foreign keys for {table.Name.ToLower()}...");
                            try
                            {
                                string fkDdl = _sqlConverter.GenerateForeignKeyDdl(table, targetSchema);
                                if (!string.IsNullOrWhiteSpace(fkDdl))
                                {
                                    _pgService.ExecuteSql(pgConnStr, fkDdl);
                                    AppendLog($"Foreign keys for {table.Name.ToLower()} created.");
                                }
                            }
                            catch (Exception ex)
                            {
                                AppendLog($"[WARNING] Failed to create foreign keys for {table.Name.ToLower()}: {ex.Message}");
                            }
                            UpdateProgress($"Creating foreign keys for {table.Name.ToLower()}");
                        }
                    }
                }, token);

                Progress = 100;
                StatusText = "Migration completed successfully!";
                AppendLog("Migration process completed.");

                // Write a clean summary of failures
                WriteFailureSummary();
            }
            catch (OperationCanceledException)
            {
                StatusText = "Migration cancelled.";
                AppendLog("Migration was cancelled by the user.");
            }
            catch (Exception ex)
            {
                StatusText = "Migration failed.";
                AppendLog($"[CRITICAL ERROR] Migration failed: {ex.Message}");
                WriteFailureSummary();
            }
            finally
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void WriteFailureSummary()
        {
            try
            {
                var failedList = new List<DbObjectBase>();
                foreach (var obj in MigratingObjects)
                {
                    if (obj.Status == "Failed")
                    {
                        failedList.Add(obj);
                    }
                }

                if (failedList.Count > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("==================================================");
                    sb.AppendLine("             MIGRATION FAILURE SUMMARY            ");
                    sb.AppendLine("==================================================");
                    sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine($"Total Failed Objects: {failedList.Count}");
                    sb.AppendLine();

                    foreach (var obj in failedList)
                    {
                        string objType = obj.GetType().Name.Replace("Db", "");
                        sb.AppendLine($"[TYPE]  {objType}");
                        sb.AppendLine($"[NAME]  {obj.Name}");
                        sb.AppendLine($"[ERROR] {obj.ErrorMessage}");
                        sb.AppendLine(new string('-', 50));
                    }

                    System.IO.File.WriteAllText(@"d:\AIProject\Antigravity\MigrationPgSql3\failed_objects.log", sb.ToString(), System.Text.Encoding.UTF8);
                }
            }
            catch {}
        }

        private void ShowFailures()
        {
            var failedObjects = MigratingObjects.Where(o => o.Status == "Failed").ToList();
            if (failedObjects.Count == 0)
            {
                System.Windows.MessageBox.Show("No failed items to display.", "Information", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var win = new FailureDetailsWindow(failedObjects);
            win.Owner = System.Windows.Application.Current.MainWindow;
            win.ShowDialog();
        }
    }
}
