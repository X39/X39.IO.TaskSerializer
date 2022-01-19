using System.Reflection;
using System.Runtime.CompilerServices;
using X39.Util;

namespace X39.IO;

public static class Diag
{

    private static async Task WriteAsJson(object? data, IAsyncStateMachine? stateMachine)
    {
        await using var stringWriter = new StringWriter();
        WriteAsJson(stringWriter, stateMachine, 0, new List<object>());
        Console.WriteLine(stringWriter.ToString());
    }
    private static void WriteAsJson(TextWriter writer, object? data, int depth, List<object> visited)
    {
        // ReSharper disable once AccessToModifiedClosure
        string Tab() => new(' ', 2 * depth);

        void WritePropertyStart(string key)
        {
            writer.Write(string.Concat(
                Tab(),
                @"""",
                key,
                @""": "));
        }
        void WritePropertyEnd(bool tailingComma = true)
        {
            writer.WriteLine(tailingComma ? @"," : string.Empty);
        }
        void WriteProperty(string key, string? value, bool tailingComma = true)
        {
            WritePropertyStart(key);
            if (value is null)
            {
                writer.Write("null");
            }
            else
            {
                writer.Write(string.Concat(
                    @"""",
                    value,
                    @""""));   
            }
            WritePropertyEnd(tailingComma);
        }
#pragma warning disable CS8321
        void WriteArray(string key, Action act)
#pragma warning restore CS8321
        {
            writer.WriteLine(string.Concat(
                Tab(),
                @"""",
                key,
                @""": ["));
            Indent(act);
            writer.WriteLine(string.Concat(
                Tab(),
                @"],"));
        }
#pragma warning disable CS8321
        void WriteObject(string key, Action act)
#pragma warning restore CS8321
        {
            writer.WriteLine(string.Concat(
                Tab(),
                @"""",
                key,
                @""": {"));
            Indent(act);
            writer.WriteLine(string.Concat(
                Tab(),
                @"},"));
        }

        void Indent(Action act)
        {
            // ReSharper disable once AccessToModifiedClosure
            depth++;
            act();
            depth--;
        }

        writer.WriteLine("{");
        Indent(() =>
        {
            WriteProperty("type", data?.GetType().FullName() ?? "");
            WriteProperty("value", data?.ToString() ?? "null", data is not null);
            if (data is null)
                return;
            var index = visited.IndexOf(data);
            if (index != -1)
            {
                WriteProperty("$ref", index.ToString(), false);
                return;
            }

            visited.Add(data);
            index = visited.IndexOf(data);

            foreach (var fieldInfo in data.GetType()
                         .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var value = fieldInfo.GetValue(data);
                if (value is Array array)
                {
                    WriteArray(fieldInfo.Name, () =>
                    {
                        bool first = true;
                        foreach (var val in array)
                        {
                            if (first)
                                first = false;
                            else
                            {
                                writer.WriteLine(",");
                                writer.Write(Tab());
                            }

                            // ReSharper disable once AccessToModifiedClosure
                            WriteAsJson(writer, val, depth + 1, visited);
                        }
                        writer.WriteLine();
                    });
                }
                else
                {
                    WritePropertyStart(fieldInfo.Name);
                    // ReSharper disable once AccessToModifiedClosure
                    WriteAsJson(writer, value, depth + 1, visited);
                    WritePropertyEnd();   
                }
            }
            WriteProperty("$id", index.ToString(), false);
        });
        writer.Write(string.Concat(Tab(), "}"));
    }
}