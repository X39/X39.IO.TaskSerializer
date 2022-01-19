using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using X39.Util;

namespace X39.IO;

[PublicAPI]
public class BinaryStateMachineSerializer
{
    private readonly Stream _stream;


    public delegate void UnableToSerializeHandler(BinaryStateMachineSerializer binaryStateMachineSerializer, Type type,
        object value, ref bool serialized);

    public event UnableToSerializeHandler? UnableToSerialize;

    public BinaryStateMachineSerializer(Stream stream)
    {
        _stream = stream;
    }


    public void Serialize(MethodInfo moveNext, IAsyncStateMachine stateMachine)
    {
        using var writer = new BinaryWriter(_stream);
        WriteEndianness(writer);
        SerializeAsyncStateMachine(writer, moveNext, stateMachine);
    }

    private static void WriteEndianness(BinaryWriter writer)
    {
        writer.Write(BitConverter.IsLittleEndian
            ? (byte) EType.LittleEndian
            : (byte) EType.BigEndian);
    }

    private void SerializeAsyncStateMachine(BinaryWriter writer, MethodInfo methodInfo, IAsyncStateMachine stateMachine)
    {
        // ReSharper disable SuggestBaseTypeForParameter
        static bool IsStateMachineBuilder(FieldInfo fieldInfo)
            => fieldInfo.Name == "<>t__builder";

        static bool IsAwaiterHolder(FieldInfo fieldInfo)
            => fieldInfo.Name.StartsWith("<>u__");

        static bool IsMethodVariable(FieldInfo fieldInfo)
            => fieldInfo.Name.StartsWith("<")
               && fieldInfo.Name.Contains(">5__");

        static bool IsStateVariable(FieldInfo fieldInfo)
            => fieldInfo.Name == "<>1__state"
               && fieldInfo.FieldType.IsEquivalentTo(typeof(int));

        static bool HasSpecialChars(FieldInfo fieldInfo)
            => fieldInfo.Name.IndexOfAny(new[] {'>', '<'}) != -1;
        // ReSharper restore SuggestBaseTypeForParameter

        writer.Write((byte) EType.StateMachine);
        writer.Write(methodInfo.DeclaringType?.FullName()
                     ?? throw new NullReferenceException("Failed to receive DeclaringType of MoveNext method."));
        writer.Write(methodInfo.Name);
        var type = stateMachine.GetType();
        foreach (var fieldInfo in type.GetFields(
                     AsyncMethodBuilderUtil.AllInstanceBindingFlags))
        {
            // ReSharper disable InvertIf
            if (IsStateMachineBuilder(fieldInfo))
            {
                SerializeStateMachineBuilder(
                    writer,
                    fieldInfo.FieldType,
                    fieldInfo.GetValue(stateMachine)
                    ?? throw new NullReferenceException("State machine builder was null."));
                continue;
            }

            if (IsAwaiterHolder(fieldInfo))
            {
                SerializeAwaiter(
                    writer,
                    fieldInfo.FieldType,
                    fieldInfo.GetValue(stateMachine)
                    ?? throw new NullReferenceException("State machine builder was null."));
                continue;
            }

            if (IsStateVariable(fieldInfo))
            {
                SerializeStateVariable(
                    writer,
                    (int) (fieldInfo.GetValue(stateMachine)
                           ?? throw new NullReferenceException("State was null.")));
                continue;
            }

            if (IsMethodVariable(fieldInfo)
                || !HasSpecialChars(fieldInfo))
            {
                SerializeValue(
                    writer,
                    fieldInfo.FieldType,
                    fieldInfo.GetValue(stateMachine)
                    ?? throw new NullReferenceException("State machine builder was null."));
                continue;
            }

            if (fieldInfo == typeof(TaskSerializerPromise)
                    .GetField(
                        nameof(TaskSerializerPromise._callbackAdded),
                        AsyncMethodBuilderUtil.AllInstanceBindingFlags))
                continue;

            throw new Exception("Failed to serialize state-machine. Please report this.")
            {
                Data =
                {
                    {"IAsyncStateMachine", type.FullName()},
                    {"field", fieldInfo.Name},
                    {"field-type", fieldInfo.FieldType.FullName()},
                }
            };
            // ReSharper restore InvertIf
        }
    }

