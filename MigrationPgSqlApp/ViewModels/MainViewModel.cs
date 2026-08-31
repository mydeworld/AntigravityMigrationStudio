using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MigrationPgSqlApp.Models;
using MigrationPgSqlApp.Services;

namespace MigrationPgSqlApp.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly OracleDbService _oracleService = new();
        private readonly PostgresDbService _pgService = new();

        public MainViewModel()
        {
            ConnectionVM = new ConnectionViewModel(_oracleService, _pgService);
            SchemaTreeVM = new SchemaTreeViewModel();
            ObjectPreviewVM = new ObjectPreviewViewModel();
            ProgressVM = new MigrationProgressViewModel(_oracleService, _pgService);

            LoadMetadataCommand = new AsyncRelayCommand(LoadMetadataAsync, () => ConnectionVM.IsSourceSuccess);
            StartMigrationCommand = new AsyncRelayCommand(StartMigrationAsync, () => !ProgressVM.IsRunning && ConnectionVM.IsSourceSuccess && ConnectionVM.IsPgSuccess);

            // Notify start migration command when connection state changes
            ConnectionVM.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ConnectionViewModel.IsSourceSuccess) || 
                    e.PropertyName == nameof(ConnectionViewModel.IsPgSuccess))
                {
                    LoadMetadataCommand.NotifyCanExecuteChanged();
                    StartMigrationCommand.NotifyCanExecuteChanged();
                }
            };
        }

        public ConnectionViewModel ConnectionVM { get; }
        public SchemaTreeViewModel SchemaTreeVM { get; }
        public ObjectPreviewViewModel ObjectPreviewVM { get; }
        public MigrationProgressViewModel ProgressVM { get; }

        private int _activeTabIndex = 0;
        public int ActiveTabIndex
        {
            get => _activeTabIndex;
            set => SetProperty(ref _activeTabIndex, value);
        }

        private bool _isLoading = false;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public IAsyncRelayCommand LoadMetadataCommand { get; }
        public IAsyncRelayCommand StartMigrationCommand { get; }

        private async Task LoadMetadataAsync()
        {
            IsLoading = true;
            SchemaTreeVM.Clear();
            ObjectPreviewVM.SelectedItem = null;

            try
            {
                if (ConnectionVM.SourceDbType == SourceDatabaseType.Oracle)
                {
                    string oraConnStr = ConnectionVM.GetOracleConnectionString();
                    var loadTablesTask = Task.Run(() => _oracleService.LoadTables(oraConnStr));
                    var loadViewsTask = Task.Run(() => _oracleService.LoadViews(oraConnStr));
                    var loadProcsTask = Task.Run(() => _oracleService.LoadProcedures(oraConnStr));
                    var loadSeqsTask = Task.Run(() => _oracleService.LoadSequences(oraConnStr));
                    var loadTrigsTask = Task.Run(() => _oracleService.LoadTriggers(oraConnStr));
                    var loadIdxsTask = Task.Run(() => _oracleService.LoadIndexes(oraConnStr));

                    await Task.WhenAll(loadTablesTask, loadViewsTask, loadProcsTask, loadSeqsTask, loadTrigsTask, loadIdxsTask);

                    SchemaTreeVM.Tables = new ObservableCollection<Models.DbTable>(loadTablesTask.Result);
                    SchemaTreeVM.Views = new ObservableCollection<Models.DbView>(loadViewsTask.Result);
                    SchemaTreeVM.Procedures = new ObservableCollection<Models.DbProcedure>(loadProcsTask.Result);
                    SchemaTreeVM.Sequences = new ObservableCollection<Models.DbSequence>(loadSeqsTask.Result);
                    SchemaTreeVM.Triggers = new ObservableCollection<Models.DbTrigger>(loadTrigsTask.Result);
                    SchemaTreeVM.Indexes = new ObservableCollection<Models.DbIndex>(loadIdxsTask.Result);
                }
                else
                {
                    string srcPgConnStr = ConnectionVM.GetSourcePgConnectionString();
                    string srcSchema = ConnectionVM.SrcPgSchema;

                    var loadTablesTask = Task.Run(() => _pgService.LoadTables(srcPgConnStr, srcSchema));
                    var loadViewsTask = Task.Run(() => _pgService.LoadViews(srcPgConnStr, srcSchema));
                    var loadProcsTask = Task.Run(() => _pgService.LoadProcedures(srcPgConnStr, srcSchema));
                    var loadSeqsTask = Task.Run(() => _pgService.LoadSequences(srcPgConnStr, srcSchema));
                    var loadTrigsTask = Task.Run(() => _pgService.LoadTriggers(srcPgConnStr, srcSchema));
                    var loadIdxsTask = Task.Run(() => _pgService.LoadIndexes(srcPgConnStr, srcSchema));

                    await Task.WhenAll(loadTablesTask, loadViewsTask, loadProcsTask, loadSeqsTask, loadTrigsTask, loadIdxsTask);

                    SchemaTreeVM.Tables = new ObservableCollection<Models.DbTable>(loadTablesTask.Result);
                    SchemaTreeVM.Views = new ObservableCollection<Models.DbView>(loadViewsTask.Result);
                    SchemaTreeVM.Procedures = new ObservableCollection<Models.DbProcedure>(loadProcsTask.Result);
                    SchemaTreeVM.Sequences = new ObservableCollection<Models.DbSequence>(loadSeqsTask.Result);
                    SchemaTreeVM.Triggers = new ObservableCollection<Models.DbTrigger>(loadTrigsTask.Result);
                    SchemaTreeVM.Indexes = new ObservableCollection<Models.DbIndex>(loadIdxsTask.Result);
                }

                // Auto select first item for preview if available
                if (SchemaTreeVM.Tables.Count > 0)
                {
                    ObjectPreviewVM.SelectedItem = SchemaTreeVM.Tables[0];
                }
                else if (SchemaTreeVM.Views.Count > 0)
                {
                    ObjectPreviewVM.SelectedItem = SchemaTreeVM.Views[0];
                }
                else if (SchemaTreeVM.Procedures.Count > 0)
                {
                    ObjectPreviewVM.SelectedItem = SchemaTreeVM.Procedures[0];
                }

                // Setup default target schema name
                ObjectPreviewVM.TargetSchema = string.IsNullOrEmpty(ConnectionVM.PgSchema) ? "public" : ConnectionVM.PgSchema.ToLower();

                // Switch to Navigator tab (Index 1)
                ActiveTabIndex = 1;
            }
            catch (Exception ex)
            {
                // We could set an error message or dialog
                System.Windows.MessageBox.Show($"Failed to load database metadata: {ex.Message}", "Metadata Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task StartMigrationAsync()
        {
            // Save database configuration settings from this run
            ConnectionVM.SaveConfig();

            string srcConnStr = ConnectionVM.GetSourceConnectionString();
            string srcSchema = ConnectionVM.GetSourceSchema();
            string pgConnStr = ConnectionVM.GetPgConnectionString();
            string pgSchema = ConnectionVM.PgSchema.ToLower();

            // Refresh preview SQLs to ensure any edited scripts are finalized in models
            ObjectPreviewVM.RefreshDdl();

            // Switch to progress log tab (Index 2)
            ActiveTabIndex = 2;

            // Trigger migration
            await ProgressVM.StartMigrationAsync(
                ConnectionVM.SourceDbType,
                srcConnStr,
                srcSchema,
                pgConnStr,
                pgSchema,
                SchemaTreeVM.Tables.ToList(),
                SchemaTreeVM.Views.ToList(),
                SchemaTreeVM.Procedures.ToList(),
                SchemaTreeVM.Sequences.ToList(),
                SchemaTreeVM.Triggers.ToList(),
                SchemaTreeVM.Indexes.ToList()
            );
        }
    }
}
