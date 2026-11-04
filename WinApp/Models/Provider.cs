using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using System.Data.SqlClient;
using System.Reflection;
using System.Collections;

namespace System
{
    using TBL = DataSchema.Table;
    using COL = DataSchema.Column;
    using PRO = DataSchema.StoredProc;
    using PAR = DataSchema.ProcParam;

    public class CodeGenerator
    {
        static Dictionary<string, string> sql_types = new Dictionary<string, string> {
            { "varbinary", "byte[]" },
            { "binary", "byte[]" },
            { "uniqueidentifier", "Guid" },
            { "rowversion", "byte[]" },
            { "bit", "bool" },
            { "tinyint", "byte" },
            { "smallint", "short" },
            { "int", "int" },
            { "bigint", "long" },
            { "smallmoney", "decimal" },
            { "money", "decimal" },
            { "numeric", "decimal" },
            { "decimal", "decimal" },
            { "real", "double" },
            { "float", "double" },
            { "smalldatetime", "DateTime" },
            { "datetime", "DateTime" },
            { "date", "DateTime" },
            { "sql_variant", "object" },
        };
        static string indent;
        static int tab;

        static public string GetCShapType(string sqlType)
        {
            string s;
            sql_types.TryGetValue(sqlType, out s);

            return s ?? "string";
        }

        public class Line
        {
            protected string content = string.Empty;
            public Line Add(string s)
            {
                content += $"{indent}{s}\r\n";
                return this;
            }
            public Line Tab(int n)
            {
                tab += n;
                if (tab < 0) tab = 0;
                indent = new string(' ', tab << 2);
                return this;
            }
            public override string ToString()
            {
                return content;
            }
        }
        public abstract class Block : Line
        {
            public virtual string Title
            {
                get
                {
                    var name = GetType().Name;
                    var res = string.Empty;
                    foreach (char c in name)
                    {
                        if (res != string.Empty && char.IsUpper(c))
                            res += ' ';
                        res += c;
                    }
                    return res;
                }
            }
            protected virtual string get_open_text() => "{";
            protected virtual string get_close_text() => "}";
            public Block Open()
            {
                Add(get_open_text());
                return (Block)Tab(1);
            }
            public Block Close()
            {
                Tab(-1);
                return (Block)Add(get_close_text());
            }
            public override string ToString()
            {
                End();
                return base.ToString();
            }
            public Block Start(string text)
            {
                Add(text);
                return Open();
            }
            public Block End()
            {
                while (tab > 0) Close();
                return this;
            }
            public Block CreateBlock(string firstLine, Action renderBody)
            {
                Add(firstLine);
                Open();
                renderBody();
                Close();
                return this;
            }
            public virtual Block CreateBlock(TBL source)
            {
                return CreateBlock(GetFirstLine(source), () => source.ForEach(c => RenderColumn(c)));
            }
            protected virtual string GetFirstLine(TBL source) => source.Name;
            protected virtual void RenderColumn(COL col) { }
        }
        class ControllerEngine : Block
        {
            public override string Title => "Controller";
            public override string ToString()
            {
                return "using Models;\r\n" + base.ToString();
            }
            public override Block CreateBlock(TBL source)
            {
                Start($"namespace {GetType().Assembly.GetName().Name}.Controllers");
                base.CreateBlock(source);
                return End();
            }
            protected override string GetFirstLine(TBL source)
            {
                return $"public partial class {source.Name}Controller : DataController<{source.Name}>";
            }

            protected override void RenderColumn(COL col)
            {
            }
        }
        class CShapModel : Block
        {
            protected string GetColumnType(COL col)
            {
                string t;
                if (!sql_types.TryGetValue(col.Type, out t))
                    return "string";
                    
                return t + '?';
            }
            protected override void RenderColumn(COL col) => Add($"public {GetColumnType(col)} {col.Name} " + "{ get; set; }");
            protected override string GetFirstLine(TBL source) => $"public partial class {source.Name}";

            public override string Title => "Model";
            public override Block CreateBlock(TBL source)
            {
                Start("namespace Models");
                base.CreateBlock(source);
                return End();
            }
        }
        class SqlBlock : Block
        {
            protected override string get_open_text() => "BEGIN";
            protected override string get_close_text() => "END";

