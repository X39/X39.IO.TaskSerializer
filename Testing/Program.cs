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
        await awaitableDispatcher.Dispatch();
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
        Console.WriteLine("Starting async task...");
        Task.Run(() => Async(awaitableDispatcher));
        Console.WriteLine("Hit enter to continue");
        Console.ReadLine();
    }

    private static void AwaitableDispatcherOnAwaitableReceived(AwaitableDispatcher sender, MethodInfo moveNext, IAsyncStateMachine asyncStateMachine)
    {
        // using var stream = new FileStream("serialized.task", FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        var stream = new MemoryStream();
        {
            var serializer = new BinaryStateMachineSerializer(stream);
            serializer.Serialize(moveNext, asyncStateMachine);
            Console.WriteLine("Task Serialized.");
        }
        stream = new MemoryStream(stream.ToArray());
        {
            var serializer = new BinaryStateMachineSerializer(stream);
            serializer.Deserialize();
            Console.WriteLine("Task Deserialized.");
        }
    }
}