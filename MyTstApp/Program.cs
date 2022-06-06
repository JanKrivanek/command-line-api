using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.CommandLine.Tests.Invocation;
using System.CommandLine.Tests.Utility;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;
using Process = System.Diagnostics.Process;

namespace MyTstApp
{
    internal class Program
    {
        public static async Task<int> Main(string[] args)
        {
            ////await new CancelOnProcessTerminationTests().CancelOnProcessTermination_timeout_on_cancel_processing(null);
            //await new Tests().CancelOnProcessTermination_timeout_on_cancel_processing(null);
            ////await new Tests().CancelOnProcessTermination_timeout_on_cancel_processing(100);

            //return 5;

            RootCommand rootCommand = new RootCommand();
            rootCommand.AddCommand(New3CommandFactory.Create());

            rootCommand.SetHandler(() =>
            {
                Console.WriteLine("Hello world!");
            });

            int res;
            try
            {
                res = await CreateParser(rootCommand).Parse(args).InvokeAsync();
            }
            catch (Exception ex) when(ex is OperationCanceledException || ex is TaskCanceledException)
            {
                Console.WriteLine("MUHEHEHEHE " + ex + " ---------------------");
                res = 123;
            }
             

            return res;
        }

        internal static Parser CreateParser(Command command)
        {
            var builder = new CommandLineBuilder(command)
                .UseParseErrorReporting() 
                .EnablePosixBundling(false)
                .CancelOnProcessTermination(TimeSpan.FromSeconds(1));

            return builder.Build();
        }
    }

    internal static class New3CommandFactory
    {
        internal static Command Create()
        {
            Command newCommand = new MyCommand();
            return newCommand;
        }
    }

    public class MyCommand: Command, ICommandHandler
    {
        internal MyCommand()
            : base("my", "my command")
        {
            this.TreatUnmatchedTokensAsErrors = true;
            this.AddGlobalOption(DebugAttachOption);

            this.Handler = this;
        }

        internal static Option<bool> DebugAttachOption { get; } = new("--debug:attach")
        {
            Description = "attaching debugger option dklskdf",
            IsHidden = true
        };

        public int Invoke(InvocationContext context) => InvokeAsync(context).GetAwaiter().GetResult();


        public async Task<int> InvokeAsync(InvocationContext context)
        {
            Console.WriteLine("Woo haa");
            //Console.ReadKey();

            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), /*context.GetCancellationToken()*/default).ConfigureAwait(false);
                if (context.GetCancellationToken().IsCancellationRequested)
                {
                    Console.WriteLine("Cancel requested - done");
                    //return 5;
                }
                else
                {
                    Console.WriteLine(i);
                }
            }

            Console.WriteLine("Done");

            return 2;
        }
    }

    public class Tests
    {
        private const int SIGINT = 2;
        private const int SIGTERM = 15;

        public async Task CancelOnProcessTermination_timeout_on_cancel_processing(int? timeOutMs)
        {
            Console.WriteLine("bbb");

            TimeSpan? timeOut = timeOutMs.HasValue ? TimeSpan.FromMilliseconds(timeOutMs.Value) : null;

            const string ChildProcessWaiting = "Waiting for the command to be cancelled";
            const int CancelledExitCode = 42;
            const int ForceTerminationCode = 130;

            Func<string[], Task<int>> childProgram = (string[] args) =>
            {
                var command = new Command("the-command");

                command.SetHandler(async context =>
                {
                    var cancellationToken = context.GetCancellationToken();

                    try
                    {
                        context.Console.WriteLine(ChildProcessWaiting);
                        await Task.Delay(int.MaxValue, cancellationToken);
                        context.ExitCode = 1;
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine("Operation cancelled catched - test");

                        // For Process.Exit handling the event must remain blocked as long as the
                        // command is executed.
                        // We are currently blocking that event because CancellationTokenSource.Cancel
                        // is called from the event handler.
                        // We'll do an async Task.Delay now. This means the Cancel call will return
                        // and we're no longer actively blocking the event.
                        // The event handler is responsible to continue blocking until the command
                        // has finished executing. If it doesn't we won't get the CancelledExitCode.
                        await Task.Delay(TimeSpan.FromMilliseconds(5000));

                        Console.WriteLine("After delay in test");

                        context.ExitCode = CancelledExitCode;
                    }

                });

                return new CommandLineBuilder(new RootCommand
                       {
                           command
                       })
                       .CancelOnProcessTermination(timeOut)
                       .Build()
                       .InvokeAsync("the-command");
            };

            using RemoteExecution program = RemoteExecutor.Execute(childProgram, psi: new ProcessStartInfo() /*{ RedirectStandardOutput = true }*/);

            Process process = program.Process;

            // Wait for the child to be in the command handler.
            //string childState = await process.StandardOutput.ReadLineAsync();
            //childState.Should().Be(ChildProcessWaiting);

            // Request termination
            kill(process.Id, SIGTERM).Should().Be(0);

            // Verify the process terminates timely
            bool processExited = process.WaitForExit(10000);
            if (!processExited)
            {
                process.Kill();
                process.WaitForExit();
            }
            processExited.Should().Be(true);

            // Verify the process exit code
            process.ExitCode.Should().Be(timeOutMs.HasValue ? ForceTerminationCode : CancelledExitCode);
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int kill(int pid, int sig);
    }
}