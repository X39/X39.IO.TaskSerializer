using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace X39.IO;

[PublicAPI]
public readonly struct TaskSerializerPromiseAwaiter : INotifyCompletion
{
    private readonly TaskSerializerPromise _taskSerializerPromise;
    public bool IsCompleted => _taskSerializerPromise.State != EPromiseState.Primed;

    public void GetResult()
    {
        var tmp = _taskSerializerPromise;
        if (tmp.State == EPromiseState.Primed)
            SpinWait.SpinUntil(() => tmp.State != EPromiseState.Primed);
        switch (tmp.State)
        {
            case EPromiseState.Completed:
                return;
            case EPromiseState.Failed:
                throw new AggregateException(
                    tmp.Exception
                    ?? throw new Exception("Failure set without exception"));
            case EPromiseState.Primed:
            default: throw new InvalidOperationException();
        }
    }

    internal TaskSerializerPromiseAwaiter(TaskSerializerPromise taskSerializerPromise) =>
        _taskSerializerPromise = taskSerializerPromise;

    public void OnCompleted(Action completion) => _taskSerializerPromise.AddCallback(completion);
}