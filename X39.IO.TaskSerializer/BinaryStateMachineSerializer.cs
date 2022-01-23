using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
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

    public delegate void UnableToDeserializeHandler(BinaryStateMachineSerializer binaryStateMachineSerializer,
        Type type,
        ref object? value, ref bool serialized);

    public event UnableToDeserializeHandler? UnableToDeserialize;

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

    // ReSharper disable SuggestBaseTypeForParameter
    private static bool IsStateMachineBuilder(FieldInfo fieldInfo)
        => fieldInfo.Name == "<>t__builder";

    private static bool IsAwaiterHolder(FieldInfo fieldInfo)
        => fieldInfo.Name.StartsWith("<>u__");

    private static bool IsThisVariable(FieldInfo fieldInfo)
        => fieldInfo.Name == "4__this";

    private static bool IsMethodVariable(FieldInfo fieldInfo)
        => fieldInfo.Name.StartsWith("<")
           && fieldInfo.Name.Contains(">5__");

    private static bool IsStateVariable(FieldInfo fieldInfo)
        => fieldInfo.Name == "<>1__state"
           && fieldInfo.FieldType.IsEquivalentTo(typeof(int));

    private static bool HasSpecialChars(FieldInfo fieldInfo)
        => fieldInfo.Name.IndexOfAny(new[] {'>', '<'}) != -1;
    // ReSharper restore SuggestBaseTypeForParameter

    private void SerializeAsyncStateMachine(BinaryWriter writer, MethodInfo methodInfo, IAsyncStateMachine stateMachine)
    {
        writer.Write((byte) EType.StateMachine);

        SerializeMethodInfo(writer, methodInfo);
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
                || !HasSpecialChars(fieldInfo)
                || IsThisVariable(fieldInfo))
            {
                SerializeValue(
                    writer,
                    fieldInfo.FieldType,
                    fieldInfo.GetValue(stateMachine));
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

    private static void SerializeMethodInfo(BinaryWriter writer, MethodInfo methodInfo)
    {
        writer.Write((byte) EType.MethodInfoData);
        writer.Write(methodInfo.DeclaringType?.AssemblyQualifiedName
                     ?? throw new NullReferenceException("Failed to receive DeclaringType of MethodInfo.")
                     {
                         Data =
                         {
                             {"MethodInfo", methodInfo}
                         }
                     });
        writer.Write(methodInfo.Name);
        var parameters = methodInfo.GetParameters();
        writer.Write(parameters.Length);
        foreach (var parameterInfo in parameters)
        {
            writer.Write(parameterInfo.ParameterType.AssemblyQualifiedName
                         ?? throw new NullReferenceException("Failed to receive ParameterTypes AssemblyQualifiedName.")
                         {
                             Data =
                             {
                                 {"MethodInfo", methodInfo},
                                 {"ParameterInfo", parameterInfo},
                             }
                         });
        }
    }

    private static (MethodInfo methodInfo, Type declaringType) DeserializeMethodInfo(BinaryReader reader)
    {
        if (EType.MethodInfoData != (EType) reader.ReadByte())
            throw new SerializationException("Invalid EType.");
        var declaringTypeAQN = reader.ReadString();
        var declaringType = Type.GetType(declaringTypeAQN)
                            ?? throw new NullReferenceException("Failed to receive DeclaringType of method.")
                            {
                                Data =
                                {
                                    {"AssemblyQualifiedName", declaringTypeAQN},
                                }
                            };
        var methodName = reader.ReadString();
        var parameterCount = reader.ReadInt32();
        var parameters = new Type[parameterCount];
        for (var i = 0; i < parameterCount; i++)
        {
            var parameterTypeAQN = reader.ReadString();
            var parameterType = Type.GetType(parameterTypeAQN)
                                ?? throw new SerializationException(
                                    "Failed to receive parameter type of method info.")
                                {
                                    Data =
                                    {
                                        {"DeclaringType", declaringType},
                                        {"MethodName", methodName},
                                        {"ParameterCount", parameterCount},
                                        {"AssemblyQualifiedNameOfParameter", parameterTypeAQN},
                                    }
                                };
            parameters[i] = parameterType;
        }

        var methodInfo = declaringType.GetMethod(methodName, AsyncMethodBuilderUtil.AllInstanceBindingFlags, parameters)
                         ?? throw new SerializationException(
                             "Failed to receive MethodInfo.")
                         {
                             Data =
                             {
                                 {"DeclaringType", declaringType},
                                 {"MethodName", methodName},
                                 {"ParameterCount", parameterCount},
                             }
                         };
        return (methodInfo, declaringType);
    }

    private void SerializeStateMachineBuilder(BinaryWriter writer, Type type, object value)
    {
        writer.Write((byte) EType.StateMachineBuilder);
        writer.Write(type.AssemblyQualifiedName);


        if (!AsyncMethodBuilderUtil.SolveForStateMachine(
                type, value, out var awaitable, out var moveNextAction))
        {
            writer.Write((byte) EType.NoStateMachine);
        }
        else if (moveNextAction?.Target is not IAsyncStateMachine asyncStateMachine)
        {
            writer.Write((byte) EType.EndOfStateStream);
        }
        else
        {
            writer.Write((byte) EType.SubStateMachine);
            SerializeAsyncStateMachine(writer, moveNextAction.Method, asyncStateMachine);
        }
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
    /// Will raise <see cref="UnableToDeserialize"/> for any type but:
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
    private void SerializeValue(BinaryWriter writer, Type type, object? value)
    {
        if (value is null)
        {
            writer.Write((byte) EType.NullValue);
            return;
        }

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
                // ReSharper disable RedundantCast
                writer.Write((int) val.Year);
                writer.Write((int) val.Month);
                writer.Write((int) val.Day);
                writer.Write((int) val.Hour);
                writer.Write((int) val.Minute);
                writer.Write((int) val.Second);
                writer.Write((int) val.Millisecond);
                return;
            case DateOnly val:
                writer.Write((int) val.Year);
                writer.Write((int) val.Month);
                writer.Write((int) val.Day);
                return;
            case TimeOnly val:
                writer.Write((int) val.Hour);
                writer.Write((int) val.Minute);
                writer.Write((int) val.Second);
                writer.Write((int) val.Millisecond);
                return;
            case TimeSpan val:
                writer.Write((int) val.Days);
                writer.Write((int) val.Hours);
                writer.Write((int) val.Minutes);
                writer.Write((int) val.Seconds);
                writer.Write((int) val.Milliseconds);
                // ReSharper restore RedundantCast
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
        Value,
        SubStateMachine,
        NullValue,
        MethodInfoData
    }

    public void Deserialize()
    {
        using var reader = new BinaryReader(_stream);
        var endian = BitConverter.IsLittleEndian ? EType.LittleEndian : EType.BigEndian;
        if (endian != (EType) reader.ReadByte())
            throw new SerializationException("Cannot deserialize different endianness.");
        var (methodInfo, stateMachine) = DeserializeAsyncStateMachine(reader);
    }

    private (MethodInfo methodInfo, object stateMachine) DeserializeAsyncStateMachine(BinaryReader reader)
    {
        if (reader.ReadByte() != (byte) EType.StateMachine)
            throw new SerializationException("Invalid EType.");

        var (methodInfo, stateMachineType) = DeserializeMethodInfo(reader);
        var stateMachine = stateMachineType.CreateInstance();
        foreach (var fieldInfo in stateMachineType.GetFields(
                     AsyncMethodBuilderUtil.AllInstanceBindingFlags))
        {
            // ReSharper disable InvertIf
            if (IsStateMachineBuilder(fieldInfo))
            {
                var (asyncMethodBuilderType, subStateMachine) = DeserializeStateMachineBuilder(reader);

                var asyncMethodBuilderCreateMethodInfo =
                    asyncMethodBuilderType.GetMethod("Create", AsyncMethodBuilderUtil.AllInstanceBindingFlags)
                    ?? throw new SerializationException("Failed to locate Create method on async-method-builder.")
                    {
                        Data =
                        {
                            {"method-builder-type", asyncMethodBuilderType},
                        }
                    };
                var asyncMethodBuilder = asyncMethodBuilderCreateMethodInfo.Invoke(null, Array.Empty<object>());
                fieldInfo.SetValue(stateMachine, asyncMethodBuilder);
                continue;
            }

            if (IsAwaiterHolder(fieldInfo))
            {
                continue;
            }

            if (IsStateVariable(fieldInfo))
            {
                var state = DeserializeStateVariable(reader);
                fieldInfo.SetValue(stateMachine, state);
                continue;
            }

            if (IsThisVariable(fieldInfo))
            {
                var value = DeserializeValue(reader, fieldInfo.FieldType);
                fieldInfo.SetValue(stateMachine, value);
                continue;
            }

            if (IsMethodVariable(fieldInfo)
                || !HasSpecialChars(fieldInfo))
            {
                var value = DeserializeValue(reader, fieldInfo.FieldType);
                fieldInfo.SetValue(stateMachine, value);
                continue;
            }

            if (fieldInfo == typeof(TaskSerializerPromise)
                    .GetField(
                        nameof(TaskSerializerPromise._callbackAdded),
                        AsyncMethodBuilderUtil.AllInstanceBindingFlags))
                continue;

            throw new Exception("Failed to Deserialize state-machine. Please report this.")
            {
                Data =
                {
                    {"IAsyncStateMachine", stateMachineType.FullName()},
                    {"field", fieldInfo.Name},
                    {"field-type", fieldInfo.FieldType.FullName()},
                }
            };
            // ReSharper restore InvertIf
        }

        throw new NotImplementedException();
    }

    private (Type stateMachineBuilderType, object? stateMachine) DeserializeStateMachineBuilder(BinaryReader reader)
    {
        var stateMachineBuilderTypeAQN = reader.ReadString();
        var stateMachineBuilderType = Type.GetType(stateMachineBuilderTypeAQN)
                                      ?? throw new SerializationException(
                                          "Failed to deserialize state-machine-builder.")
                                      {
                                          Data =
                                          {
                                              {"AssemblyQualifiedName", stateMachineBuilderTypeAQN}
                                          }
                                      };

        var type = (EType) reader.ReadByte();

        switch (type)
        {
            case EType.NoStateMachine:
            case EType.EndOfStateStream:
                return (stateMachineBuilderType, null);
            case EType.SubStateMachine:
                var (_, stateMachine) = DeserializeAsyncStateMachine(reader);
                return (stateMachineBuilderType, stateMachine);
            case EType.LittleEndian:
            case EType.BigEndian:
            case EType.StateVariable:
            case EType.StateMachineBuilder:
            case EType.StateMachine:
            case EType.Value:
            case EType.NullValue:
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static int DeserializeStateVariable(BinaryReader reader)
    {
        return reader.ReadInt32();
    }

    /// <summary>
    /// Deserializes a value into the <paramref name="reader"/>.
    /// </summary>
    /// <remarks>
    /// Will raise <see cref="UnableToDeserialize"/> for any type but:
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
    /// <param name="reader"></param>
    /// <param name="dataType"></param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a given type could not be Deserialized.
    /// </exception>
    private object? DeserializeValue(BinaryReader reader, Type dataType)
    {
        var type = (EType) reader.ReadByte();
        if (type == EType.NullValue)
            return null;
        if (dataType.IsEquivalentTo(typeof(byte))) return reader.ReadByte();
        if (dataType.IsEquivalentTo(typeof(ushort))) return reader.ReadUInt16();
        if (dataType.IsEquivalentTo(typeof(uint))) return reader.ReadUInt32();
        if (dataType.IsEquivalentTo(typeof(ulong))) return reader.ReadUInt64();
        if (dataType.IsEquivalentTo(typeof(sbyte))) return reader.ReadSByte();
        if (dataType.IsEquivalentTo(typeof(short))) return reader.ReadInt16();
        if (dataType.IsEquivalentTo(typeof(int))) return reader.ReadInt32();
        if (dataType.IsEquivalentTo(typeof(long))) return reader.ReadInt64();
        if (dataType.IsEquivalentTo(typeof(float))) return reader.ReadSingle();
        if (dataType.IsEquivalentTo(typeof(double))) return reader.ReadDouble();
        if (dataType.IsEquivalentTo(typeof(decimal))) return reader.ReadDecimal();
        if (dataType.IsEquivalentTo(typeof(string))) return reader.ReadString();
        if (dataType.IsEquivalentTo(typeof(DateTime)))
        {
            var year = reader.ReadInt32();
            var month = reader.ReadInt32();
            var day = reader.ReadInt32();
            var hour = reader.ReadInt32();
            var minute = reader.ReadInt32();
            var second = reader.ReadInt32();
            var millisecond = reader.ReadInt32();
            return new DateTime(
                year,
                month,
                day,
                hour,
                minute,
                second,
                millisecond
            );
        }

        if (dataType.IsEquivalentTo(typeof(DateOnly)))
        {
            var year = reader.ReadInt32();
            var month = reader.ReadInt32();
            var day = reader.ReadInt32();
            return new DateOnly(
                year,
                month,
                day
            );
        }

        if (dataType.IsEquivalentTo(typeof(TimeOnly)))
        {
            var hour = reader.ReadInt32();
            var minute = reader.ReadInt32();
            var second = reader.ReadInt32();
            var millisecond = reader.ReadInt32();
            return new TimeOnly(
                hour,
                minute,
                second,
                millisecond
            );
        }

        if (dataType.IsEquivalentTo(typeof(TimeSpan)))
        {
            var days = reader.ReadInt32();
            var hours = reader.ReadInt32();
            var minutes = reader.ReadInt32();
            var seconds = reader.ReadInt32();
            var milliseconds = reader.ReadInt32();
            return new TimeSpan(
                days,
                hours,
                minutes,
                seconds,
                milliseconds
            );
        }

        if (dataType.IsEquivalentTo(typeof(AwaitableDispatcher)))
            return null;
        {
            var deserialized = false;
            object? value = null;
            UnableToDeserialize?.Invoke(this, dataType, ref value, ref deserialized);
            if (!deserialized)
                throw new InvalidOperationException("Failed to Deserialize type.")
                {
                    Data =
                    {
                        {"type", dataType.FullName()},
                    }
                };
            return null;
        }
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