            public SqlBlock Go()
            {
                Tab(-tab);
                return (SqlBlock)Add("GO");
            }
        }
        class StoredProcedure : SqlBlock
        {
            abstract class IFBlock : SqlBlock
            {
                protected abstract void CreateBody(TBL source);
                protected virtual void End(TBL source)
                {
                    var n = source.PrimaryKey.Name;
                    Add($"WHERE {n} = @{n}");
                }
                public string Create(int index, TBL source)
                {
                    CreateBlock($"IF @action = {index - 1}", () => {
                        CreateBody(source);
                        if (source.PrimaryKey != null)
                        {
                            End(source);
                        }
                        Add("return");
                    });
                    return content;
                }
            }
            class Insert : IFBlock
            {
                protected override void CreateBody(TBL source)
                {
                    List<string> vals = new List<string>();

                    Add($"INSERT INTO {source.Name} VALUES").Tab(1);
                    source.ForEach(c => {
                        if (c != source.Identity) vals.Add('@' + c.Name);
                    });
                    Add($"({string.Join(", ", vals)})").Tab(-1);
                }
                protected override void End(TBL source)
                {
                    if (source.Identity != null)
                        Add($"SET @{source.Identity.Name} = @@IDENTITY");
                }
            }
            class Update : IFBlock
            {
                protected override void CreateBody(TBL source)
                {
                    var vals = new List<string>();
                    Add($"UPDATE {source.Name} SET").Tab(1);
                    source.ForEach(c => {
                        if (c != source.PrimaryKey) 
                            vals.Add($"{indent}{c.Name} = @{c.Name}");
                    });
                    content += string.Join(",\r\n", vals) + "\r\n";
                    Tab(-1);
                }
            }
            class Delete : IFBlock
            {
                protected override void CreateBody(TBL source)
                {
                    Add("DELETE FROM " + source.Name);
                }
            }

            IFBlock[] childs = new IFBlock[] { 
                new Delete(),
                new Update(),
                new Insert(),
            };
            public override Block CreateBlock(TBL source)
            {
                string procName = "update" + source.Name;
                Add("IF NOT (SELECT CREATED FROM INFORMATION_SCHEMA.ROUTINES "
                    + $"WHERE ROUTINE_NAME='{procName}') IS NULL")
                    .Tab(1).Add($"drop proc {procName}");
                
                Go().Add($"create proc {procName}")
                    .Add("( @action int");

                source.ForEach(col => {
                    var s = $", @{col.Name} {col.Type}";
                    if (!string.IsNullOrEmpty(col.DataSize))
                        s += $"({col.DataSize})";
                    s += col == source.Identity ? " output" : " = NULL";
                    Add(s);
                });

                Start(") AS");

                for (int i = 0; i < childs.Length; i++)
                {
                    content += childs[i].Create(i, source);
                }
                End();
                return Go();
            }

        }
        public string Mode { get; set; }
        
        Block CreateEngine()
        {
            switch (char.ToLower(Mode[0]))
            {
                case 'm': return new CShapModel();
                case 'p': return new StoredProcedure();
                case 'c': return new ControllerEngine();
            }
            return null;
        }
        public Block Run(TBL table)
        {
            var engine = CreateEngine();
            if (engine != null)
            {
                IEnumerable<TBL> lst = Provider.Current.Schema.Tables.Values;
                if (table != null)
                {
                    lst = new List<TBL> { table };
                }
                foreach (TBL e in lst.OrderBy(x => x.Name))
                {
                    engine.CreateBlock(e);
                }
            }
            return engine;
        }
    }
    public class DataSchema
    {        
        #region Migrate
        public class SqlObject
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public object Value { get; set; }
        }
        public class SqlObjectCollection<T> : Dictionary<string, T> where T: SqlObject
        {
            public void Add(T one) => base.Add(one.Name.ToLower(), one);
            public void Find(string name, Action<T> callback)
            {
                var one = this[name];
                if (one != null)
                    callback(one);
            }
            new public T this[string name]
            {
                get
                {
                    T one;
                    TryGetValue(name.ToLower(), out one);
                    return one;
                }
            }
        }
        public abstract class ColumnBase : SqlObject
        {
            public int Position { get; set; }
            public string DataSize { get; set; }
        }
        public interface ITable
        {
            void CreateItem(SqlDataReader r);
        }
        public abstract class TableBase<T> : SqlObject, ITable
            where T : ColumnBase, new()
        {
            protected SqlObjectCollection<T> childs { get; private set; } = new SqlObjectCollection<T>();
            public void CreateItem(SqlDataReader r)
            {
                var i = new T();
                i.Name = r.GetString(1);
                i.Type = sql_data_type[r.GetInt32(2)];

                int max_length = r.GetInt16(3);
                int precision = r.GetByte(4);
                int scale = r.GetByte(5);
                
                SetItemExt(i, r.GetBoolean(6));

                if (precision != 0 && scale != 0)
                {
                    i.DataSize = $"{precision}, {scale}";
                }
                else if (i.Type.Contains("char"))
                {
                    i.DataSize = $"{max_length}";
                }
                i.Position = r.GetInt32(7);

                childs.Add(i);
            }
            protected abstract void SetItemExt(T i, bool b);

            public T this[string name] => childs[name];
            public T this[int index]
            {
                get
                {
                    foreach (var e in childs.Values)
                    {
                        if (e.Position == index)
                            return e;
                    }
                    return null;
                }
            }
            public void ForEach(Action<T> callback)
            {
                foreach (T i in childs.Values.OrderBy(x => x.Position)) callback(i);
            }
        }
        public class Column : ColumnBase
        {
            public bool IsNullable { get; set; }
            public string GetUpdateValue(object value)
            {
                if (value == null)
                    return "NULL";

                var s = $"'{value}'";
                if (value is string)
                    s = 'N' + s;
                return s;
            }
            public string GetUpdateEntityValue(Type type, object entity)
            {
                var p = type.GetProperty(Name);
                return GetUpdateValue(p?.GetValue(entity));
            }
            public string GetUpdateEntityValue(object entity) => GetUpdateEntityValue(entity.GetType(), entity);
        }
        public class Table : TableBase<COL>
        {
            protected override void SetItemExt(COL i, bool b)
            {
                if (b) Identity = i;
            }
            public COL Identity { get; set; }
            public COL PrimaryKey { get; set; }
            public SqlObjectCollection<COL> Columns => childs;

