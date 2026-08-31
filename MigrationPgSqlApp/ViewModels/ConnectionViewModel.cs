using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MigrationPgSqlApp.Models;
using MigrationPgSqlApp.Services;

namespace MigrationPgSqlApp.ViewModels
{
    public class ConnectionViewModel : ObservableObject
    {
        private readonly OracleDbService _oracleService;
        private readonly PostgresDbService _pgService;

        public ConnectionViewModel(OracleDbService oracleService, PostgresDbService pgService)
        {
            _oracleService = oracleService;
            _pgService = pgService;

            TestOracleCommand = new AsyncRelayCommand(TestOracleConnectionAsync);
            TestPgCommand = new AsyncRelayCommand(TestPgConnectionAsync);
            TestSrcPgCommand = new AsyncRelayCommand(TestSrcPgConnectionAsync);
            LoadConfig();
        }

        // Source Database Type
        private SourceDatabaseType _sourceDbType = SourceDatabaseType.Oracle;
        public SourceDatabaseType SourceDbType
        {
            get => _sourceDbType;
            set
            {
                if (SetProperty(ref _sourceDbType, value))
                {
                    OnPropertyChanged(nameof(IsSourceOracle));
                    OnPropertyChanged(nameof(IsSourcePg));
                    OnPropertyChanged(nameof(IsSourceSuccess));
                }
            }
        }

        public bool IsSourceOracle
        {
            get => SourceDbType == SourceDatabaseType.Oracle;
            set
            {
                if (value) SourceDbType = SourceDatabaseType.Oracle;
            }
        }

        public bool IsSourcePg
        {
            get => SourceDbType == SourceDatabaseType.PostgreSQL;
            set
            {
                if (value) SourceDbType = SourceDatabaseType.PostgreSQL;
            }
        }

        // Oracle Credentials
        private string _oraHost = "localhost";
        public string OraHost
        {
            get => _oraHost;
            set => SetProperty(ref _oraHost, value);
        }

        private int _oraPort = 1521;
        public int OraPort
        {
            get => _oraPort;
            set => SetProperty(ref _oraPort, value);
        }

        private string _oraServiceOrSid = "ORCL";
        public string OraServiceOrSid
        {
            get => _oraServiceOrSid;
            set => SetProperty(ref _oraServiceOrSid, value);
        }

        private bool _oraIsSid = false;
        public bool OraIsSid
        {
            get => _oraIsSid;
            set => SetProperty(ref _oraIsSid, value);
        }

        private string _oraUser = "SYSTEM";
        public string OraUser
        {
            get => _oraUser;
            set => SetProperty(ref _oraUser, value);
        }

        private string _oraPassword = string.Empty;
        public string OraPassword
        {
            get => _oraPassword;
            set => SetProperty(ref _oraPassword, value);
        }

        private string _oraSchema = string.Empty;
        public string OraSchema
        {
            get => _oraSchema;
            set => SetProperty(ref _oraSchema, value);
        }

        // Source PostgreSQL Credentials (when SourceDbType == PostgreSQL)
        private string _srcPgHost = "localhost";
        public string SrcPgHost
        {
            get => _srcPgHost;
            set => SetProperty(ref _srcPgHost, value);
        }

        private int _srcPgPort = 5432;
        public int SrcPgPort
        {
            get => _srcPgPort;
            set => SetProperty(ref _srcPgPort, value);
        }

        private string _srcPgDatabase = "postgres";
        public string SrcPgDatabase
        {
            get => _srcPgDatabase;
            set => SetProperty(ref _srcPgDatabase, value);
        }

        private string _srcPgUser = "postgres";
        public string SrcPgUser
        {
            get => _srcPgUser;
            set => SetProperty(ref _srcPgUser, value);
        }

        private string _srcPgPassword = string.Empty;
        public string SrcPgPassword
        {
            get => _srcPgPassword;
            set => SetProperty(ref _srcPgPassword, value);
        }

        private string _srcPgSchema = "public";
        public string SrcPgSchema
        {
            get => _srcPgSchema;
            set => SetProperty(ref _srcPgSchema, value);
        }

        private string _srcPgStatus = "Disconnected";
        public string SrcPgStatus
        {
            get => _srcPgStatus;
            set => SetProperty(ref _srcPgStatus, value);
        }

