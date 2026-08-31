using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MigrationPgSqlApp.Models
{
    public enum SourceDatabaseType
    {
        Oracle = 0,
        PostgreSQL = 1
    }

    public class DbObjectBase : ObservableObject
    {
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        private string _status = "Pending"; // Pending, Migrating, Success, Failed, Skipped
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public virtual string DisplayType => GetType().Name.Replace("Db", "");
    }

    public class DbTable : DbObjectBase
    {
        private List<ColumnInfo> _columns = new();
        public List<ColumnInfo> Columns
        {
            get => _columns;
            set => SetProperty(ref _columns, value);
        }

        private PrimaryKeyInfo? _primaryKey;
        public PrimaryKeyInfo? PrimaryKey
        {
            get => _primaryKey;
            set => SetProperty(ref _primaryKey, value);
        }

        private List<IndexInfo> _indexes = new();
        public List<IndexInfo> Indexes
        {
            get => _indexes;
            set => SetProperty(ref _indexes, value);
        }

        private List<ForeignKeyInfo> _foreignKeys = new();
        public List<ForeignKeyInfo> ForeignKeys
        {
            get => _foreignKeys;
            set => SetProperty(ref _foreignKeys, value);
        }

        private long _rowCount;
        public long RowCount
        {
            get => _rowCount;
            set => SetProperty(ref _rowCount, value);
        }

        private long _migratedCount;
        public long MigratedCount
        {
            get => _migratedCount;
            set => SetProperty(ref _migratedCount, value);
        }
    }

    public class DbView : DbObjectBase
    {
        private string _definition = string.Empty;
        public string Definition
        {
            get => _definition;
            set => SetProperty(ref _definition, value);
        }

        private string _convertedDefinition = string.Empty;
        public string ConvertedDefinition
        {
            get => _convertedDefinition;
            set => SetProperty(ref _convertedDefinition, value);
        }
    }

    public class DbProcedure : DbObjectBase
    {
        private string _objectType = "PROCEDURE"; // PROCEDURE, FUNCTION, PACKAGE, PACKAGE BODY
        public string ObjectType
        {
            get => _objectType;
            set => SetProperty(ref _objectType, value);
        }

        public override string DisplayType => ObjectType;

        private string _sourceCode = string.Empty;
        public string SourceCode
        {
            get => _sourceCode;
            set => SetProperty(ref _sourceCode, value);
        }

        private string _convertedSourceCode = string.Empty;
        public string ConvertedSourceCode
        {
            get => _convertedSourceCode;
            set => SetProperty(ref _convertedSourceCode, value);
        }
    }

    public class ColumnInfo
    {
        public string Name { get; set; } = string.Empty;
        public string OracleType { get; set; } = string.Empty;
        public string PgType { get; set; } = string.Empty;
        public long Length { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public string? DefaultValue { get; set; }
    }

    public class PrimaryKeyInfo
    {
        public string ConstraintName { get; set; } = string.Empty;
        public List<string> ColumnNames { get; set; } = new();
    }

    public class IndexInfo
    {
        public string IndexName { get; set; } = string.Empty;
        public List<string> ColumnNames { get; set; } = new();
        public bool IsUnique { get; set; }
    }

    public class ForeignKeyInfo
    {
        public string ConstraintName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public string ReferenceTable { get; set; } = string.Empty;
        public string ReferenceColumn { get; set; } = string.Empty;
    }

    public class DbSequence : DbObjectBase
    {
        private long _minValue;
        public long MinValue
        {
            get => _minValue;
            set => SetProperty(ref _minValue, value);
        }

        private long _maxValue;
        public long MaxValue
        {
            get => _maxValue;
            set => SetProperty(ref _maxValue, value);
        }

        private long _incrementBy = 1;
        public long IncrementBy
        {
            get => _incrementBy;
            set => SetProperty(ref _incrementBy, value);
        }

        private long _lastNumber = 1;
        public long LastNumber
        {
            get => _lastNumber;
            set => SetProperty(ref _lastNumber, value);
        }

        private string _convertedDdl = string.Empty;
        public string ConvertedDdl
        {
            get => _convertedDdl;
            set => SetProperty(ref _convertedDdl, value);
        }
    }

    public class DbTrigger : DbObjectBase
    {
        private string _tableName = string.Empty;
        public string TableName
        {
            get => _tableName;
            set => SetProperty(ref _tableName, value);
        }

        private string _triggerType = string.Empty;
        public string TriggerType
        {
            get => _triggerType;
            set => SetProperty(ref _triggerType, value);
        }

        private string _triggeringEvent = string.Empty;
        public string TriggeringEvent
        {
            get => _triggeringEvent;
            set => SetProperty(ref _triggeringEvent, value);
        }

        private string _triggerBody = string.Empty;
        public string TriggerBody
        {
            get => _triggerBody;
            set => SetProperty(ref _triggerBody, value);
        }

        private string _convertedTriggerBody = string.Empty;
        public string ConvertedTriggerBody
        {
            get => _convertedTriggerBody;
            set => SetProperty(ref _convertedTriggerBody, value);
        }
    }

    public class DbIndex : DbObjectBase
    {
        private string _tableName = string.Empty;
        public string TableName
        {
            get => _tableName;
            set => SetProperty(ref _tableName, value);
        }

        private List<string> _columnNames = new();
        public List<string> ColumnNames
        {
            get => _columnNames;
            set => SetProperty(ref _columnNames, value);
        }

        private bool _isUnique;
        public bool IsUnique
        {
            get => _isUnique;
            set => SetProperty(ref _isUnique, value);
        }

        private string _convertedDdl = string.Empty;
        public string ConvertedDdl
        {
            get => _convertedDdl;
            set => SetProperty(ref _convertedDdl, value);
        }
    }
}
