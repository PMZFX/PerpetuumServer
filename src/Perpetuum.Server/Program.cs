using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Perpetuum.Bootstrapper;

namespace Perpetuum.Server
{
    public static class Program
    {
        static int Main(string[] args)
        {
            if (args.Any(arg => arg == "-h" || arg == "--help"))
            {
                ShowHelp();
                return 0;
            }

            if (!TryParseArguments(args, out string gameRoot, out bool dumpCommands))
            {
                ShowHelp();
                return 2;
            }

            var bootstrapper = new PerpetuumBootstrapper();
            try
            {
                if (dumpCommands)
                {
                    Console.WriteLine("dumping commands to commands.txt");
                    bootstrapper.WriteCommandsToFile("commands.txt");
                    return 0;
                }

                if (!Directory.Exists(gameRoot))
                {
                    Console.WriteLine($"GameRoot folder was not found: {gameRoot}");
                    return 3;
                }

                bootstrapper.Init(gameRoot);

                if (bootstrapper.TryInitUpnp(out bool upnpSuccess) && !upnpSuccess)
                {
                    return 2000;
                }

                int stopRequested = 0;
                void RequestStop()
                {
                    if (Interlocked.Exchange(ref stopRequested, 1) != 0)
                        return;

                    Console.WriteLine();
                    Console.WriteLine("STOPPING HOST IN 4 SECONDS");
                    Console.WriteLine();

                    bootstrapper.Stop(TimeSpan.FromSeconds(4));
                }

                Console.CancelKeyPress += (sender, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    RequestStop();
                };

                using PosixSignalRegistration terminateRegistration = OperatingSystem.IsWindows()
                    ? null
                    : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
                    {
                        context.Cancel = true;
                        RequestStop();
                    });

                bootstrapper.Start();
                bootstrapper.WaitForStop();
                return 0;
            }
            catch (Exception ex)
            {
                DisplayException(ex);
                return 1;
            }
        }

        private static bool TryParseArguments(string[] args, out string gameRoot, out bool dumpCommands)
        {
            gameRoot = null;
            dumpCommands = false;

            foreach (string arg in args)
            {
                switch (arg)
                {
                    case "-dc":
                    case "--dump-commands":
                        dumpCommands = true;
                        break;
                    default:
                        if (arg.StartsWith("-", StringComparison.Ordinal) || gameRoot != null)
                        {
                            Console.WriteLine($"Unknown or duplicate argument: {arg}");
                            return false;
                        }

                        gameRoot = arg;
                        break;
                }
            }

            return dumpCommands || gameRoot != null;
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Usage: Perpetuum.Server [options] <GAMEROOT>");
            Console.WriteLine("  -h,  --help           Show help");
            Console.WriteLine("  -dc, --dump-commands  Write commands.txt and exit");
        }

        private static void DisplayException(Exception ex)
        {
            if (ex is AggregateException aggregateException)
            {
                foreach (var innerException in aggregateException.InnerExceptions)
                {
                    DisplayException(innerException);
                }
                return;
            }

            if (ex.InnerException != null)
            {
                DisplayException(ex.InnerException);
            }

            Console.WriteLine(ex.Message);
        }
    }
}
