using System.Reflection;
using JetBrains.Annotations;
using X39.Util;

namespace X39.IO;

[PublicAPI]
public class TaskSerializer : IAsyncDisposable
{
    private readonly Stream _streamImplementation;
    private readonly BinaryWriter _writer;

    public TaskSerializerConfig Config { get; }

    public TaskSerializer(Stream stream)
    {
        _streamImplementation = stream;
        _writer = new BinaryWriter(stream);
        Config = TaskSerializerConfig.Default;
    }

    public TaskSerializer(Stream stream, TaskSerializerConfig config)
    {
        _streamImplementation = stream;
        _writer = new BinaryWriter(stream);
        Config = config;
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        await _streamImplementation.DisposeAsync();
    }

    public async Task Serialize<T>(T data)
        where T : notnull
    {
        var solver = Config.GetSolver<T>();
        var stateMachine = solver(data);
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
                WritePropertyStart(fieldInfo.Name);
                // ReSharper disable once AccessToModifiedClosure
                WriteAsJson(writer, fieldInfo.GetValue(data), depth + 1, visited);
                WritePropertyEnd();
            }
            WriteProperty("$id", index.ToString(), false);
        });
        writer.Write(string.Concat(Tab(), "}"));
    }
}