        private bool _isSrcPgSuccess = false;
        public bool IsSrcPgSuccess
        {
            get => _isSrcPgSuccess;
            set
            {
                if (SetProperty(ref _isSrcPgSuccess, value))
                {
                    OnPropertyChanged(nameof(IsSourceSuccess));
                }
            }
        }

        // Target PostgreSQL Credentials
        private string _pgHost = "localhost";
        public string PgHost
        {
            get => _pgHost;
            set => SetProperty(ref _pgHost, value);
        }

        private int _pgPort = 5432;
        public int PgPort
        {
            get => _pgPort;
            set => SetProperty(ref _pgPort, value);
        }

        private string _pgDatabase = "postgres";
        public string PgDatabase
        {
            get => _pgDatabase;
            set => SetProperty(ref _pgDatabase, value);
        }

        private string _pgUser = "postgres";
        public string PgUser
        {
            get => _pgUser;
            set => SetProperty(ref _pgUser, value);
        }

        private string _pgPassword = string.Empty;
        public string PgPassword
        {
            get => _pgPassword;
            set => SetProperty(ref _pgPassword, value);
        }

        private string _pgSchema = "public";
        public string PgSchema
        {
            get => _pgSchema;
            set => SetProperty(ref _pgSchema, value);
        }

        // Connection States
        private string _oraStatus = "Disconnected";
        public string OraStatus
        {
            get => _oraStatus;
            set => SetProperty(ref _oraStatus, value);
        }

        private string _pgStatus = "Disconnected";
        public string PgStatus
        {
            get => _pgStatus;
            set => SetProperty(ref _pgStatus, value);
        }

        private bool _isOraSuccess = false;
        public bool IsOraSuccess
        {
            get => _isOraSuccess;
            set
            {
                if (SetProperty(ref _isOraSuccess, value))
                {
                    OnPropertyChanged(nameof(IsSourceSuccess));
                }
            }
        }

        private bool _isPgSuccess = false;
        public bool IsPgSuccess
        {
            get => _isPgSuccess;
            set => SetProperty(ref _isPgSuccess, value);
        }

        public bool IsSourceSuccess => SourceDbType == SourceDatabaseType.Oracle ? IsOraSuccess : IsSrcPgSuccess;

        public IAsyncRelayCommand TestOracleCommand { get; }
        public IAsyncRelayCommand TestPgCommand { get; }
        public IAsyncRelayCommand TestSrcPgCommand { get; }

        public string GetOracleConnectionString()
        {
            return _oracleService.BuildConnectionString(OraHost, OraPort, OraServiceOrSid, OraIsSid, OraUser, OraPassword);
        }

        public string GetSourcePgConnectionString()
        {
            return _pgService.BuildConnectionString(SrcPgHost, SrcPgPort, SrcPgDatabase, SrcPgUser, SrcPgPassword);
        }

        public string GetSourceConnectionString()
        {
            return SourceDbType == SourceDatabaseType.Oracle ? GetOracleConnectionString() : GetSourcePgConnectionString();
        }

        public string GetSourceSchema()
        {
            return SourceDbType == SourceDatabaseType.Oracle ? OraSchema : SrcPgSchema;
        }

        public string GetPgConnectionString()
        {
            return _pgService.BuildConnectionString(PgHost, PgPort, PgDatabase, PgUser, PgPassword);
        }

        private async Task TestOracleConnectionAsync()
        {
            OraStatus = "Connecting...";
            IsOraSuccess = false;

            string connStr = GetOracleConnectionString();
            string error = string.Empty;

            bool success = await Task.Run(() => _oracleService.TestConnection(connStr, out error));

            if (success)
            {
                OraStatus = "Connected Successfully!";
                IsOraSuccess = true;
            }
            else
            {
                OraStatus = $"Connection Failed: {error}";
                IsOraSuccess = false;
            }
        }

        private async Task TestSrcPgConnectionAsync()
        {
            SrcPgStatus = "Connecting...";
            IsSrcPgSuccess = false;

            string connStr = GetSourcePgConnectionString();
            string error = string.Empty;

            bool success = await Task.Run(() => _pgService.TestConnection(connStr, out error));

            if (success)
            {
                SrcPgStatus = "Connected Successfully!";
                IsSrcPgSuccess = true;
            }
            else
            {
                SrcPgStatus = $"Connection Failed: {error}";
                IsSrcPgSuccess = false;
            }
        }

