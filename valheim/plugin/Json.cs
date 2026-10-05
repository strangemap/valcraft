using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValCraft
{
	/// <summary>A small JSON reader for the link's messages: objects become dictionaries, arrays lists, numbers doubles.</summary>
	internal static class Json
	{
		public static object Parse(string s)
		{
			int i = 0;
			return Value(s, ref i);
		}

		private static void Ws(string s, ref int i)
		{
			while (i < s.Length && char.IsWhiteSpace(s[i]))
				i++;
		}

		private static object Value(string s, ref int i)
		{
			Ws(s, ref i);
			char c = s[i];
			if (c == '{')
			{
				var d = new Dictionary<string, object>();
				i++;
				Ws(s, ref i);
				if (s[i] == '}') { i++; return d; }
				while (true)
				{
					Ws(s, ref i);
					string k = Str(s, ref i);
					Ws(s, ref i);
					i++; // :
					d[k] = Value(s, ref i);
					Ws(s, ref i);
					if (s[i++] == '}')
						return d;
				}
			}
			if (c == '[')
			{
				var l = new List<object>();
				i++;
				Ws(s, ref i);
				if (s[i] == ']') { i++; return l; }
				while (true)
				{
					l.Add(Value(s, ref i));
					Ws(s, ref i);
					if (s[i++] == ']')
						return l;
				}
			}
			if (c == '"')
				return Str(s, ref i);
			if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
			if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
			if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
			int start = i;
			while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
				i++;
			return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
		}

		private static string Str(string s, ref int i)
		{
			var sb = new StringBuilder();
			i++; // opening quote
			while (s[i] != '"')
			{
				if (s[i] == '\\')
				{
					i++;
					char e = s[i];
					switch (e)
					{
						case 'n': sb.Append('\n'); break;
						case 't': sb.Append('\t'); break;
						case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; break;
						default: sb.Append(e); break;
					}
				}
				else
					sb.Append(s[i]);
				i++;
			}
			i++;
			return sb.ToString();
		}

		public static double D(object o) => o is double d ? d : 0.0;
		public static List<object> L(object o) => o as List<object> ?? new List<object>();
	}
}
