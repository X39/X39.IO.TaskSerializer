using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using X39.Util;

namespace X39.IO;

[PublicAPI]
public class TaskSerializerConfig
{
    public static TaskSerializerConfig Default => DefaultConfig ??= CreateDefaultTaskSerializerConfig();

    private static TaskSerializerConfig CreateDefaultTaskSerializerConfig()
    {
        Delegate CreateStateMachineFieldSolvingDelegate(Type type)
        {
            var property = type.GetField("StateMachine",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic);
            if (property is null)
                throw new NullReferenceException("Unknown task parameter mapping.")
                {
                    Data =
                    {
                        {"type", type.FullName()},
                    }
                };
            var paramExpression = Expression.Parameter(type);
            var propertyExpression = Expression.Field(paramExpression, property);
            var lambdaExpression = Expression.Lambda(propertyExpression, paramExpression);
            var @delegate = lambdaExpression.Compile();
            return @delegate;
        }

        var config = new TaskSerializerConfig();

        // ToDo: Get state machine object of task using reflection
        var stateMachineSolvingDelegates = new Dictionary<Type, Delegate>();
        config.RegisterStateMachineSolver<Task>((t) =>
        {
            var type = t.GetType();
            if (!stateMachineSolvingDelegates.TryGetValue(type, out var @delegate))
            {
                @stateMachineSolvingDelegates[type] = @delegate = CreateStateMachineFieldSolvingDelegate(type);
            }

            return @delegate.DynamicInvoke(t) as IAsyncStateMachine;
        });
        config.RegisterStateMachineSolver<ValueTask>((t) =>
        {
            var type = t.AsTask().GetType();
            if (!stateMachineSolvingDelegates.TryGetValue(type, out var @delegate))
            {
                @stateMachineSolvingDelegates[type] = @delegate = CreateStateMachineFieldSolvingDelegate(type);
            }

            return @delegate.DynamicInvoke(t) as IAsyncStateMachine;
        });

        return config;
    }

    private static TaskSerializerConfig? DefaultConfig;

    private readonly Dictionary<Type, object> _stateMachineSolvers = new();

    public Func<T, IAsyncStateMachine?> GetSolver<T>()
        => (Func<T, IAsyncStateMachine?>) _stateMachineSolvers[typeof(T)];
    public void RegisterStateMachineSolver<T>(Func<T, IAsyncStateMachine?> stateMachineSolver)
    {
        _stateMachineSolvers.Add(typeof(T), stateMachineSolver);
    }
}