using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MigrationPgSqlApp.Models;

namespace MigrationPgSqlApp.ViewModels
{
    public class SchemaTreeViewModel : ObservableObject
    {
        public SchemaTreeViewModel()
        {
            ToggleAllTablesCommand = new RelayCommand<bool>(ToggleAllTables);
            ToggleAllViewsCommand = new RelayCommand<bool>(ToggleAllViews);
            ToggleAllProceduresCommand = new RelayCommand<bool>(ToggleAllProcedures);
            ToggleAllSequencesCommand = new RelayCommand<bool>(ToggleAllSequences);
            ToggleAllTriggersCommand = new RelayCommand<bool>(ToggleAllTriggers);
            ToggleAllIndexesCommand = new RelayCommand<bool>(ToggleAllIndexes);
        }

        private ObservableCollection<DbTable> _tables = new();
        public ObservableCollection<DbTable> Tables
        {
            get => _tables;
            set => SetProperty(ref _tables, value);
        }

        private ObservableCollection<DbView> _views = new();
        public ObservableCollection<DbView> Views
        {
            get => _views;
            set => SetProperty(ref _views, value);
        }

        private ObservableCollection<DbProcedure> _procedures = new();
        public ObservableCollection<DbProcedure> Procedures
        {
            get => _procedures;
            set => SetProperty(ref _procedures, value);
        }

        private ObservableCollection<DbSequence> _sequences = new();
        public ObservableCollection<DbSequence> Sequences
        {
            get => _sequences;
            set => SetProperty(ref _sequences, value);
        }

        private ObservableCollection<DbTrigger> _triggers = new();
        public ObservableCollection<DbTrigger> Triggers
        {
            get => _triggers;
            set => SetProperty(ref _triggers, value);
        }

        private ObservableCollection<DbIndex> _indexes = new();
        public ObservableCollection<DbIndex> Indexes
        {
            get => _indexes;
            set => SetProperty(ref _indexes, value);
        }

        public IRelayCommand<bool> ToggleAllTablesCommand { get; }
        public IRelayCommand<bool> ToggleAllViewsCommand { get; }
        public IRelayCommand<bool> ToggleAllProceduresCommand { get; }
        public IRelayCommand<bool> ToggleAllSequencesCommand { get; }
        public IRelayCommand<bool> ToggleAllTriggersCommand { get; }
        public IRelayCommand<bool> ToggleAllIndexesCommand { get; }

        private void ToggleAllTables(bool select)
        {
            foreach (var table in Tables)
            {
                table.IsSelected = select;
            }
        }

        private void ToggleAllViews(bool select)
        {
            foreach (var view in Views)
            {
                view.IsSelected = select;
            }
        }

        private void ToggleAllProcedures(bool select)
        {
            foreach (var proc in Procedures)
            {
                proc.IsSelected = select;
            }
        }

        private void ToggleAllSequences(bool select)
        {
            foreach (var seq in Sequences)
            {
                seq.IsSelected = select;
            }
        }

        private void ToggleAllTriggers(bool select)
        {
            foreach (var trig in Triggers)
            {
                trig.IsSelected = select;
            }
        }

        private void ToggleAllIndexes(bool select)
        {
            foreach (var idx in Indexes)
            {
                idx.IsSelected = select;
            }
        }

        public void Clear()
        {
            Tables.Clear();
            Views.Clear();
            Procedures.Clear();
            Sequences.Clear();
            Triggers.Clear();
            Indexes.Clear();
        }
    }
}