            #region CREATE SQL
            // Hàm tạo câu lệnh select
            public string CreateSelectSql(bool distinct, string columns, string filter, string order)
            {
                if (columns == null)
                    columns = "*";
                else if (distinct)
                    columns = "DISTINCT " + columns;

                var s = "SELECT " + columns + " FROM " + Name;
                if (filter != null)
                    s += " WHERE " + filter;
                if (order != null)
                    s += " ORDER BY " + order;

                return s;
            }

            // Hàm tạo câu lệnh select
            public string CreateSelectSql(string columns, string filter, string order) => 
                CreateSelectSql(false, columns, filter, order);
            #endregion

            public string CreateInsertSql(object entity)
            {
                var engine = new CodeGenerator.Line();
                engine.Add($"INSERT INTO {Name} VALUES");

                var vals = new List<string>();
                var type = entity.GetType();
                ForEach(c => {
                    if (c != Identity)
                    {
                        vals.Add(c.GetUpdateEntityValue(type, entity));
                    }
                });
                return engine.Add($"({string.Join(", ", vals)})").ToString();
            }
            public string CreateUpdateSql(object entity)
            {
                var engine = new CodeGenerator.Line();
                engine.Add($"UPDATE {Name}").Tab(1);

                var type = entity.GetType();
                var first = true;
                ForEach(c => {
                    if (c != PrimaryKey)
                    {
                        engine.Add($"{(first ? "SET" : ", ")} {c.Name} = {c.GetUpdateEntityValue(type, entity)}");
                        first = false;
                    }
                });
                engine.Add($"WHERE {PrimaryKey.Name} = {PrimaryKey.GetUpdateEntityValue(type, entity)}");
                return engine.ToString();
            }
            public string CreateDeleteSql(object entity)
            {
                return $"DELETE FROM {Name} WHERE {PrimaryKey.Name} = {PrimaryKey.GetUpdateEntityValue(entity)}";
            }

            // Hàm đọc giá trị của một cột trong bản ghi tìm theo điều kiện
            public object GetValue(string column, string filter)
            {
                object v = null;
                Provider.CreateCommand(cmd => {
                    cmd.CommandText = CreateSelectSql(column, filter, null);
                    v = cmd.ExecuteScalar();
                });
                return v;
            }
            
            // Hàm đọc giá trị của một cột trong bản ghi tìm theo khóa chính
            public object GetValueById(string column, object id)
            {
                return GetValue(column ?? "*", $"{PrimaryKey.Name} = '{id}'");
            }

            // Hàm đọc giá trị tự tăng
            public int GetIdentity()
            {
                object value = null;
                Provider.CreateCommand(cmd => {
                    cmd.CommandText = $"SELECT CAST(IDENT_CURRENT('{Name}') as int)";
                    value = cmd.ExecuteScalar();
                });
                return value == null ? 0 : (int)value;
            }


