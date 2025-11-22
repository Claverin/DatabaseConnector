using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FirebirdSql.Data.FirebirdClient;
using DotNetEnv;

namespace DbMetaTool
{
    public static class Program
    {
        // Przykładowe wywołania:
        // DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"
        // DbMetaTool export-scripts --connection-string "..." --output-dir "C:\out"
        // DbMetaTool update-db --connection-string "..." --scripts-dir "C:\scripts"
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Użycie:");
                Console.WriteLine("  build-db --db-dir <ścieżka> --scripts-dir <ścieżka>");
                Console.WriteLine("  export-scripts --connection-string <connStr> --output-dir <ścieżka>");
                Console.WriteLine("  update-db --connection-string <connStr> --scripts-dir <ścieżka>");
                return 1;
            }

            Env.Load();

            try
            {
                var command = args[0].ToLowerInvariant();

                switch (command)
                {
                    case "build-db":
                        {
                            string dbDir = GetArgValue(args, "--db-dir");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            BuildDatabase(dbDir, scriptsDir);
                            Console.WriteLine("Baza danych została zbudowana pomyślnie.");
                            return 0;
                        }

                    case "export-scripts":
                        {
                            string connStr = GetConnectionString(args);
                            string outputDir = GetArgValue(args, "--output-dir");

                            ExportScripts(connStr, outputDir);
                            Console.WriteLine("Skrypty zostały wyeksportowane pomyślnie.");
                            return 0;
                        }

                    case "update-db":
                        {
                            string connStr = GetConnectionString(args);
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            UpdateDatabase(connStr, scriptsDir);
                            Console.WriteLine("Baza danych została zaktualizowana pomyślnie.");
                            return 0;
                        }

                    default:
                        Console.WriteLine($"Nieznane polecenie: {command}");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Błąd: " + ex.Message);
                return -1;
            }
        }

        private static string GetArgValue(string[] args, string name)
        {
            int idx = Array.IndexOf(args, name);
            if (idx == -1 || idx + 1 >= args.Length)
                throw new ArgumentException($"Brak wymaganego parametru {name}");
            return args[idx + 1];
        }

        private static string GetConnectionString(string[] args)
        {
            int idx = Array.IndexOf(args, "--connection-string");
            if (idx != -1 && idx + 1 < args.Length)
                return args[idx + 1];

            string? fromEnv = Env.GetString("CONNECTION_STRING");
            if (!string.IsNullOrWhiteSpace(fromEnv))
                return fromEnv;

            throw new ArgumentException("Brak parametru --connection-string oraz wartości CONNECTION_STRING w pliku .env");
        }

        /// <summary>
        /// Buduje nową bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void BuildDatabase(string databaseDirectory, string scriptsDirectory)
        {
            string dbPath = databaseDirectory.EndsWith(".fdb", StringComparison.OrdinalIgnoreCase)
                ? databaseDirectory
                : databaseDirectory + ".fdb";

            string? dir = Path.GetDirectoryName(dbPath);
            if (string.IsNullOrEmpty(dir))
                throw new ArgumentException("Ścieżka do bazy musi zawierać katalog.", nameof(databaseDirectory));

            Directory.CreateDirectory(dir);

            var csb = new FbConnectionStringBuilder
            {
                Database = dbPath,
                UserID = Env.GetString("FB_NEW_DB_USER"),
                Password = Env.GetString("FB_NEW_DB_PASSWORD"),
                DataSource = "localhost",
                Port = 3050,
                Dialect = 3,
                ServerType = FbServerType.Default,
                ClientLibrary = "fbclient.dll"
            };


            FbConnection.CreateDatabase(csb.ConnectionString);
            Console.WriteLine("Utworzono pustą bazę danych.");

            using var conn = new FbConnection(csb.ConnectionString);
            conn.Open();

            ExecuteScripts(conn, Path.Combine(scriptsDirectory, "domains"));
            ExecuteScripts(conn, Path.Combine(scriptsDirectory, "tables"));
            ExecuteScripts(conn, Path.Combine(scriptsDirectory, "procedures"));

            Console.WriteLine("Wszystkie skrypty wykonane pomyślnie.");
        }

