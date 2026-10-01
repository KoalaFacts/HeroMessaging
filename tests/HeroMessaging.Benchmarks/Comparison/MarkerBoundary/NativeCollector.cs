using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace HeroMessaging.Benchmarks;

internal sealed class NativeCollector
{
    private int _stop;
    private int _error;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int OutputCallback(uint type, IntPtr data, nuint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RecordTrace(byte[] command, nuint length, OutputCallback callback);

    internal static bool TryRun(string[] args)
    {
        if (args is ["--native-callback-self-test"])
        {
            NativeCollectorSelfTest.Run();
            return true;
        }

        if (args is ["--native-collector", var library, var commandFile, var seconds])
        {
            var duration = int.Parse(seconds, CultureInfo.InvariantCulture);
            if (duration is < 1 or > 180)
            {
                throw new ArgumentOutOfRangeException(nameof(args), "Collector duration must be bounded.");
            }

            Environment.ExitCode = Invoke(library, File.ReadAllText(commandFile), duration);
            return true;
        }

        if (args is ["--native-logging-self-test", var testLibrary, var log])
        {
            if (File.Exists(log) || string.IsNullOrWhiteSpace(log) || log.Any(char.IsWhiteSpace) ||
                log.Contains('"', StringComparison.Ordinal) || log.Contains('\\', StringComparison.Ordinal) || log.StartsWith('-'))
            {
                throw new ArgumentException("Native logging self-test requires a new, unambiguous log path.", nameof(args));
            }

            var result = Invoke(testLibrary,
                $"--script \"this_is_not_a_valid_diagnostic_script(\" --log-filter debug --log-mode file --log-path {log}", 5);
            if (result != 1 || !File.Exists(log) || !File.ReadAllText(log).Contains("Syntax error", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Native negative logging self-test did not retain the expected syntax failure.");
            }

            Console.WriteLine("Native logging negative self-test passed; no provider or benchmark capture performed.");
            return true;
        }

        return false;
    }

    private static int Invoke(string library, string command, int seconds)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("Native logging adapter requires Linux x64.");
        }

        var handle = NativeLibrary.Load(Path.GetFullPath(library));
        try
        {
            var invoke = Marshal.GetDelegateForFunctionPointer<RecordTrace>(NativeLibrary.GetExport(handle, "RecordTrace"));
            var collector = new NativeCollector();
            OutputCallback callback = collector.OnOutput;
            using var timer = new Timer(_ => collector.Stop(), null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
            var bytes = Encoding.UTF8.GetBytes(command);
            var result = invoke(bytes, (nuint)bytes.Length, callback);
            GC.KeepAlive(callback);
            Console.WriteLine($"Native result={result}; callbackError={Volatile.Read(ref collector._error)}.");
            return result == 0 && Volatile.Read(ref collector._error) != 0 ? 1 : result;
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }

    internal void Stop() => Interlocked.Exchange(ref _stop, 1);

    internal int OnOutput(uint type, IntPtr data, nuint length)
    {
        // Never unwind a managed exception through the native callback boundary.
        try
        {
            if (length > int.MaxValue || (length != 0 && data == IntPtr.Zero))
            {
                Interlocked.Exchange(ref _error, 1);
                Stop();
            }
            else
            {
                var text = length == 0 ? null : Marshal.PtrToStringUTF8(data, (int)length);
                if (type == 2)
                {
                    Interlocked.Exchange(ref _error, 1);
                    Stop();
                    Console.Error.WriteLine(text);
                }
                else if (!string.IsNullOrEmpty(text))
                {
                    Console.WriteLine(text);
                }
            }
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref _error, 1);
            Stop();
        }

        return Volatile.Read(ref _stop);
    }
}
