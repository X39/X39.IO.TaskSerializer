using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using X39.IO;
using X39.Util.Threading.Tasks;

public static class Program
{
    private static async Task Async(AwaitableDispatcher awaitableDispatcher)
    {
        await awaitableDispatcher.Serialize();
        await Async_(0);
        await Async_(5);
        await Async_(10);
        await Async_(15);
        await Async_(20);
    }
    private static async Task Async_(int i)
    {
        await Task.Delay(1000);
        Debugger.Log(0, "", i++.ToString());
        await Task.Delay(1000);
        Debugger.Log(0, "", i++.ToString());
        await Task.Delay(1000);
        Debugger.Log(0, "", i++.ToString());
        await Task.Delay(1000);
        Debugger.Log(0, "", i.ToString());
    }
    public static void Main()
    {
        using var memoryStream = new MemoryStream();
        var awaitableDispatcher = new AwaitableDispatcher();
        awaitableDispatcher.AwaitableReceived += AwaitableDispatcherOnAwaitableReceived;
        Task.Run(() => Async(awaitableDispatcher));
        Console.ReadLine();
    }

    private static void AwaitableDispatcherOnAwaitableReceived(AwaitableDispatcher sender, MethodInfo moveNext, IAsyncStateMachine asyncStateMachine)
    {
        using var file = new FileStream("serialized.task", FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        var serializer = new BinaryStateMachineSerializer(file);
        serializer.Serialize(moveNext, asyncStateMachine);
    }
}