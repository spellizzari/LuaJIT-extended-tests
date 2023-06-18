//#define USE_ORIG
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace tests
{
    public class Tests
    {
        // Path to the LuaJIT executable to test.
#if USE_ORIG
        private const string LuaJitDirPath = "bin\\x64\\Orig";
#elif DEBUG
        private const string LuaJitDirPath = "bin\\x64\\Debug";
#else
        private const string LuaJitDirPath = "bin\\x64\\Release";
#endif

        // Additional flags passed to test.lua to enumerate and execute tests.
        private const string RunnerFlags = "+dse +fold +fwd +fuse +loop +sink +slow";

        // Paths to a few directories.
        private static string SolutionDirPath => Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..\\..\\..\\..\\"));
        private static string TestsDirPath => Path.Combine(SolutionDirPath, "tests");
        private static string LuaJitExePath => Path.Combine(SolutionDirPath, LuaJitDirPath, "luajit.exe");

        [TestCaseSource(nameof(EnumerateTests))]
        public void RunTest(string testFilePath, TestOptions options)
        {
            var luaJitExePath = LuaJitExePath;

            Console.WriteLine();

            // Handle overflow option.
            if ((options & TestOptions.WithOverflow) != 0)
            {
                string tempFilePath = Path.GetTempFileName() + ".lua";
                using var writer = File.CreateText(tempFilePath);
                writer.Write("local __overflow={");
                for (int i = 0; i < (1 << 17); i++)
                    writer.Write("{},");
                writer.Write("};");
                writer.Write(File.ReadAllText(testFilePath));
                testFilePath = tempFilePath;
            }

            int exitCode = RunLuaJIT(
                luaJitExePath,
                $"\"{Path.Combine(TestsDirPath, "test", "test.lua")}\" {RunnerFlags} \"{testFilePath}\"",
                Path.Combine(TestsDirPath, "test"));

            if (exitCode != 0)
                Assert.Fail("Test failed.");
        }

        // Enumerates test cases using test.lua.
        private static IEnumerable<TestCaseData> EnumerateTests()
        {
            // Run test.lua to discover runnable tests.
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = LuaJitExePath,
                Arguments = $"\"{Path.Combine(TestsDirPath, "test", "test.lua")}\" {RunnerFlags} --list",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            })!;

            using var reportWriter = File.CreateText(Path.Combine(TestsDirPath, "discovery.txt"));

            for (; ; )
            {
                // Parse output.
                var line = process.StandardOutput.ReadLine();
                if (line is null)
                    break;
                reportWriter.WriteLine(line);

                // If test file is skipped, extract reason.
                var path = line;
                var skipped = path.StartsWith("-");
                var skipReason = string.Empty;
                if (skipped)
                {
                    var sep = line.IndexOf(' ');
                    path = line[1..sep];
                    skipReason = line[(sep + 1)..];
                }

                // Return test case data.
                yield return MakeTestCaseData(path, TestOptions.None, skipped, skipReason);
                yield return MakeTestCaseData(path, TestOptions.WithOverflow, skipped, skipReason);
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                reportWriter.WriteLine(error);
                throw new InvalidOperationException(error);
            }

            static TestCaseData MakeTestCaseData(string testFilePath, TestOptions opts, bool skipped, string skipReason)
            {
                var relPath = Path.GetRelativePath(TestsDirPath, testFilePath);

                var testName = new StringBuilder();
                if (opts != TestOptions.None)
                    testName.Append(opts.ToString() + ".");
                testName.Append(Path.GetFileNameWithoutExtension(relPath));

                return new TestCaseData(Path.GetFullPath(testFilePath), opts)
                {
                    RunState = skipped ? RunState.Ignored : RunState.Runnable,
                }
                .SetName(testName.ToString())
                .SetCategory(opts != TestOptions.None ? opts.ToString() : Path.GetDirectoryName(relPath)!)
                .SetProperty("_CodeFilePath", testFilePath)
                .SetProperty(PropertyNames.SkipReason, skipReason);
            }
        }

        private static int RunLuaJIT(string luaJitExePath, string arguments, string workingDirectory)
        {
            /*return
                Debugger.IsAttached
                ? RunLuaJITInProcess(luaJitExePath, arguments, workingDirectory)
                : RunLuaJITOutOfProcess(luaJitExePath, arguments, workingDirectory);
            /*/
            return RunLuaJITOutOfProcess(luaJitExePath, arguments, workingDirectory);
            //*/
        }

        private static int RunLuaJITInProcess(string luaJitExePath, string arguments, string workingDirectory)
        {
            var moduleHandle = LoadLibrary(luaJitExePath);
            Assert.That(moduleHandle, Is.Not.EqualTo(IntPtr.Zero));
            try
            {
                var mainPtr = GetProcAddress(moduleHandle, "main");
                Assert.That(mainPtr, Is.Not.EqualTo(IntPtr.Zero));

                var argumentsList = new List<string> { luaJitExePath };
                var cmdlineArgsPtr = CommandLineToArgvW(arguments, out var cmdlineArgsCount);
                try
                {
                    for (int i = 0; i < cmdlineArgsCount; i++)
                    {
                        var arg = Marshal.PtrToStringUni(Marshal.ReadIntPtr(cmdlineArgsPtr, i * IntPtr.Size));
                        Assert.That(arg, Is.Not.Null);
                        argumentsList.Add(arg);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(cmdlineArgsPtr);
                }

                var prevCD = Environment.CurrentDirectory;
                Environment.CurrentDirectory = workingDirectory;
                try
                {
                    var main = Marshal.GetDelegateForFunctionPointer<MainHandler>(mainPtr);
                    int retcode = main(argumentsList.Count, argumentsList.ToArray());
                    return retcode;
                }
                finally
                {
                    Environment.CurrentDirectory = prevCD;
                }
            }
            finally
            {
                FreeLibrary(moduleHandle);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int MainHandler(int argc, [In, MarshalAs(UnmanagedType.LPArray)] string[] argv);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr LoadLibrary(string lpFileName);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);
        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

        private static int RunLuaJITOutOfProcess(string luaJitExePath, string arguments, string workingDirectory)
        {
            Console.WriteLine($"Executing \"{luaJitExePath}\" {arguments}");
            Console.WriteLine($"Working Directory: \"{workingDirectory}\"");
            var luaJitExePathLinePrefix = luaJitExePath + ": ";

            // Filters some lines to make them shorter.
            string ProcessOutputLine(string s)
            {
                if (s.StartsWith(luaJitExePathLinePrefix, StringComparison.OrdinalIgnoreCase))
                    s = s[luaJitExePathLinePrefix.Length..];
                return s;
            }

            // Forwards stdout/stderr to console.
            async Task ConsumeStreamAsync(StreamReader reader)
            {
                for (; ; )
                {
                    var line = await reader.ReadLineAsync();
                    if (line is null)
                        break;
                    Console.WriteLine(ProcessOutputLine(line));
                }
            }

            // Start LuaJIT.exe running test.lua with the current test.
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = luaJitExePath,
                Arguments = arguments,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            })!;

            // Redirect output to console.
            var stderrTask = ConsumeStreamAsync(process.StandardError);
            var stdoutTask = ConsumeStreamAsync(process.StandardOutput);

            // Wait for end of execution.
            process.WaitForExit();
            stderrTask.Wait();
            stdoutTask.Wait();

            return process.ExitCode;
        }
    }

    [Flags]
    public enum TestOptions
    {
        None = 0,
        WithOverflow = 1,
    }
}