        private async Task TestPgConnectionAsync()
        {
            PgStatus = "Connecting...";
            IsPgSuccess = false;

            string connStr = GetPgConnectionString();
            string error = string.Empty;

            bool success = await Task.Run(() => _pgService.TestConnection(connStr, out error));

            if (success)
            {
                PgStatus = "Connected Successfully!";
                IsPgSuccess = true;
            }
            else
            {
                PgStatus = $"Connection Failed: {error}";
                IsPgSuccess = false;
            }
        }

        private void LoadConfig()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db_config.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var config = JsonSerializer.Deserialize<DbConfig>(json);
                    if (config != null)
                    {
                        SourceDbType = config.SourceDbType;

                        OraHost = config.OraHost;
                        OraPort = config.OraPort;
                        OraServiceOrSid = config.OraServiceOrSid;
                        OraIsSid = config.OraIsSid;
                        OraUser = config.OraUser;
                        OraPassword = config.OraPassword;
                        OraSchema = config.OraSchema;

                        SrcPgHost = string.IsNullOrEmpty(config.SrcPgHost) ? "localhost" : config.SrcPgHost;
                        SrcPgPort = config.SrcPgPort == 0 ? 5432 : config.SrcPgPort;
                        SrcPgDatabase = string.IsNullOrEmpty(config.SrcPgDatabase) ? "postgres" : config.SrcPgDatabase;
                        SrcPgUser = string.IsNullOrEmpty(config.SrcPgUser) ? "postgres" : config.SrcPgUser;
                        SrcPgPassword = config.SrcPgPassword ?? string.Empty;
                        SrcPgSchema = string.IsNullOrEmpty(config.SrcPgSchema) ? "public" : config.SrcPgSchema;

                        PgHost = config.PgHost;
                        PgPort = config.PgPort;
                        PgDatabase = config.PgDatabase;
                        PgUser = config.PgUser;
                        PgPassword = config.PgPassword;
                        PgSchema = config.PgSchema;
                    }
                }
            }
            catch { }
        }

        public void SaveConfig()
        {
            try
            {
                var config = new DbConfig
                {
                    SourceDbType = SourceDbType,

                    OraHost = OraHost,
                    OraPort = OraPort,
                    OraServiceOrSid = OraServiceOrSid,
                    OraIsSid = OraIsSid,
                    OraUser = OraUser,
                    OraPassword = OraPassword,
                    OraSchema = OraSchema,

                    SrcPgHost = SrcPgHost,
                    SrcPgPort = SrcPgPort,
                    SrcPgDatabase = SrcPgDatabase,
                    SrcPgUser = SrcPgUser,
                    SrcPgPassword = SrcPgPassword,
                    SrcPgSchema = SrcPgSchema,

                    PgHost = PgHost,
                    PgPort = PgPort,
                    PgDatabase = PgDatabase,
                    PgUser = PgUser,
                    PgPassword = PgPassword,
                    PgSchema = PgSchema
                };
                string json = JsonSerializer.Serialize(config);
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db_config.json");
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }

    public class DbConfig
    {
        public SourceDatabaseType SourceDbType { get; set; } = SourceDatabaseType.Oracle;

        public string OraHost { get; set; } = string.Empty;
        public int OraPort { get; set; }
        public string OraServiceOrSid { get; set; } = string.Empty;
        public bool OraIsSid { get; set; }
        public string OraUser { get; set; } = string.Empty;
        public string OraPassword { get; set; } = string.Empty;
        public string OraSchema { get; set; } = string.Empty;

        public string SrcPgHost { get; set; } = "localhost";
        public int SrcPgPort { get; set; } = 5432;
        public string SrcPgDatabase { get; set; } = "postgres";
        public string SrcPgUser { get; set; } = "postgres";
        public string SrcPgPassword { get; set; } = string.Empty;
        public string SrcPgSchema { get; set; } = "public";

        public string PgHost { get; set; } = string.Empty;
        public int PgPort { get; set; }
        public string PgDatabase { get; set; } = string.Empty;
        public string PgUser { get; set; } = string.Empty;
        public string PgPassword { get; set; } = string.Empty;
        public string PgSchema { get; set; } = string.Empty;
    }
}
