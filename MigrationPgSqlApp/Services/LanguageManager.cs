using CommunityToolkit.Mvvm.ComponentModel;

namespace MigrationPgSqlApp.Services
{
    public class LanguageManager : ObservableObject
    {
        private static LanguageManager? _instance;
        public static LanguageManager Instance => _instance ??= new LanguageManager();

        private int _selectedLanguageIndex = 0; // 0 = 简体中文, 1 = English
        public int SelectedLanguageIndex
        {
            get => _selectedLanguageIndex;
            set
            {
                if (SetProperty(ref _selectedLanguageIndex, value))
                {
                    OnPropertyChanged(string.Empty); // Notify all properties bound to Lang
                }
            }
        }

        public bool IsChinese => SelectedLanguageIndex == 0;

        // Header & Status
        public string AppTitle => IsChinese ? "Antigravity 异构数据库迁移工作站" : "Antigravity Migration Studio";
        public string AppSubtitle => IsChinese ? "Oracle / PostgreSQL ➔ PostgreSQL 自动化迁移" : "Oracle / PostgreSQL ➔ PostgreSQL";
        public string SourceDbHeader => IsChinese ? "源数据库" : "Source DB";
        public string TargetDbHeader => IsChinese ? "目标 PostgreSQL" : "Target PostgreSQL";

        // Tabs
        public string Tab1Header => IsChinese ? "数据库连接配置" : "Connections";
        public string Tab2Header => IsChinese ? "元数据导航与 DDL 预览" : "Metadata Navigator";
        public string Tab3Header => IsChinese ? "迁移管道执行" : "Migration Execution";

        // Tab 1 Connections
        public string SourceDbPanelHeader => IsChinese ? "源数据库配置" : "Source Database";
        public string TargetDbPanelHeader => IsChinese ? "目标 PostgreSQL 数据库配置" : "PostgreSQL Target Database";
        public string TestOraBtn => IsChinese ? "测试 Oracle 连接" : "Test Oracle Connection";
        public string TestSrcPgBtn => IsChinese ? "测试源 PostgreSQL 连接" : "Test Source PostgreSQL Connection";
        public string TestPgBtn => IsChinese ? "测试 PostgreSQL 连接" : "Test PostgreSQL Connection";
        public string ConnectLoadBtn => IsChinese ? "连接并加载元数据" : "Connect and Load Metadata";

        // Tab 2 Navigator
        public string ChecklistHeader => IsChinese ? "数据库迁移对象清单" : "Database Objects Checklist";
        public string AllBtn => IsChinese ? "全选" : "All";
        public string NoneBtn => IsChinese ? "全不选" : "None";
        public string TablesHeader => IsChinese ? "数据表 (Tables)" : "Tables";
        public string ViewsHeader => IsChinese ? "视图 (Views)" : "Views";
        public string SequencesHeader => IsChinese ? "序列 (Sequences)" : "Sequences";
        public string IndexesHeader => IsChinese ? "索引 (Indexes)" : "Indexes";
        public string ProceduresHeader => IsChinese ? "存储过程与包 (Procedures & Packages)" : "Procedures & Packages";
        public string TriggersHeader => IsChinese ? "触发器 (Triggers)" : "Triggers";
        public string OriginalCodeHeader => IsChinese ? "原始 Oracle 源码 (Original Source SQL)" : "Original Source SQL";
        public string ConvertedCodeHeader => IsChinese ? "转换后的 PostgreSQL DDL (可在线修改)" : "Converted PostgreSQL DDL (Editable)";
        public string EditNote => IsChinese ? "* 您可以直接人工修改 PostgreSQL 语法，微调后的脚本将自动保存并应用于实际迁移。" : "* You can manually adjust PostgreSQL syntax. Edits are saved and migrated.";
        public string ColumnMappingHeader => IsChinese ? "字段映射明细预览" : "Column Mapping Preview";
        public string GeneratedDdlHeader => IsChinese ? "生成的 PostgreSQL 建表 DDL" : "Generated PostgreSQL DDL";

        // Column DataGrid
        public string ColName => IsChinese ? "字段名称" : "Column Name";
        public string ColOraType => IsChinese ? "Oracle 类型" : "Oracle Type";
        public string ColLength => IsChinese ? "长度/精度" : "Length/Scale";
        public string ColPgType => IsChinese ? "PostgreSQL 类型" : "PostgreSQL Type";
        public string ColNullable => IsChinese ? "可空" : "Nullable";
        public string ColPK => IsChinese ? "主键" : "PK";
        public string ColDefault => IsChinese ? "默认值" : "Default";

        // Tab 3 Execution
        public string OptionsHeader => IsChinese ? "迁移选项配置" : "Migration Options";
        public string MigrateSchemaText => IsChinese ? "迁移表结构与视图" : "Migrate Table Structures / Views";
        public string MigrateSeqsText => IsChinese ? "迁移数据库序列" : "Migrate Database Sequences";
        public string MigrateIdxsText => IsChinese ? "迁移非主键索引" : "Migrate Non-PK Indexes";
        public string MigrateDataText => IsChinese ? "迁移表数据 (Bulk Copy)" : "Migrate Table Data (Bulk Copy)";
        public string TruncateText => IsChinese ? "导入前先清空目标表" : "Truncate Target Tables First";
        public string MigrateProcsText => IsChinese ? "迁移存储过程、函数与包" : "Migrate Procedures, Functions & Packages";
        public string MigrateTrigsText => IsChinese ? "迁移数据库触发器" : "Migrate Database Triggers";
        public string StatusListHeader => IsChinese ? "各对象迁移状态" : "Objects Migration Status";
        public string RunPipelineBtn => IsChinese ? "启动迁移流水线" : "Run Migration Pipeline";
        public string CancelBtn => IsChinese ? "取消迁移操作" : "Cancel Operation";
        public string SuccessText => IsChinese ? "成功: " : "Success: ";
        public string FailedText => IsChinese ? "失败: " : "Failed: ";
        public string ViewDetailsText => IsChinese ? "(查看明细)" : "(View Details)";
        public string LogsHeader => IsChinese ? "迁移管道日志" : "Migration Pipeline Logs";
        public string ReadingMetadataText => IsChinese ? "正在从源数据库中读取元数据..." : "Reading database metadata from Oracle...";

        // Failure Details Window
        public string FailuresWinTitle => IsChinese ? "迁移失败详情" : "Migration Failure Details";
        public string FailuresListHeader => IsChinese ? "失败对象列表" : "Failed Objects List";
        public string SearchWatermark => IsChinese ? "搜索对象名称或错误关键字..." : "Search object name or error...";
        public string DetailedReasonHeader => IsChinese ? "详细错误日志与原因:" : "Detailed Error Reason:";
        public string CopyErrorBtn => IsChinese ? "复制错误信息" : "Copy Error Info";
        public string CloseBtn => IsChinese ? "关闭" : "Close";
    }
}
