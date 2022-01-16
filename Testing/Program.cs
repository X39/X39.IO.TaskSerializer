using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using X39.IO;

public static class Program
{
    private static async Task Async()
    {
        var i = 0;
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
        var asyncDirect = Async();
        var serializer = new TaskSerializer(Stream.Null);
        serializer.Serialize(asyncDirect);
        Console.ReadLine();
    }
}