        /// <summary>
        /// Generuje skrypty metadanych z istniejącej bazy danych Firebird 5.0.
        /// </summary>
        public static void ExportScripts(string connectionString, string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);

            using var conn = new FbConnection(connectionString);
            conn.Open();

            ExportDomains(conn, Path.Combine(outputDirectory, "domains"));
            ExportTables(conn, Path.Combine(outputDirectory, "tables"));
            ExportProcedures(conn, Path.Combine(outputDirectory, "procedures"));
        }

        /// <summary>
        /// Aktualizuje istniejącą bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void UpdateDatabase(string connectionString, string scriptsDirectory)
        {
            using var conn = new FbConnection(connectionString);
            conn.Open();
            Console.WriteLine("Połączono z bazą danych.");

            ExecuteScripts(conn, Path.Combine(scriptsDirectory, "domains"));
            ExecuteScripts(conn, Path.Combine(scriptsDirectory, "tables"));
            ExecuteScripts(conn, Path.Combine(scriptsDirectory, "procedures"));

            Console.WriteLine("Aktualizacja bazy wykonana pomyślnie.");
        }

        private static void ExecuteScripts(FbConnection conn, string dir)
        {
            if (!Directory.Exists(dir))
            {
                Console.WriteLine($"Brak folderu: {dir}");
                return;
            }

            foreach (var file in Directory.GetFiles(dir, "*.sql"))
            {
                string sql = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(sql))
                    continue;

                try
                {
                    using var cmd = new FbCommand(sql, conn);
                    cmd.ExecuteNonQuery();
                    Console.WriteLine($"Wykonano skrypt: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Błąd w pliku {Path.GetFileName(file)}: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Eksport danych. Domeny, Tabele, Procedury.
        /// </summary>
        private static void ExportDomains(FbConnection conn, string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            string sql = @"
                SELECT 
                    RDB$FIELD_NAME,
                    RDB$FIELD_TYPE,
                    RDB$FIELD_LENGTH,
                    RDB$FIELD_SCALE,
                    RDB$FIELD_PRECISION,
                    RDB$CHARACTER_LENGTH,
                    RDB$FIELD_SUB_TYPE
                FROM RDB$FIELDS
                WHERE (RDB$SYSTEM_FLAG = 0 OR RDB$SYSTEM_FLAG IS NULL)";

            using var cmd = new FbCommand(sql, conn);
            using var rd = cmd.ExecuteReader();

            while (rd.Read())
            {
                string name = rd.GetString(0).Trim();

                if (name.StartsWith("RDB$", StringComparison.OrdinalIgnoreCase))
                    continue;

                int fieldType = rd.GetInt32(1);
                int fieldLength = rd.GetInt32(2);
                short fieldScale = rd.GetInt16(3);
                short? fieldPrecision = rd.IsDBNull(4) ? null : rd.GetInt16(4);
                int? charLength = rd.IsDBNull(5) ? null : rd.GetInt32(5);
                short? fieldSubType = rd.IsDBNull(6) ? null : rd.GetInt16(6);

                string typeSql = MapFbType(fieldType, fieldLength, fieldScale, fieldPrecision, charLength, fieldSubType);

                File.WriteAllText(Path.Combine(outputDir, $"{name}.sql"),
                    $"CREATE DOMAIN {name} AS {typeSql};");
            }
        }

        private static void ExportTables(FbConnection conn, string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            string sqlTables = @"
                SELECT RDB$RELATION_NAME
                FROM RDB$RELATIONS
                WHERE RDB$SYSTEM_FLAG = 0 AND RDB$VIEW_BLR IS NULL";

            using var cmd = new FbCommand(sqlTables, conn);
            using var rd = cmd.ExecuteReader();

            while (rd.Read())
            {
                string table = rd.GetString(0).Trim();

                string sqlCols = @"
                    SELECT 
                        rf.RDB$FIELD_NAME,
                        rf.RDB$FIELD_SOURCE,
                        f.RDB$FIELD_TYPE,
                        f.RDB$FIELD_LENGTH,
                        f.RDB$FIELD_SCALE,
                        f.RDB$FIELD_PRECISION,
                        f.RDB$CHARACTER_LENGTH,
                        f.RDB$FIELD_SUB_TYPE,
                        rf.RDB$NULL_FLAG
                    FROM RDB$RELATION_FIELDS rf
                    JOIN RDB$FIELDS f ON rf.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME
                    WHERE rf.RDB$RELATION_NAME = @tbl
                    ORDER BY rf.RDB$FIELD_POSITION";

                using var cmd2 = new FbCommand(sqlCols, conn);
                cmd2.Parameters.AddWithValue("@tbl", table);

                using var rd2 = cmd2.ExecuteReader();

                var cols = new List<string>();

                while (rd2.Read())
                {
                    string colName = rd2.GetString(0).Trim();
                    string fieldSource = rd2.GetString(1).Trim();

                    int fieldType = rd2.GetInt32(2);
                    int fieldLength = rd2.GetInt32(3);
                    short fieldScale = rd2.GetInt16(4);
                    short? fieldPrecision = rd2.IsDBNull(5) ? null : rd2.GetInt16(5);
                    int? charLength = rd2.IsDBNull(6) ? null : rd2.GetInt32(6);
                    short? fieldSubType = rd2.IsDBNull(7) ? null : rd2.GetInt16(7);
                    bool isNotNull = !rd2.IsDBNull(8) && rd2.GetInt16(8) == 1;

                    string columnDef;

                    if (fieldSource.StartsWith("RDB$", StringComparison.OrdinalIgnoreCase))
                    {
                        string sqlType = MapFbType(fieldType, fieldLength, fieldScale, fieldPrecision, charLength, fieldSubType);
                        columnDef = $"    {colName} {sqlType}";
                    }
                    else
                    {
                        columnDef = $"    {colName} {fieldSource}";
                    }

                    if (isNotNull)
                        columnDef += " NOT NULL";

                    cols.Add(columnDef);
                }

                string ddl =
                    $"CREATE TABLE {table} (\n" +
                    string.Join(",\n", cols) +
                    "\n);";

                File.WriteAllText(Path.Combine(outputDir, $"{table}.sql"), ddl);
            }
        }

        private static void ExportProcedures(FbConnection conn, string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            string sqlProc = @"
                SELECT 
                    RDB$PROCEDURE_NAME,
                    RDB$PROCEDURE_SOURCE
                FROM RDB$PROCEDURES
                WHERE RDB$SYSTEM_FLAG = 0";

            using var cmd = new FbCommand(sqlProc, conn);
            using var rd = cmd.ExecuteReader();

            while (rd.Read())
            {
                string name = rd.GetString(0).Trim();
                string source = rd.IsDBNull(1) ? "" : rd.GetString(1);
                string sourceTrim = source?.TrimStart() ?? "";

                string sqlParams = @"
                    SELECT 
                        pp.RDB$PARAMETER_NAME,
                        pp.RDB$PARAMETER_TYPE,
                        pp.RDB$FIELD_SOURCE,
                        f.RDB$FIELD_TYPE,
                        f.RDB$FIELD_LENGTH,
                        f.RDB$FIELD_SCALE,
                        f.RDB$FIELD_PRECISION,
                        f.RDB$CHARACTER_LENGTH,
                        f.RDB$FIELD_SUB_TYPE
                    FROM RDB$PROCEDURE_PARAMETERS pp
                    JOIN RDB$FIELDS f ON pp.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME
                    WHERE pp.RDB$PROCEDURE_NAME = @proc
                    ORDER BY pp.RDB$PARAMETER_TYPE, pp.RDB$PARAMETER_NUMBER";

                using var cmdP = new FbCommand(sqlParams, conn);
                cmdP.Parameters.AddWithValue("@proc", name);

                using var rdP = cmdP.ExecuteReader();

                var inParams = new List<string>();
                var outParams = new List<string>();

                while (rdP.Read())
                {
                    string paramName = rdP.GetString(0).Trim();
                    short paramType = rdP.GetInt16(1);
                    string fieldSource = rdP.GetString(2).Trim();

                    int fieldType = rdP.GetInt32(3);
                    int fieldLength = rdP.GetInt32(4);
                    short fieldScale = rdP.GetInt16(5);
                    short? fieldPrecision = rdP.IsDBNull(6) ? null : rdP.GetInt16(6);
                    int? charLength = rdP.IsDBNull(7) ? null : rdP.GetInt32(7);
                    short? fieldSubType = rdP.IsDBNull(8) ? null : rdP.GetInt16(8);

                    string typeSql =
                        fieldSource.StartsWith("RDB$", StringComparison.OrdinalIgnoreCase)
                        ? MapFbType(fieldType, fieldLength, fieldScale, fieldPrecision, charLength, fieldSubType)
                        : fieldSource;

                    string paramDef = $"    {paramName} {typeSql}";

                    if (paramType == 0)
                        inParams.Add(paramDef);
                    else
                        outParams.Add(paramDef);
                }

                var sb = new StringBuilder();

                sb.Append("CREATE OR ALTER PROCEDURE ").Append(name);

                if (inParams.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("(");
                    sb.AppendLine(string.Join(",\n", inParams));
                    sb.AppendLine(")");
                }

                if (outParams.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("RETURNS (");
                    sb.AppendLine(string.Join(",\n", outParams));
                    sb.AppendLine(")");
                }

                sb.AppendLine();
                sb.AppendLine("AS");

                if (!string.IsNullOrWhiteSpace(sourceTrim))
                {
                    if (sourceTrim.StartsWith("AS", StringComparison.OrdinalIgnoreCase))
                        sourceTrim = sourceTrim.Substring(2).TrimStart();

                    sb.AppendLine(sourceTrim);
                }
                else
                {
                    sb.AppendLine("BEGIN");
                    sb.AppendLine("END");
                }

                File.WriteAllText(Path.Combine(outputDir, $"{name}.sql"), sb.ToString());
            }
        }

        /// <summary>
        /// Mapowanie Firebird do SQL.
        /// </summary>
        private static string MapFbType(
            int fieldType,
            int fieldLength,
            short fieldScale,
            short? fieldPrecision,
            int? charLength,
            short? fieldSubType)
        {
            int effectiveLen = charLength ?? fieldLength;

            switch (fieldType)
            {
                case 14:
                    return $"CHAR({effectiveLen})";
                case 37:
                    return $"VARCHAR({effectiveLen})";
                case 7:
                case 8:
                case 16:
                    if (fieldScale < 0)
                    {
                        int scale = -fieldScale;
                        int precision =
                            fieldPrecision.HasValue && fieldPrecision.Value > 0
                                ? fieldPrecision.Value
                                : fieldType switch
                                {
                                    7 => 4,
                                    8 => 9,
                                    16 => 18,
                                    _ => 18
                                };
                        string kind = fieldSubType == 2 ? "DECIMAL" : "NUMERIC";
                        return $"{kind}({precision},{scale})";
                    }
                    return fieldType switch
                    {
                        7 => "SMALLINT",
                        8 => "INTEGER",
                        16 => "BIGINT",
                        _ => "INTEGER"
                    };
                case 10:
                    return "FLOAT";
                case 27:
                    return "DOUBLE PRECISION";
                default:
                    return "BLOB";
            }
        }
    }
}