using System;
using System.Collections.Generic;
using System.Text;

namespace JSON2SQL
{
    class JProperty
    {
        private decimal _Lenght;
        public string PropertyName { get; set; }
        public string ParentProperty { get; set; }
        public string DataType { get; set; }
        public decimal Lenght
        {
            get => _Lenght;
            set { _Lenght = Math.Round(value / 5) * 5; }
        }
        public JProperty(string propertyName, string datatype, string parentProperty, int lenght)
        {
            PropertyName = propertyName;
            ParentProperty = parentProperty;
            DataType = datatype;
            Lenght = lenght;
        }

    }

    class JObject
    {
        public bool isRoot { get; set; }
        public string SchemaName { get; set; }
        public string ParentName { get; set; }
        public string Name { get; set; }
        public Dictionary<string, JProperty> Properties { get; set; }

        public JObject()
        {
            Properties = new Dictionary<string, JProperty>();
        }


        public string Generate(string action, string target, string entity)
        {
            switch (action)
            {
                case "create":
                    return GenerateTables(target);
                case "select":
                    return GenerateJSONQuery(entity);
                default:
                    return "";
            }
        }

        public string GenerateTables(string target)
        {
            StringBuilder stringBuilder = new StringBuilder(1000);
            StringBuilder sqlStatement = stringBuilder;

            string schema = "";
            if (!isRoot)
                schema = SchemaName;

            string camelName = CamelCaseFields(Name);

            sqlStatement.AppendFormat("-- {0} CREATE TABLE {1} --", target, camelName)
                .Append(Environment.NewLine)
                .AppendFormat("CREATE TABLE {0}{1} (", schema, camelName)
                .Append(Environment.NewLine);

            foreach (KeyValuePair<string, JProperty> prop in Properties)
            {
                if (prop.Value == null)
                {
                    Properties.Remove(prop.Key);
                    continue;
                }
                string parent = "";
                if (!string.IsNullOrEmpty(prop.Value.ParentProperty))
                    parent = CamelCaseFields(prop.Value.ParentProperty) + "_";

                sqlStatement.Append("\"")
                    .Append(parent)
                    .Append(CamelCaseFields(prop.Value.PropertyName))
                    .Append("\" ")
                    .Append(TranslateDataType(prop.Value, target))
                    .Append(",").Append(Environment.NewLine);
            }
            sqlStatement.Remove(sqlStatement.Length - 1, 1);
            sqlStatement.Append(");");
            return sqlStatement.ToString();
        }

        public string GenerateJSONQuery(string entity)
        {

            StringBuilder sqlStatement = new StringBuilder(1000);
            sqlStatement.AppendFormat("-- POSTGRESQL SELECT TABLE {0} --", CamelCaseFields(Name)).Append(Environment.NewLine).Append("SELECT ").Append(Environment.NewLine);

            if (isRoot)
            {
                foreach (KeyValuePair<string, JProperty> prop in Properties)
                {
                    ClearNullProperties(prop);

                    string parentProperty = "";

                    if (!string.IsNullOrEmpty(prop.Value.ParentProperty))
                        parentProperty = parentProperty + "->'" + prop.Value.ParentProperty + "'";


                    sqlStatement.Append("(convert_from (decode ( (message::json->>'payload')::json->>'raw', 'base64'), 'UTF8'))::json")
                        .Append(parentProperty)
                        .Append("->>")
                        .Append("\'")
                        .Append(prop.Value.PropertyName)
                        .Append("\'")
                        .Append(" ")
                        .Append("\"")
                        .Append(CamelCaseFields(prop.Key))
                        .Append("\"")
                        .Append(Environment.NewLine)
                        .Append(",");
                }

                sqlStatement.Remove(sqlStatement.Length - 1, 1);
                sqlStatement.Append("from wosa_odi_bridge_prd where entity = '" + entity + "'");
            }
            else
            {
                foreach (KeyValuePair<string, JProperty> prop in Properties)
                {
                    ClearNullProperties(prop);

                    string parentProperty = "";

                    if (!string.IsNullOrEmpty(prop.Value.ParentProperty))
                        parentProperty = parentProperty + "->'" + prop.Value.ParentProperty + "'";


                    sqlStatement.Append(Name)
                        .Append(parentProperty)
                        .Append("->>")
                        .Append("\'")
                        .Append(prop.Value.PropertyName)
                        .Append("\'")
                        .Append(" ")
                        .Append("\"")
                        .Append(CamelCaseFields(prop.Key))
                        .Append("\"")
                        .Append(Environment.NewLine)
                        .Append(",");
                }

                sqlStatement.Remove(sqlStatement.Length - 1, 1);
                sqlStatement.Append("from wosa_odi_bridge_prd,")
                    .AppendFormat("json_array_elements((convert_from (decode ( (message::json->>'payload')::json->>'raw', 'base64'), 'UTF8'))::json->'{0}') {0}", Name)
                    .Append("where entity = '" + entity + "'");
            }

            return sqlStatement.ToString();
        }

        public void ClearNullProperties(KeyValuePair<string, JProperty> prop)
        {
            if (prop.Value == null)
            {
                Properties.Remove(prop.Key);
            }
        }

        public string TranslateDataType(JProperty prop, string target)
        {
            if (target == "mssql")
            {
                switch (prop.DataType)
                {
                    case "System.String":
                        return "nvarchar(" + prop.Lenght + ")";
                    case "System.Int32":
                        return "integer";
                    case "System.Guid":
                        return "uniqueidentifier";
                    case "System.DateTime":
                        return "datetime";
                    case "System.Single":
                        return "numeric(10,2)";
                    case "System.Boolean":
                        return "bit";
                    default:
                        return null;
                }

            }
            else if (target == "pgsql")
            {
                switch (prop.DataType)
                {
                    case "System.String":
                        return "character varying(" + prop.Lenght + ")";
                    case "System.Int32":
                        return "integer";
                    case "System.Guid":
                        return "uuid";
                    case "System.DateTime":
                        return "timestamp without time zone";
                    case "System.Single":
                        return "numeric(10,2)";
                    case "System.Boolean":
                        return "boolean";
                    default:
                        return null;
                }
            }
            else
                return null;
        }

        public string CamelCaseFields(string field)
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(field))
            {
                var splited = field.Split("_");
                foreach (string s in splited)
                {
                    sb.Append(s.Substring(0, 1).ToUpper());
                    sb.Append(s.Substring(1, s.Length - 1));
                }
            }
            return sb.ToString();
        }
    }
}