    private void SerializeStateMachineBuilder(BinaryWriter writer, Type type, object value)
    {
        writer.Write((byte) EType.StateMachineBuilder);
        writer.Write(type.FullName());
        ;


        if (!AsyncMethodBuilderUtil.SolveForStateMachine(type, value, out var awaitable, out var moveNextAction))
        {
            writer.Write((byte) EType.NoStateMachine);
        }
        else if (moveNextAction?.Target is not IAsyncStateMachine asyncStateMachine)
        {
            writer.Write((byte) EType.EndOfStateStream);
        }
        else
        {
            SerializeAsyncStateMachine(writer, moveNextAction.Method, asyncStateMachine);
        }
    }


    private static void SerializeAwaiter(BinaryWriter writer, Type type, object value)
    {
        // empty
    }

    private static void SerializeStateVariable(BinaryWriter writer, int state)
    {
        writer.Write((byte) EType.StateVariable);
        writer.Write(state);
    }

    /// <summary>
    /// Serializes a value into the <paramref name="writer"/>.
    /// </summary>
    /// <remarks>
    /// Will raise <see cref="UnableToSerialize"/> for any type but:
    /// <list type="bullet">
    ///     <item>Enum values</item>
    ///     <item><see cref="byte"/></item>
    ///     <item><see cref="ushort"/></item>
    ///     <item><see cref="uint"/></item>
    ///     <item><see cref="ulong"/></item>
    ///     <item><see cref="sbyte"/></item>
    ///     <item><see cref="short"/></item>
    ///     <item><see cref="int"/></item>
    ///     <item><see cref="long"/></item>
    ///     <item><see cref="float"/></item>
    ///     <item><see cref="double"/></item>
    ///     <item><see cref="decimal"/></item>
    ///     <item><see cref="string"/></item>
    ///     <item><see cref="DateTime"/></item>
    ///     <item><see cref="DateOnly"/></item>
    ///     <item><see cref="TimeOnly"/></item>
    ///     <item><see cref="TimeSpan"/></item>
    /// </list>
    /// </remarks>
    /// <param name="writer"></param>
    /// <param name="type"></param>
    /// <param name="value"></param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a given type could not be serialized.
    /// </exception>
    private void SerializeValue(BinaryWriter writer, Type type, object value)
    {
        writer.Write((byte) EType.Value);
        switch (value)
        {
            case byte val:
                writer.Write(val);
                return;
            case ushort val:
                writer.Write(val);
                return;
            case uint val:
                writer.Write(val);
                return;
            case ulong val:
                writer.Write(val);
                return;
            case sbyte val:
                writer.Write(val);
                return;
            case short val:
                writer.Write(val);
                return;
            case int val:
                writer.Write(val);
                return;
            case long val:
                writer.Write(val);
                return;
            case float val:
                writer.Write(val);
                return;
            case double val:
                writer.Write(val);
                return;
            case decimal val:
                writer.Write(val);
                return;
            case string val:
                writer.Write(val);
                return;
            case DateTime val:
                writer.Write(val.Year);
                writer.Write(val.Month);
                writer.Write(val.Day);
                writer.Write(val.Hour);
                writer.Write(val.Minute);
                writer.Write(val.Second);
                writer.Write(val.Millisecond);
                return;
            case DateOnly val:
                writer.Write(val.Year);
                writer.Write(val.Month);
                writer.Write(val.Day);
                return;
            case TimeOnly val:
                writer.Write(val.Hour);
                writer.Write(val.Minute);
                writer.Write(val.Second);
                writer.Write(val.Millisecond);
                return;
            case TimeSpan val:
                writer.Write(val.Days);
                writer.Write(val.Hours);
                writer.Write(val.Minutes);
                writer.Write(val.Seconds);
                writer.Write(val.Milliseconds);
                return;
            case AwaitableDispatcher:
                return;
            default:
            {
                var serialized = false;
                UnableToSerialize?.Invoke(this, type, value, ref serialized);
                if (!serialized)
                    throw new InvalidOperationException("Failed to serialize type.")
                    {
                        Data =
                        {
                            {"type", type.FullName()},
                        }
                    };
                return;
            }
        }
    }


