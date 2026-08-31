using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MigrationPgSqlApp.Models;
using MigrationPgSqlApp.Services;

namespace MigrationPgSqlApp.ViewModels
{
    public class ObjectPreviewViewModel : ObservableObject
    {
        private readonly SqlConverter _sqlConverter = new();

        private DbObjectBase? _selectedItem;
        public DbObjectBase? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    OnSelectedItemChanged();
                }
            }
        }

        private DbTable? _selectedTable;
        public DbTable? SelectedTable
        {
            get => _selectedTable;
            set => SetProperty(ref _selectedTable, value);
        }

        private DbView? _selectedView;
        public DbView? SelectedView
        {
            get => _selectedView;
            set => SetProperty(ref _selectedView, value);
        }

        private DbProcedure? _selectedProcedure;
        public DbProcedure? SelectedProcedure
        {
            get => _selectedProcedure;
            set => SetProperty(ref _selectedProcedure, value);
        }

        private DbSequence? _selectedSequence;
        public DbSequence? SelectedSequence
        {
            get => _selectedSequence;
            set => SetProperty(ref _selectedSequence, value);
        }

        private DbTrigger? _selectedTrigger;
        public DbTrigger? SelectedTrigger
        {
            get => _selectedTrigger;
            set => SetProperty(ref _selectedTrigger, value);
        }

        private DbIndex? _selectedIndex;
        public DbIndex? SelectedIndex
        {
            get => _selectedIndex;
            set => SetProperty(ref _selectedIndex, value);
        }

        private string _sourceSql = string.Empty;
        public string SourceSql
        {
            get => _sourceSql;
            set => SetProperty(ref _sourceSql, value);
        }

        private string _convertedSql = string.Empty;
        public string ConvertedSql
        {
            get => _convertedSql;
            set
            {
                if (SetProperty(ref _convertedSql, value))
                {
                    // Save manual edits back to the model
                    if (SelectedProcedure != null)
                    {
                        SelectedProcedure.ConvertedSourceCode = value;
                    }
                    else if (SelectedView != null)
                    {
                        SelectedView.ConvertedDefinition = value;
                    }
                    else if (SelectedSequence != null)
                    {
                        SelectedSequence.ConvertedDdl = value;
                    }
                    else if (SelectedTrigger != null)
                    {
                        SelectedTrigger.ConvertedTriggerBody = value;
                    }
                    else if (SelectedIndex != null)
                    {
                        SelectedIndex.ConvertedDdl = value;
                    }
                }
            }
        }

        private string _targetSchema = "public";
        public string TargetSchema
        {
            get => _targetSchema;
            set
            {
                if (SetProperty(ref _targetSchema, value))
                {
                    RefreshDdl();
                }
            }
        }

        private void OnSelectedItemChanged()
        {
            SelectedTable = SelectedItem as DbTable;
            SelectedView = SelectedItem as DbView;
            SelectedProcedure = SelectedItem as DbProcedure;
            SelectedSequence = SelectedItem as DbSequence;
            SelectedTrigger = SelectedItem as DbTrigger;
            SelectedIndex = SelectedItem as DbIndex;

            RefreshDdl();
        }

        public void RefreshDdl()
        {
            if (SelectedTable != null)
            {
                SourceSql = $"-- Table: {SelectedTable.Name}\n-- Est. Rows: {SelectedTable.RowCount}\n-- Columns: {SelectedTable.Columns.Count}";
                
                string ddl = _sqlConverter.GenerateTableDdl(SelectedTable, TargetSchema);
                string idxDdl = _sqlConverter.GenerateIndexDdl(SelectedTable, TargetSchema);
                string fkDdl = _sqlConverter.GenerateForeignKeyDdl(SelectedTable, TargetSchema);

                ConvertedSql = ddl + "\n" + idxDdl + "\n" + fkDdl;
            }
            else if (SelectedView != null)
            {
                SourceSql = SelectedView.Definition;
                if (string.IsNullOrEmpty(SelectedView.ConvertedDefinition))
                {
                    SelectedView.ConvertedDefinition = _sqlConverter.GenerateViewDdl(SelectedView, TargetSchema);
                }
                ConvertedSql = SelectedView.ConvertedDefinition;
            }
            else if (SelectedProcedure != null)
            {
                SourceSql = SelectedProcedure.SourceCode;
                if (string.IsNullOrEmpty(SelectedProcedure.ConvertedSourceCode))
                {
                    SelectedProcedure.ConvertedSourceCode = _sqlConverter.ConvertProcedure(SelectedProcedure, TargetSchema);
                }
                ConvertedSql = SelectedProcedure.ConvertedSourceCode;
            }
            else if (SelectedSequence != null)
            {
                SourceSql = $"-- Sequence: {SelectedSequence.Name}\n-- Min Value: {SelectedSequence.MinValue}\n-- Max Value: {SelectedSequence.MaxValue}\n-- Increment: {SelectedSequence.IncrementBy}\n-- Last Number: {SelectedSequence.LastNumber}";
                if (string.IsNullOrEmpty(SelectedSequence.ConvertedDdl))
                {
                    SelectedSequence.ConvertedDdl = _sqlConverter.GenerateSequenceDdl(SelectedSequence, TargetSchema);
                }
                ConvertedSql = SelectedSequence.ConvertedDdl;
            }
            else if (SelectedTrigger != null)
            {
                SourceSql = $"-- Trigger: {SelectedTrigger.Name} ON {SelectedTrigger.TableName}\n-- Timing: {SelectedTrigger.TriggerType}\n-- Event: {SelectedTrigger.TriggeringEvent}\n\n{SelectedTrigger.TriggerBody}";
                if (string.IsNullOrEmpty(SelectedTrigger.ConvertedTriggerBody))
                {
                    SelectedTrigger.ConvertedTriggerBody = _sqlConverter.ConvertTrigger(SelectedTrigger, TargetSchema);
                }
                ConvertedSql = SelectedTrigger.ConvertedTriggerBody;
            }
            else if (SelectedIndex != null)
            {
                SourceSql = $"-- Index: {SelectedIndex.Name} ON {SelectedIndex.TableName}\n-- Columns: {string.Join(", ", SelectedIndex.ColumnNames)}\n-- Unique: {SelectedIndex.IsUnique}";
                if (string.IsNullOrEmpty(SelectedIndex.ConvertedDdl))
                {
                    SelectedIndex.ConvertedDdl = _sqlConverter.GenerateIndexDdl(SelectedIndex, TargetSchema);
                }
                ConvertedSql = SelectedIndex.ConvertedDdl;
            }
            else
            {
                SourceSql = string.Empty;
                ConvertedSql = string.Empty;
            }
        }
    }
}
