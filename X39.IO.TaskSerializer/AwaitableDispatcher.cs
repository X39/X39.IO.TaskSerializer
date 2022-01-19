using System.Reflection;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using X39.Util;

namespace X39.IO;

[PublicAPI]
public class AwaitableDispatcher
{
    public delegate void AwaitableReceivedDelegate(AwaitableDispatcher sender, MethodInfo moveNext, IAsyncStateMachine asyncStateMachine);
    public event AwaitableReceivedDelegate? AwaitableReceived;


    public TaskSerializerPromise Dispatch()
    {
        return new TaskSerializerPromise(TaskSerializerPromiseAwaited);
    }

    private void TaskSerializerPromiseAwaited(TaskSerializerPromise taskSerializerPromise, Action moveNextCallback)
    {
        var ev = AwaitableReceived;
        if (ev is null)
        {
            try
            {
                throw new Exception("Cannot dispatch as no one listens to dispatched tasks.");
            }
            catch (Exception ex)
            {
                taskSerializerPromise.Complete(ex);
                return;
            }
        }
        var targetType = moveNextCallback.Target?.GetType()
                         ?? throw new NullReferenceException("Action has no Target.");
        AsyncMethodBuilderUtil.SolveForStateMachine(
            targetType, moveNextCallback.Target, out var stateMachine);
        if (stateMachine is not IAsyncStateMachine asyncStateMachine)
            throw new NullReferenceException("Could not find state-machine for awaitable.");
        if (asyncStateMachine.GetType().GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            throw new InvalidDataException(
                "Expected compiler generated IAsyncStateMachine. " +
                // Yes! We need to make sure **no one** is ever stupid enough to even attempt to dig into the reasoning
                // why this exception was thrown.
                "This check is artificially enforced! " +
                "The reasoning is special handling has to be done with builders etc. and " +
                "no \"guarantee\" beyond explicit naming can be granted for the types. " +
                "DO NOT just add the CompilerGeneratedAttribute to your custom state machine, " +
                "or bad things will happen!");
        ev(this, moveNextCallback.Method, asyncStateMachine);
    }
}