    private enum EType : byte
    {
        /// <summary>
        /// Endianness when serializing was Little-Endian
        /// </summary>
        LittleEndian,

        /// <summary>
        /// Endianness when serializing was Big-Endian
        /// </summary>
        BigEndian,

        /// <summary>
        /// State variable follows
        /// </summary>
        StateVariable,

        /// <summary>
        /// A state machine builder follows
        /// </summary>
        StateMachineBuilder,

        /// <summary>
        /// No state machine is present
        /// </summary>
        NoStateMachine,

        /// <summary>
        /// State machine follows
        /// </summary>
        StateMachine,

        /// <summary>
        /// The state machine refers to the final task (self-referencing move-next target).
        /// </summary>
        EndOfStateStream,

        /// <summary>
        /// A value follows.
        /// </summary>
        Value
    }
}

internal static class AsyncMethodBuilderUtil
{
    internal const BindingFlags AllInstanceBindingFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void SolveForStateMachine(Type type, object value, out object? stateMachine)
    {
        GetStateMachineField(
            value,
            out _,
            out stateMachine);
    }

    public static bool SolveForStateMachine(Type type, object value, out object awaitable, out Action? moveNextAction)
    {
        GetMTaskField(type, value, out var awaitableFieldInfo, out awaitable);
        GetStateMachineField(
            awaitable,
            out _,
            out var asyncStateMachine);
        if (asyncStateMachine is null)
        {
            moveNextAction = null;
            return false;
        }

        var moveNextActionTmp = GetMoveNextAction(awaitable);
        if (moveNextActionTmp.Target == awaitable || moveNextActionTmp.Target is null)
        {
            moveNextAction = null!;
            return true;
        }

        moveNextAction = moveNextActionTmp;
        return true;
    }

    /// <summary>
    /// Looks up a field named <code>_moveNextAction</code> or a _single_ field of type <see cref="Action"/>
    /// on <paramref name="awaitable"/>.
    /// </summary>
    /// <param name="awaitable"></param>
    /// <returns></returns>
    /// <exception cref="NullReferenceException"></exception>
    private static Action GetMoveNextAction(object awaitable)
    {
        var moveNextFieldInfo =
            awaitable.GetType().GetField(
                "_moveNextAction", AllInstanceBindingFlags)
            ?? awaitable.GetType()
                .GetFields(AllInstanceBindingFlags)
                .SingleOrDefault(q => q.FieldType.IsEquivalentTo(typeof(Action)))
            ?? throw new NullReferenceException("Cannot locate move next action on awaitable.");
        var moveNextAction = moveNextFieldInfo.GetValue(awaitable) as Action
                             ?? throw new NullReferenceException(
                                 "Failed to receive move next action of awaitable.");
        return moveNextAction;
    }

    private static void GetStateMachineField(object value, out Type stateMachineType, out object? stateMachine)
    {
        var stateMachineField = value.GetType().GetField("StateMachine", AllInstanceBindingFlags);
        stateMachineType = stateMachineField?.FieldType!;
        stateMachine = stateMachineField?.GetValue(value)
                       ?? throw new NullReferenceException("Failed to receive state machine.")
                       {
                           Data =
                           {
                               {"type", value.GetType().FullName()},
                               {"BindingFlags", AllInstanceBindingFlags},
                               {"FieldName", "StateMachine"},
                           }
                       };
    }

    private static void GetMTaskField(Type type, object value, out FieldInfo fieldInfo, out object fieldValue)
    {
        fieldInfo = type.GetField("m_task", AllInstanceBindingFlags)!;
        // ReSharper disable once ConstantConditionalAccessQualifier
        fieldValue = fieldInfo?.GetValue(value)
                     ?? throw new NullReferenceException("Failed to receive task of state machine builder.")
                     {
                         Data =
                         {
                             {"type", type.FullName()},
                             {"BindingFlags", AllInstanceBindingFlags},
                             {"FieldName", "StateMachine"},
                         }
                     };
    }
}