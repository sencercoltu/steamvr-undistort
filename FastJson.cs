using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Collections;
using JsonObject = System.Collections.Generic.Dictionary<string, object>;
using IJsonObject = System.Collections.Generic.IDictionary<string, object>;
using System.Linq;

namespace FastJsonLib
{
    public static class FastJson
    {
        [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
        public sealed class ScriptIgnoreAttribute : Attribute
        {
            public ScriptIgnoreAttribute()
            {
            }
        }

        public enum DateFormat
        {
            JSON,
            JavaScript
        }

        public class Options
        {
            public DateFormat DateFormat = DateFormat.JSON;
            public bool DiscardNull = false;
            public bool DiscardEmptyArray = false;
            public bool IncludeProperties = true;
            public bool IncludeFields = false;
        }

        public static readonly Options DefaultOptions = new Options { DateFormat = DateFormat.JSON, DiscardNull = false, DiscardEmptyArray = false, IncludeProperties = true, IncludeFields = false };

        public static readonly long DatetimeMinTimeTicks = (new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks;

        private static bool IsDelimiter(string json, ref int pos, out char delim)
        {
            delim = '\0';
            if (pos >= json.Length) return false;
            var ch = json[pos];
            switch (ch)
            {
                case ']':
                case '}':
                    delim = ch;
                    return true;
                case ',':
                case '\r':
                case '\n':
                    delim = ch;
                    return true;
            }
            return false;
        }

        public static string JavaScriptDecode(string text)
        {
            if (text == null) return "";
            var sb = new StringBuilder();
            var len = text.Length;
            var escaped = false;
            for (var i = 0; i < len; i++)
            {
                var ch = text[i];
                if (escaped)
                {
                    switch (ch)
                    {
                        case '\'':
                            sb.Append('\'');
                            break;
                        case '"':
                            sb.Append('"');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        case 'f':
                            sb.Append('\f');
                            break;
                        case 'b':
                            sb.Append('\b');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case 'u':
                            {
                                i++;
                                var uc = text.Substring(i, 4);
                                i += 3; //döngüde artacak ondan 4 değil
                                switch (uc)
                                {
                                    case "0085":
                                        sb.Append('\u0085');
                                        break;
                                    case "2028":
                                        sb.Append('\u2028');
                                        break;
                                    case "2029":
                                        sb.Append('\u2029');
                                        break;
                                    case "005c":
                                        sb.Append('\\');
                                        break;
                                    case "0022":
                                        sb.Append('\"');
                                        break;
                                    case "0027":
                                        sb.Append('\'');
                                        break;
                                    case "0026":
                                        sb.Append('&');
                                        break;
                                    case "003c":
                                        sb.Append('<');
                                        break;
                                    case "003e":
                                        sb.Append('>');
                                        break;
                                }
                            }
                            break;
                    }
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else
                    sb.Append(ch);
            }

            return sb.ToString();
        }

        public static string JavaScriptEncode(string text)
        {
            if (text == null) return "";
            var len = text.Length;
            var sb = new StringBuilder();
            for (var i = 0; i < len; i++)
            {
                var ch = text[i];
                switch (ch)
                {
                    case '\u0085':
                        sb.Append(@"\u0085");
                        break;
                    case '\u2028':
                        sb.Append(@"\u2028");
                        break;
                    case '\u2029':
                        sb.Append(@"\u2029");
                        break;
                    case '\\':
                        sb.Append(@"\u005c");
                        break;
                    case '\"':
                        sb.Append(@"\u0022");
                        break;
                    case '\'':
                        sb.Append(@"\u0027");
                        break;
                    case '&':
                        sb.Append(@"\u0026");
                        break;
                    case '<':
                        sb.Append(@"\u003c");
                        break;
                    case '>':
                        sb.Append(@"\u003e");
                        break;
                    case '\r':
                        sb.Append(@"\r");
                        break;
                    case '\n':
                        sb.Append(@"\n");
                        break;
                    case '\t':
                        sb.Append(@"\t");
                        break;
                    case '\b':
                        sb.Append(@"\b");
                        break;
                    case '\f':
                        sb.Append(@"\f");
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }

            return sb.ToString();

        }

        private static bool SkipWhiteSpace(string json, ref int pos)
        {
            while (true)
            {
                if (pos >= json.Length) return false;
                switch (json[pos])
                {
                    case ',':
                    case ' ':
                    case '\r':
                    case '\n':
                    case '\t':
                        pos++;
                        break;
                    default:
                        return true;
                }
            }
        }

        private static bool IsGenericDictionary(Type type)
        {
            return type != null &&
                type.IsGenericType &&
                (typeof(IDictionary).IsAssignableFrom(type) || type.GetGenericTypeDefinition() == typeof(IDictionary<,>)) &&
                type.GetGenericArguments().Length == 2;
        }

        public static T Deserialize<T>(string json)
        {
            var pos = 0;
            SkipWhiteSpace(json, ref pos);
            var type = typeof(T);
            //if (json[pos] != '{') return new JsonObject();
            //root başlangıcını bulduk
            var dVal = DeserializeUnknown(ref json, ref pos);
            if (type == typeof(IJsonObject) || type == typeof(object[])) return (T)dVal;
            return (T)DeserializeObject(null, type, dVal, true);
        }


        public static object DeserializeRaw(string json)
        {
            var pos = 0;
            SkipWhiteSpace(json, ref pos);
            return DeserializeUnknown(ref json, ref pos);
        }

        private static object DeserializeObject(PropertyInfo prop, Type type, object dval, bool alreadyDecoded = false)
        {
            if (type == null)
                return null;

            if (dval == null)
                return null;

            if (type.IsEnum)
                return Enum.ToObject(type, Convert.ToInt32(dval));

            if (type == typeof(string))
                return Convert.ChangeType((alreadyDecoded) ? dval.ToString() : JavaScriptDecode(dval.ToString()), type);

            if (type.IsPrimitive || type == typeof(DateTime))
                return Convert.ChangeType(dval, type);

            if (prop == null)
            {

                var arr = dval as Array;
                if (type.IsArray && arr != null)
                {
                    var subType = type.GetElementType();
                    var obj = Array.CreateInstance(subType, arr.Length) as Array;
                    for (var i = 0; i < arr.Length; i++)
                    {
                        obj.SetValue(DeserializeObject(null, subType, arr.GetValue(i), alreadyDecoded), i);
                    }
                    return obj;
                }

                //null isek kafadan obje ya da array olabilir sadece
                if (dval is IJsonObject dict)
                {
                    object obj = null;
                    if (IsGenericDictionary(type))
                    {
                        Type keyType = type.GetGenericArguments()[0];
                        if (keyType != typeof(string) && keyType != typeof(object))
                            return null;
                        Type valueType = type.GetGenericArguments()[1];
                        IDictionary o = null;
                        try
                        {
                            // Get the strongly typed Dictionary<k, v> 
                            Type gt = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                            o = (IDictionary)Activator.CreateInstance(gt);
                        }
                        catch
                        {
                            o = (IDictionary)Activator.CreateInstance(type);
                        }

                        foreach (var item in dict)
                            o[item.Key] = item.Value;
                        obj = o;
                    }
                    else
                    {
                        obj = Activator.CreateInstance(type);
                        var props = type.GetProperties();
                        for (int pi = 0; pi < props.Length; pi++)
                        {
                            var p = props[pi];
                            var attribs = p.GetCustomAttributes(typeof(ScriptIgnoreAttribute), false);
                            if (attribs.Length > 0)
                                continue;
                            if (!dict.ContainsKey(p.Name))
                                continue;
                            var dictVal = dict[p.Name];
                            p.SetValue(obj, DeserializeObject(p, p.PropertyType, dictVal, alreadyDecoded));
                        }
                    }
                    return obj;
                }

                if (type is object)
                    return dval;

                return null;
            }
            else if (IsGenericDictionary(prop.PropertyType))
            {
                IDictionary o = null;
                if (dval is IJsonObject dict)
                {
                    Type keyType = prop.PropertyType.GetGenericArguments()[0];
                    if (keyType != typeof(string) && keyType != typeof(object))
                        return null;
                    Type valueType = prop.PropertyType.GetGenericArguments()[1];
                    try
                    {
                        // Get the strongly typed Dictionary<k, v> 
                        Type gt = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                        o = (IDictionary)Activator.CreateInstance(gt);
                    }
                    catch
                    {
                        o = (IDictionary)Activator.CreateInstance(prop.PropertyType);
                    }
                    foreach (var item in dict)
                        o[item.Key] = item.Value;
                }
                else
                    return null;
                return o;
            }
            else
            {
                return DeserializeObject(null, prop.PropertyType, dval, alreadyDecoded);
            }
        }

        private static object DeserializeUnknown(ref string json, ref int pos)
        {
            var ch = json[pos];
            switch (ch)
            {
                case '\0':
                    return null;
                case '{': // { ise object
                    {
                        pos++;
                        SkipWhiteSpace(json, ref pos);
                        IJsonObject result = new JsonObject();
                        ch = json[pos];
                        while (ch != '}')
                        {
                            var key = GetKey(json, ref pos);
                            if (string.IsNullOrEmpty(key)) return new JsonObject();
                            //if (key == "S!'^+&/}\\]\"")
                            //{
                            //    key = key;
                            //}
                            result[key] = DeserializeUnknown(ref json, ref pos);
                            //if (json[pos] == ',') pos++;
                            if (!SkipWhiteSpace(json, ref pos)) break;
                            ch = json[pos];
                        }
                        pos++;
                        //if (json[pos] == ',') pos++;
                        SkipWhiteSpace(json, ref pos);
                        return result;
                    }
                case '[': // [ ise array
                    {
                        pos++;
                        SkipWhiteSpace(json, ref pos);
                        var result = new List<object>();
                        ch = json[pos];
                        while (ch != ']')
                        {
                            result.Add(DeserializeUnknown(ref json, ref pos));
                            //if (json[pos] == ',') pos++;
                            if (!SkipWhiteSpace(json, ref pos)) break;
                            ch = json[pos];
                        }
                        pos++;
                        //if (json[pos] == ',') pos++;
                        SkipWhiteSpace(json, ref pos);
                        return result.ToArray();
                    }
                case '\'':
                case '"': // " ise string
                    {
                        var quoteChar = ch;
                        pos++;
                        var start = pos;
                        var end = pos;
                        var escaped = false;
                        while (true)
                        {
                            ch = json[pos];
                            if (!escaped && ch == quoteChar)
                            {
                                end = pos;
                                break;
                            }
                            else if (!escaped && ch == '\\')
                                escaped = true;
                            else if (escaped)
                                escaped = false;
                            pos++;
                            if (pos >= json.Length) return null;
                        }
                        pos++;
                        SkipWhiteSpace(json, ref pos);
                        if (start == end) return "";
                        var strValue = json.Substring(start, end - start);

                        if (strValue.StartsWith("new Date("))
                        {
                            start = 9;
                            end = strValue.IndexOf(')', start + 1);
                            strValue = strValue.Substring(start, end - start);
                            var match = Regex.Match(strValue, @"^(?<ticks>-?[0-9]+)(?:[a-zA-Z]|(?:\+|-)[0-9]{4})?");
                            var ticksStr = match.Groups["ticks"].Value;

                            if (!long.TryParse(ticksStr, out long ticks)) return DateTime.MinValue;
                            // The javascript ticks start from 1/1/1970 but FX DateTime ticks start from 1/1/0001
                            var dt = new DateTime(ticks, DateTimeKind.Local);
                            if (dt.Year < 100)
                                dt = new DateTime(ticks * 10000 + DatetimeMinTimeTicks, DateTimeKind.Utc);
                            return dt;
                        }
                        else if (strValue.StartsWith("\\/Date("))
                        {
                            start = 7;
                            end = strValue.IndexOf(')', start + 1);
                            strValue = strValue.Substring(start, end - start);
                            var match = Regex.Match(strValue, @"^(?<ticks>-?[0-9]+)(?:[a-zA-Z]|(?:\+|-)[0-9]{4})?");
                            var ticksStr = match.Groups["ticks"].Value;

                            if (!long.TryParse(ticksStr, out long ticks)) return DateTime.MinValue;
                            // The javascript ticks start from 1/1/1970 but FX DateTime ticks start from 1/1/0001
                            var dt = new DateTime(ticks, DateTimeKind.Local);
                            if (dt.Year < 100)
                                dt = new DateTime(ticks * 10000 + DatetimeMinTimeTicks, DateTimeKind.Utc);
                            return dt;
                        }
                        return JavaScriptDecode(strValue);
                    }
                default: // yoksa primitive                    
                    {
                        char delim;
                        SkipWhiteSpace(json, ref pos);
                        var start = pos;
                        var end = pos;
                        while (true)
                        {
                            if (IsDelimiter(json, ref pos, out delim))
                            {
                                end = ++pos;
                                break;
                            }
                            pos++;
                            if (pos >= json.Length) return null;
                        }

                        if (delim == ']' || delim == '}') pos--;
                        SkipWhiteSpace(json, ref pos);
                        var strValue = json.Substring(start, end - start - 1).ToLower().Trim();
                        switch (strValue)
                        {
                            case "null": return null;
                            case "true": return true;
                            case "false": return false;
                            default:
                                {
                                    if (string.IsNullOrEmpty(strValue))
                                    {
                                        if (delim == ']')
                                        {
                                            //pos++;
                                            return null as object[];
                                        }
                                        else if (delim == '}')
                                        {
                                            pos++;
                                            return null;
                                        }
                                    }
                                    try
                                    {
                                        //if (strValue.StartsWith("-") || strValue.IndexOf('.') > -1 || strValue.IndexOf("e-") > -1)
                                        //{

                                        //	if (decimal.TryParse(strValue, NumberStyles.Any, CultureInfo.InvariantCulture, out dec))
                                        //		return dec;
                                        //}

                                        if (decimal.TryParse(strValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal dec))
                                            return dec;
                                        if (long.TryParse(strValue, out long lng))
                                            return lng;
                                        if (double.TryParse(strValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double dbl))
                                            return dbl;
                                    }
                                    catch
                                    {

                                    }
                                    //if (strValue.StartsWith("-") || strValue.IndexOf('.') > -1 || strValue.IndexOf("e-") > -1)
                                    //    return decimal.Parse(strValue, NumberStyles.Any, CultureInfo.InvariantCulture);
                                    //return ulong.Parse(strValue, CultureInfo.InvariantCulture);                                    
                                    return double.Parse(strValue, NumberStyles.Any, CultureInfo.InvariantCulture);

                                }
                        }

                        // true false ise bool
                    }
            }
        }

        private static string GetKey(string json, ref int pos)
        {
            //Common.escape ile escape edilecek keyler \ ve " sadece
            SkipWhiteSpace(json, ref pos);
            char quoteChar = '\0';
            if (json[pos] == '"') quoteChar = '"';
            if (json[pos] == '\'') quoteChar = '\'';
            if (quoteChar != '\0') pos++;
            var keyStart = pos;
            bool escaped = false;
            while (true)
            {
                var ch = json[pos];
                if (!escaped && ch == '\\')
                    escaped = true;
                else if (!escaped && ch == quoteChar)
                    break;
                else if (ch == ':' && quoteChar == '\0')
                    break;
                else
                    escaped = false;
                pos++;
            }
            pos++;
            var keyEnd = pos;
            if (keyEnd <= keyStart) return null;
            var key = json.Substring(keyStart, keyEnd - keyStart - 1).Trim();
            if (string.IsNullOrEmpty(key)) return null;
            SkipWhiteSpace(json, ref pos);
            if (quoteChar == '\0') return Unescape(key);
            if (json[pos] != ':') return null;
            pos++;
            SkipWhiteSpace(json, ref pos);
            return Unescape(key);
        }

        public static string Serialize(object obj, Options options = null, StringBuilder sb = null)
        {
            if (obj == null) return "{}";
            if (options == null) options = DefaultOptions;
            if (sb == null) sb = new StringBuilder();
            SerializeUnknown(obj, options, sb);
            return sb.ToString();
        }

        //private static string Serialize(IDictionary<string, object> jsonObject, Options options = null, StringBuilder sb = null)
        //{
        //    SerializeDictionary(jsonObject, options, sb);
        //    return sb.ToString();
        //}

        private static void SerializeDictionary(IDictionary<string, object> jsonObject, Options options, StringBuilder sb)
        {
            sb.Append("{");
            //var ni = 0;
            var i = 0;
            foreach (var kvp in jsonObject)
            //for (var i = 0; i < jsonObject.Count; i++)
            {
                //var kvp = jsonObject.ElementAt(i);
                if (string.IsNullOrEmpty(kvp.Key)) continue;
                if (kvp.Value == null && options.DiscardNull) continue;
                if ((kvp.Value is object[]) && (kvp.Value as object[]).Length == 0 && options.DiscardEmptyArray) continue;
                if (i > 0) sb.Append(",");
                sb.Append('"');
                sb.Append(Escape(kvp.Key));
                sb.Append("\":");
                if (kvp.Value == null)
                {
                    if (!options.DiscardNull) sb.Append("null");
                }
                else SerializeUnknown(kvp.Value, options, sb);
                i++;

            }
            sb.Append("}");
        }

        private static void SerializeObject(Type t, object obj, Options options, StringBuilder sb)
        {
            sb.Append("{");
            if (options.IncludeFields)
            {
                var fields = t.GetFields((BindingFlags.Public | BindingFlags.Instance) & ~BindingFlags.Static);
                int i = 0;
                for (var pi = 0; pi < fields.Length; pi++)
                //foreach (var prop in fields)
                {
                    var prop = fields[pi];
                    var attribs = prop.GetCustomAttributes(typeof(ScriptIgnoreAttribute), false);
                    if (attribs.Length > 0) continue;
                    var value = prop.GetValue(obj);
                    if (value == null && options.DiscardNull) continue;
                    if (i > 0) sb.Append(",");
                    sb.Append('"');
                    sb.Append(Escape(prop.Name));
                    sb.Append("\":");
                    if (value == null)
                        sb.Append("null");
                    else if (DBNull.Value.Equals(value))
                        sb.Append("null");
                    else
                        SerializeUnknown(value, options, sb);
                    i++;
                }
            }
            if (options.IncludeProperties)
            {
                var props = t.GetProperties((BindingFlags.Public | BindingFlags.Instance) & ~BindingFlags.Static);
                int i = 0;
                for (int pi = 0; pi < props.Length; pi++)
                //foreach (var prop in props)
                {
                    var prop = props[pi];
                    var attribs = prop.GetCustomAttributes(typeof(ScriptIgnoreAttribute), false);
                    if (attribs.Length > 0) continue;
                    var value = prop.GetValue(obj);
                    if (value == null && options.DiscardNull) continue;
                    if (i > 0) sb.Append(",");
                    sb.Append('"');
                    sb.Append(Escape(prop.Name));
                    sb.Append("\":");
                    if (value == null)
                        sb.Append("null");
                    else if (DBNull.Value.Equals(value))
                        sb.Append("null");
                    else
                        SerializeUnknown(value, options, sb);
                    i++;
                }
            }
            sb.Append("}");
        }

        private static void SerializeUnknown(object value, Options options, StringBuilder sb)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }
            if (value is IDictionary<string, object> dict)
                SerializeDictionary(dict, options, sb);
            else
            {
                var t = value.GetType();
                if (t.IsEnum) sb.Append(((int)value).ToString(CultureInfo.InvariantCulture));
                else if (t == typeof(decimal)) sb.Append(((decimal)value).ToString(CultureInfo.InvariantCulture));
                else if (t == typeof(string))
                {
                    sb.Append('"');
                    sb.Append(JavaScriptEncode((string)value));
                    sb.Append('"');
                }
                else if (t == typeof(char))
                {
                    char c = (char)value;
                    if (c == '\0')
                        sb.Append("null");
                    else
                        sb.Append("\"" + c + "\"");  //'2' karakteri 2 değil 50 yazılmalı, ondan char için özel case
                }
                else if (t == typeof(DateTime))
                {
                    switch (options.DateFormat)
                    {
                        case DateFormat.JavaScript:
                            sb.Append("new Date(");
                            sb.Append((((DateTime)value).ToUniversalTime().Ticks - DatetimeMinTimeTicks) / 10000);
                            sb.Append(")");
                            break;
                        case DateFormat.JSON:
                            sb.Append("\"\\/Date(");
                            sb.Append((((DateTime)value).Ticks));
                            sb.Append(")\\/\"");
                            break;
                    }
                }
                else if (t == typeof(bool))
                    sb.Append(((bool)value) ? "true" : "false");
                else if (t.IsPrimitive)
                    sb.Append(value.NumberToString(true));
                else if (value is System.DBNull)
                {
                    sb.Append("null");
                }
                else
                {
                    if (value is IEnumerable ie)
                    {
                        //var arr = value as IEnumerable;
                        sb.Append("[");
                        //var ni = 0;
                        var i = 0;
                        foreach (var item in ie)
                        //for (var k = 0; k < arr.Length; k++)
                        {
                            if (item == null && options.DiscardNull) continue;
                            if (i > 0) sb.Append(",");
                            SerializeUnknown(item, options, sb);
                            i++;
                        }
                        sb.Append("]");
                    }
                    else
                        SerializeObject(t, value, options, sb);
                }
            }
        }

        public static string NumberToString(this object value, bool forceDigits = false, CultureInfo culture = null)
        {
            if (culture == null) culture = CultureInfo.InvariantCulture;

            if (forceDigits)
            {
                if (value is float) return ((decimal)((float)value)).ToString(culture);
                if (value is decimal) return ((decimal)value).ToString(culture);
                if (value is double d)
                {
                    if (d > (double)decimal.MaxValue || double.IsPositiveInfinity(d))
                        return decimal.MaxValue.ToString(culture);
                    if (d < (double)decimal.MinValue || double.IsNegativeInfinity(d))
                        return decimal.MaxValue.ToString(culture);
                    if (double.IsNaN(d))
                        return "0";
                    return ((decimal)d).ToString(culture);
                }
            }
            else
            {
                if (value is float) return ((float)value).ToString(culture);
                if (value is double) return ((double)value).ToString(culture);
                if (value is decimal) return ((decimal)value).ToString(culture);
            }
            if (value is byte) return ((byte)value).ToString(culture);
            if (value is sbyte) return ((sbyte)value).ToString(culture);
            if (value is short) return ((short)value).ToString(culture);
            if (value is ushort) return ((ushort)value).ToString(culture);
            if (value is int) return ((int)value).ToString(culture);
            if (value is uint) return ((uint)value).ToString(culture);
            if (value is long) return ((long)value).ToString(culture);
            if (value is ulong) return ((ulong)value).ToString(culture);
            if (value is string)
            {
                if (DateTime.TryParse(value.ToString(), culture, DateTimeStyles.AssumeLocal, out DateTime dt))
                    return value.ToString().Replace(".", culture.NumberFormat.CurrencyDecimalSeparator);
                else
                    return value == null ? "" : value.ToString();
            }
            return value == null ? "null" : value.ToString();
        }

        public static string Escape(this string s)
        {
            return s?.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public static string Unescape(this string s)
        {
            return s?.Replace("\\\\", "\\").Replace("\\\"", "\"");
        }

        private const string IndentString = "  ";

        public static string Prettify(this string str)
        {
            var indent = 0;
            var quoteChar = '\0';
            var sb = new StringBuilder();
            for (var i = 0; i < str.Length; i++)
            {
                var ch = str[i];
                switch (ch)
                {
                    case '{':
                    case '[':
                        sb.Append(ch);
                        if (quoteChar == '\0')
                        {
                            sb.AppendLine();
                            Enumerable.Range(0, ++indent).ForEach(item => sb.Append(IndentString));
                        }
                        break;
                    case '}':
                    case ']':
                        if (quoteChar == '\0')
                        {
                            sb.AppendLine();
                            Enumerable.Range(0, --indent).ForEach(item => sb.Append(IndentString));
                        }
                        sb.Append(ch);
                        break;
                    case '"':
                    case '\'':
                        sb.Append(ch);
                        if (quoteChar == ch)
                        {
                            bool escaped = false;
                            var index = i;
                            while (index > 0 && str[--index] == '\\')
                                escaped = !escaped;
                            if (!escaped)
                                quoteChar = '\0';
                        }
                        else if (quoteChar == '\0')
                            quoteChar = ch;
                        break;
                    case ',':
                        sb.Append(ch);
                        if (quoteChar == '\0')
                        {
                            sb.AppendLine();
                            Enumerable.Range(0, indent).ForEach(item => sb.Append(IndentString));
                        }
                        break;
                    case ':':
                        sb.Append(ch);
                        if (quoteChar == '\0')
                            sb.Append(" ");
                        break;
                    default:
                        if (quoteChar != '\0' || !"\t\r\n ".Contains(ch))
                        {
                            if (ch == '\n')
                            {
                                sb.AppendLine();
                                Enumerable.Range(0, indent + 1).ForEach(item => sb.Append(IndentString));
                            }
                            else
                                sb.Append(ch);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        private static void ForEach<T>(this IEnumerable<T> ie, Action<T> action)
        {
            foreach (var i in ie)
            {
                action(i);
            }
        }
    }
}