            // Hàm đọc tất cả các giá trị của một cột
            public List<object> ToList(string column)
            {
                var lst = new List<object>();
                var sql = CreateSelectSql(true, column, null, null);
                Provider.CreateReader(sql, r => {
                    lst.Add(r.GetValue(0));
                });
                return lst;
            }
            
            // Hàm tạo từ điển dữ liệu của một cột
            public IDictionary SelectDictionary(string column)
            {
                var lst = new Dictionary<object, object>();
                var sql = CreateSelectSql(false, column + "," + PrimaryKey.Name, null, null);
                Provider.CreateReader(sql, r => {
                    lst.Add(r.GetValue(1), r.GetValue(0));
                });
                return lst;
            }

            #region Generics
            // Hàm tìm kiếm một thực thể theo khóa chính
            public T Find<T>(object id)
            {
                var lst = ToList<T>(null, $"{PrimaryKey.Name} = '{id}'");
                if (lst.Count == 0)
                    return default(T);

                return lst[0];
            }

            // Hàm tạo từ điển các thực thể
            public Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(Func<TValue, TKey> key)
            {
                var map = new Dictionary<TKey, TValue>();
                foreach (var e in ToList<TValue>(null, null))
                {
                    map.Add(key(e), e);
                }
                return map;
            }

            // Hàm đọc danh sách các thực thể
            public List<T> ToList<T>(string columns, string filter)
            {
                var res = Provider.Load(CreateSelectSql(columns, filter, null));
                var lst = new List<T>();

                var typ = typeof(T);

                var cols = new Dictionary<PropertyInfo, DataColumn>();
                foreach (DataColumn c in res.Columns)
                {
                    var prop = typ.GetProperty(c.ColumnName);
                    if (prop != null && prop.CanWrite)
                    {
                        cols.Add(prop, c);
                    }
                }

                foreach (DataRow r in res.Rows)
                {
                    var e = (T)Activator.CreateInstance(typ);
                    foreach (var p in cols)
                    {
                        object v = r[p.Value];
                        if (v != DBNull.Value)
                        {
                            p.Key.SetValue(e, r[p.Value]);
                        }
                    }
                    lst.Add(e);
                }
                return lst;
            }
            #endregion
        }

        public class ProcParam : ColumnBase
        {
            public bool IsOutput { get; set; }
        }
        public class StoredProc : TableBase<PAR> 
        {
            public SqlObjectCollection<PAR> Parameters => childs;
            protected override void SetItemExt(PAR i, bool b)
            {
                i.Name = i.Name.Substring(1);
                i.IsOutput = b;
            }
        }

        static Dictionary<int, string> sql_data_type;
        #endregion
        public void ForEach(int delay, Action<TBL> callback, Action completed)
        {
            var lst = Tables.Values.OrderBy(x => x.Name);
            var ts = new Threading.ThreadStart(() => {
                foreach (TBL table in lst)
                {
                    callback(table);
                    if (delay > 0) Threading.Thread.Sleep(delay);
                }
                completed?.Invoke();
            });
            new Threading.Thread(ts).Start();
        }
        public void ForEach(Action<TBL> callback)
        {
            foreach (TBL table in Tables.Values)
            {
                callback(table);
            }
        }
        public SqlObjectCollection<TBL> Tables { get; set; }
        public SqlObjectCollection<PRO> Procs { get; set; }

