using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace X39.IO;

[PublicAPI]
public class TaskSerializerPromise
{
    internal readonly Action<TaskSerializerPromise, Action> _callbackAdded;


    internal Exception? Exception;
    internal EPromiseState State;
    private readonly List<Action> _callbacks = new();
    public TaskSerializerPromiseAwaiter GetAwaiter() => new(this);

    public TaskSerializerPromise(Action<TaskSerializerPromise, Action> callbackAdded)
    {
        _callbackAdded = callbackAdded;
    }

    internal void AddCallback(Action action)
    {
        _callbacks.Add(action);
        _callbackAdded(this, action);
    }

    private void Continue()
    {
        var exceptions = new List<Exception>();
        foreach (var callback in _callbacks)
        {
            try
            {
                callback();
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }
        }

        if (exceptions.Any())
        {
            throw new AggregateException(exceptions);
        }
    }

    private async Task ContinueAsync()
    {
        var exceptions = new List<Exception>();
        var tasks = _callbacks.Select(callback => Task.Run(() =>
        {
            try
            {
                callback();
            }
            catch (Exception e)
            {
                lock (exceptions) exceptions.Add(e);
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        if (exceptions.Any())
        {
            throw new AggregateException(exceptions);
        }
    }

    public bool IsComplete => State != EPromiseState.Primed;

    public Task CompleteAsync()
    {
        if (IsComplete)
            throw new InvalidOperationException("Already completed.");
        State = EPromiseState.Completed;
        return ContinueAsync();
    }

    public void Complete()
    {
        if (IsComplete)
            throw new InvalidOperationException("Already completed.");
        State = EPromiseState.Completed;
        Continue();
    }

    public AggregateException? Complete(Exception exception)
    {
        if (IsComplete)
            throw new InvalidOperationException("Already completed.");
        Exception = exception;
        State = EPromiseState.Failed;
        try
        {
            Continue();
        }
        catch (AggregateException ex)
        {
            return ex;
        }

        return null;
    }

    public async Task<AggregateException?> CompleteAsync(Exception exception)
    {
        if (IsComplete)
            throw new InvalidOperationException("Already completed.");
        Exception = exception;
        State = EPromiseState.Failed;
        try
        {
            await ContinueAsync();
        }
        catch (AggregateException ex)
        {
            return ex;
        }

        return null;
    }
}