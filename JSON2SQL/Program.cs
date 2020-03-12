using System;
using System.Text.Json;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using ShellProgressBar;

namespace JSON2SQL
{
    class Program
    {
        public static ProgressBarOptions ProgressBarOptions = new ProgressBarOptions
        {
            ForegroundColor = ConsoleColor.Green,
            BackgroundColor = ConsoleColor.DarkMagenta,
            ProgressCharacter = '─',
            ProgressBarOnBottom = true
        };
        public static ProgressBarOptions ChildProgressBarOptions = new ProgressBarOptions
        {
            ForegroundColor = ConsoleColor.Yellow,
            BackgroundColor = ConsoleColor.DarkMagenta,
            ProgressCharacter = '─',
            ProgressBarOnBottom = true,
            CollapseWhenFinished = true
        };
        public static ProgressBar Bar;
        public static Dictionary<string, ChildProgressBar> ChildBar = new Dictionary<string, ChildProgressBar>();
        public static List<JObject> DistinctObjects { get; set; }
        public static Dictionary<string, int> DataTypes = new Dictionary<string, int>
        {
            { "System.String", 1 },
            { "System.Single", 2},
            { "System.Int32", 3 },
            { "System.DateTime", 4 },
            { "System.Boolean", 5},
            { "System.Guid", 6}
        };
        public static Dictionary<string, string> ArgumentOptions = new Dictionary<string, string>
        {
            {"Target","" },
            {"Action","" },
            {"SchemaName","" },
            {"Entity", "" },
            {"JsonFile","" },
            {"SqlFile","" }
        };

        static void Main(string[] args)
        {
            //json2sql -mssql/pgsql -create/select -schema <schema_name> -entity <entityname> <filename> <filename_output> 

            if (args.Length == 0 || args.Length < ArgumentOptions.Count)
            {
                Console.WriteLine("Invalid args");
                Environment.Exit(1);
            }
            else
            {
                ArgumentOptions["Target"] = args[0].TrimStart('-');
                ArgumentOptions["Action"] = args[1].TrimStart('-');
                ArgumentOptions["SchemaName"] = args[2];
                ArgumentOptions["Entity"] = args[3];
                ArgumentOptions["JsonFile"] = args[4];
                ArgumentOptions["SqlFile"] = args[5];
            }

            Console.WriteLine("JSON2SQL");
            Console.WriteLine("Start: " + DateTime.Now + Environment.NewLine);

            DistinctObjects = new List<JObject>();

            try
            {
                string utf8json = File.ReadAllText(ArgumentOptions["JsonFile"], Encoding.UTF8);
                

                using (JsonDocument json = JsonDocument.Parse(utf8json))
                {
                    JsonElement root = json.RootElement;

                    ParseObject(root, null, null, ArgumentOptions["SchemaName"]);

                    File.WriteAllText(ArgumentOptions["SqlFile"], string.Empty);
                    foreach (JObject jo in DistinctObjects)
                    {
                        using (StreamWriter w = File.AppendText(ArgumentOptions["SqlFile"]))
                        {
                            w.WriteLine(jo.Generate(ArgumentOptions["Action"], ArgumentOptions["Target"], ArgumentOptions["Entity"]));
                        }
                    }
                }
            }
            catch (JsonException jex)
            {

                Console.WriteLine("Parsing Error: " + jex.Message);
            }
            catch (FileNotFoundException fex)
            {

                Console.WriteLine("Json file not found: " + fex.Message);
            }
        }

        public static void ParseObject(JsonElement element, string name, string parentName, string schemaName)
        {
            if (string.IsNullOrEmpty(name))
                name = schemaName;

            if (element.ValueKind == JsonValueKind.Array)
            {
                if (Bar == null)
                    Bar = new ProgressBar(element.GetArrayLength(), name + " - " + element.GetArrayLength(), ProgressBarOptions);
                else
                {
                    if (!ChildBar.ContainsKey(name))
                        ChildBar.Add(name, Bar.Spawn(element.GetArrayLength(), name + " - " + element.GetArrayLength(), ChildProgressBarOptions));
                    else
                    {
                        ChildBar[name].Dispose();
                        ChildBar.Remove(name);
                        ChildBar.Add(name, Bar.Spawn(element.GetArrayLength(), name + " - " + element.GetArrayLength(), ChildProgressBarOptions));
                    }

                }

                JObject jo = DistinctObjects.Find(x => x.Name == name);

                if (jo == null)
                {
                    jo = new JObject();
                    if (DistinctObjects.Count == 0)
                        jo.isRoot = true;
                    jo.Name = name;
                    jo.SchemaName = schemaName;
                    DistinctObjects.Add(jo);
                }

                foreach (JsonElement je in element.EnumerateArray())
                {
                    if (name == schemaName)
                        Bar.Tick();
                    else
                    {
                        ChildBar[name].Tick();
                    }

                    ParseObject(je, jo.Name, jo.ParentName, schemaName);
                }
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                JObject jo = DistinctObjects.Find(x => x.Name == name);

                foreach (JsonProperty jp in element.EnumerateObject())
                {
                    string propertyId = parentName + jp.Name;
                    string propertyType = GuessDataType(jp.Value);

                    if (jp.Value.ValueKind == JsonValueKind.Array)
                        ParseObject(element.GetProperty(jp.Name), propertyId, name, schemaName);
                    else if (jp.Value.ValueKind == JsonValueKind.Object)
                    {
                        ParseObject(element.GetProperty(jp.Name), name, propertyId, schemaName);
                    }
                    else
                    {
                        if (!jo.Properties.ContainsKey(propertyId))
                            jo.Properties.Add(propertyId, new JProperty(jp.Name, propertyType, parentName, jp.Value.ToString().Length));
                        else if (SwapType(jo.Properties[propertyId].DataType, propertyType))
                        {
                            jo.Properties[propertyId].DataType = propertyType;
                            jo.Properties[propertyId].Lenght = jp.Value.ToString().Length;
                        }
                        else if (propertyType == "System.String" && jo.Properties[propertyId].Lenght < jp.Value.ToString().Length)
                            jo.Properties[propertyId].Lenght = jp.Value.ToString().Length;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                JObject jo = DistinctObjects.Find(x => x.Name == name);

                if (!jo.Properties.ContainsKey(name))
                    jo.Properties.Add(name, new JProperty(name, "System.String", parentName, element.ToString().Length));

            }
            else if (element.ValueKind == JsonValueKind.Number)
            {
                JObject jo = DistinctObjects.Find(x => x.Name == name);

                if (!jo.Properties.ContainsKey(name))
                    jo.Properties.Add(name, new JProperty(name, "System.Integer", parentName, 0));

            }
        }

        public static bool SwapType(string oldType, string newType)
        {
            return DataTypes[oldType] > DataTypes[newType];
        }

        public static string GuessDataType(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                return typeof(bool).ToString();
            else if (value.ValueKind == JsonValueKind.Array || value.ValueKind == JsonValueKind.Object)
                return null;
            else if (value.ValueKind == JsonValueKind.Number && value.ToString().Length > 8)
                return typeof(string).ToString();
            else if (value.ToString().Length < 10)
            {
                if (int.TryParse(value.ToString(), out var i))
                    return "System.Int32";
                else if (float.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var f))
                    return "System.Single";
                else
                    return "System.String";                
            }
            else
            {

                if (DateTime.TryParse(value.ToString(), out var d))
                    return "System.DateTime";
                else if (Guid.TryParse(value.ToString(), out var g))
                    return "System.Guid";
                else
                    return "System.String";                
            }            
        }
    }
}