        public void Select(string sql, Action<DataRow> callback)
        {
            var dt = Provider.Load(sql);
            foreach (DataRow r in dt.Rows)
                callback(r);
        }
        public DataSchema Migrate()
        {
            #region DATA TYPES
            if (sql_data_type == null)
            {
                sql_data_type = new Dictionary<int, string>();
                Provider.CreateReader("select xusertype, name from sys.systypes", r => {
                    sql_data_type.Add(r.GetInt16(0), r.GetString(1));
                });
            }
            #endregion

            var object_map = new Dictionary<int, SqlObject>();
            Func<string, SqlObject> create_entity = type => { 
                switch (type) 
                {
                    case "P": return new StoredProc();
                    case "V":
                    case "U": return new TBL();
                }
                return null;
            };

            Tables = new SqlObjectCollection<TBL>();
            Procs = new SqlObjectCollection<PRO>();

            int min_id = int.MaxValue, max_id = int.MinValue;
            var object_sql = "select object_id, name, type from sys.objects where not type in ('S', 'SQ', 'IT')";

            Action<SqlDataReader, SqlObject> set_entity = (r, e) => {
                int id = r.GetInt32(0);
                e.Name = r.GetString(1);
                object_map.Add(id, e);

                if (id < min_id) min_id = id;
                if (id > max_id) max_id = id;

                if (e is TBL) Tables.Add((TBL)e);
                else if (e is PRO) Procs.Add((PRO)e);
            };
            Provider.CreateReader(object_sql, r => {
                var type = r.GetString(2).Trim();
                var e = create_entity(type);
                if (e != null)
                {
                    e.Type = type;
                    set_entity(r, e);
                }
            });

            var filter_id = $" WHERE object_id BETWEEN {min_id} AND {max_id}";
            var columns = "select object_id, name, user_type_id, max_length, precision, scale";
            var columns_sql = $"{columns}, is_identity as ext, column_id as pos from sys.columns {filter_id}"
                + $" union {columns}, is_output as ext, parameter_id as pos from sys.parameters {filter_id}";

            Provider.CreateReader(columns_sql, r => {
                int id = r.GetInt32(0);
                SqlObject o;
                if (object_map.TryGetValue(id, out o))
                {
                    ((ITable)o).CreateItem(r);
                }
            });


            var index_sql = "select object_id, index_id from sys.index_columns" + filter_id;
            Provider.CreateReader(index_sql, r => {
                SqlObject o;
                if (object_map.TryGetValue(r.GetInt32(0), out o))
                {
                    var t = (TBL)o;
                    t.PrimaryKey = t[r.GetInt32(1)];
                }
            });

            
            return this;
        }
    }
    public class Provider
    {
        static public event Action<string> ConnectionError;
        static public event Action<string> CommandError;

        static public Provider Current { get; private set; }
        public Provider()
        {
            Current = this;
        }

        DataSchema _schema;
        public DataSchema Schema
        {
            get
            {
                if (_schema == null)
                    _schema = new DataSchema();
                return _schema;
            }
        }
        public string HostName { get; set; } = @"LOCALHOST\SQLEXPRESS";
        public string DatabaseName { get; set; } = @"KTPM";
        public string ConnectionString => $"Data Source={HostName};Initial Catalog={DatabaseName};Integrated Security=True";
        static public void CreateCommand(Action<SqlCommand> callback)
        {
            SqlConnection conn = new SqlConnection(Current.ConnectionString);
            try
            {
                conn.Open();
            }
            catch
            {
                ConnectionError?.Invoke(conn.ConnectionString);
                return;
            }

            var cmd = new SqlCommand { Connection = conn };
            try
            {
                callback(cmd);
            }
            catch
            {
                CommandError?.Invoke(cmd.CommandText);
            }
            finally
            {
                if (conn.State == ConnectionState.Open)
                    conn.Close();
            }
        }
        static public void CreateReader(string sql, Action<SqlDataReader> callback)
        {
            CreateCommand(cmd => {
                cmd.CommandText = sql;
                var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    callback.Invoke(reader);
                }
                reader.Close();
            });
        }
        /// <summary>
        /// Đọc dữ liệu từ table
        /// </summary>
        /// <param name="sql">Câu lệnh SQL (select)</param>
        /// <returns>Đối tượng kiểu DataTable</returns>
        static public DataTable Load(string sql)
        {
            DataTable table = new DataTable();
            CreateCommand(cmd => {
                cmd.CommandText = sql;
                var reader = cmd.ExecuteReader();
                table.BeginLoadData();
                table.Load(reader);

                reader.Close();
                table.EndLoadData();
            });

            return table;
        }
        static public TBL GetTable(string name) => Current.Schema.Tables[name];
        static public TBL GetTable<T>() => GetTable(typeof(T).Name);
        static public List<T> Select<T>()
        {
            return GetTable<T>().ToList<T>(null, null);
        }
        static public List<object> GetValueList(string tableName, string valueName)
        {
            var t = GetTable(tableName);
            var lst = new List<object>();

            CreateCommand(cmd => {

                var fields = valueName;
                if (t.PrimaryKey != null)
                    fields +=  "," + t.PrimaryKey.Name;

                cmd.CommandText = t.CreateSelectSql(fields, null, valueName);
                var rd = cmd.ExecuteReader();
                if (fields == valueName)
                {
                    while (rd.Read())
                    {
                        var val = rd.GetValue(0);
                        lst.Add(val);
                    }
                }
                while (rd.Read())
                {
                    var val = rd.GetValue(0);
                    var key = rd.GetValue(1);
                    lst.Add(new KeyValuePair<object, object>(key, val));
                }
            });
            return lst;
        }

        static public PRO GetStoredProcedure(string name) => Current.Schema.Procs[name];
    }
}
