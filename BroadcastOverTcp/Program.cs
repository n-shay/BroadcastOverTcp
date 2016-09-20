namespace BroadcastOverTcp
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    internal class Program
    {
        private static void Main(string[] args)
        {
            var utilArgs = new UtilityArguments(args);

            // present help
            if (utilArgs.Port == null ||
                utilArgs.FilePath == null ||
                utilArgs.Contains("help") ||
                utilArgs.Contains("h"))
            {
                Console.WriteLine("Broadcast a text file over TCP, line by line with delay.");
                Console.WriteLine();
                Console.WriteLine("USAGE:");
                Console.WriteLine("    broadcastOverTcp -f [file path] -p [tcp port] [-a [ip address] | ");
                Console.WriteLine("                                                   -r |");
                Console.WriteLine("                                                   -d [seconds] ]");
                Console.WriteLine();
                Console.WriteLine("REQUIRED:");
                Console.WriteLine("    -f [file path]        Path to text file (line separated by CR/LF)");
                Console.WriteLine("    -p [tcp port]         TCP port of broadcast destination");

                Console.WriteLine();
                Console.WriteLine("OPTIONAL:");
                Console.WriteLine("    -a [ip address]       IP address (or host name) of broadcast destination (default=127.0.0.1)");
                Console.WriteLine("    -r                    Start again immediated, when file broadcast is finished");
                Console.WriteLine("    -d [seconds]          Number of seconds to wait between broadcast (default=2)");
                Console.WriteLine("    -i                    Include line breaks (CR/LF) in broadcast");
                Console.WriteLine();
                Console.WriteLine("Press any key to exit...");

                Console.ReadKey();
                return;
            }

            // validate port
            if (utilArgs.Port == 0)
            {
                Console.WriteLine($"Port is not a valid (0 < port <= {ushort.MaxValue})");
                return;
            }

            // validate ip address
            var ip = IPAddress.Loopback;
            if (utilArgs.IpAddress != null)
            {
                try
                {
                    ip = Dns.GetHostAddresses(utilArgs.IpAddress).FirstOrDefault();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    Console.WriteLine();
                }

                if (ip == null)
                {
                    Console.WriteLine("IP address cannot be resolved.");
                    return;
                }
            }

            // validate file path
            var file = new FileInfo(utilArgs.FilePath);
            if (!file.Exists)
            {
                Console.WriteLine("File cannot be found.");
                return;
            }
            if (file.Length == 0)
            {
                Console.WriteLine("File is empty.");
                return;
            }

            // validate delay
            var delay = utilArgs.Delay ?? 2;


            Console.WriteLine("Press 'Q' at any time to quit...");
            Console.WriteLine();

            var ipEndPoint = new IPEndPoint(ip, utilArgs.Port.Value);

            var cancellationTokenSource = new CancellationTokenSource();

            var task =
                Task.Run(
                    () => Broadcast(
                        ipEndPoint,
                        file,
                        delay,
                        utilArgs.Repeat,
                        utilArgs.IncludeLineBreak,
                        cancellationTokenSource.Token,
                        ex =>
                            {
                                Console.WriteLine($"Error:      {ex.Message}");
                                Console.WriteLine($"Details:    {ex.ToString()}");

                                Thread.Sleep(5000);
                                Environment.Exit(0);
                            }));

            // Listen for a key response.
            ConsoleKeyInfo keyInfo;
            do
            {
                keyInfo = Console.ReadKey(true);

                try
                {
                    //switch (keyInfo.Key)
                    //{
                    //}
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error executing {keyInfo.Key}. Exception: {ex}");
                }
            } while (keyInfo.Key != ConsoleKey.Q);

            if (!task.IsCompleted)
            {
                Console.WriteLine($"[{DateTime.Now:s}] Stopping...");
                cancellationTokenSource.Cancel();

                task.Wait(TimeSpan.FromSeconds(8));
            }

            Console.WriteLine($"[{DateTime.Now:s}] Goodbye!");
        }

        private static bool IsConnected(Socket socket)
        {
            return !((socket.Poll(1000, SelectMode.SelectRead) && (socket.Available == 0)) || !socket.Connected);
        }

        private static bool TryConnect(ref Socket socket, IPEndPoint endPoint)
        {
            try
            {
                if(socket != null)
                {
                    if (IsConnected(socket))
                        return true;
                    
                    socket.Close();

                    socket = null;
                }

                if (socket == null)
                    socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

                socket.Connect(endPoint);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:s}] Server unreachable ({ex.Message}).");
            }

            return false;
        }

        public static void Broadcast(
            IPEndPoint endPoint,
            FileInfo file,
            uint delaySeconds,
            bool repeat,
            bool includeLineBreak,
            CancellationToken cancellationToken,
            Action<Exception> exceptionCallback)
        {
            Socket socket = null;
            try
            {
                do
                {
                    while (!TryConnect(ref socket, endPoint))
                        Thread.Sleep(500);

                    Console.WriteLine($"[{DateTime.Now:s}] Starting file broadcast...");

                    using (var fileStream = file.OpenRead())
                    {
                        using (var reader = new StreamReader(fileStream, Encoding.UTF8))
                        {
                            while (!reader.EndOfStream)
                            {
                                var line = reader.ReadLine();
                                if (line != null)
                                {
                                    var lineToSend = line + (includeLineBreak ? Environment.NewLine : null);

                                    var data = Encoding.ASCII.GetBytes(lineToSend);

                                    while (!TryConnect(ref socket, endPoint))
                                        Thread.Sleep(500);

                                    var count = socket.Send(data);

                                    Console.WriteLine($"[{DateTime.Now:s}] Sent: {line} [{count} bytes]");
                                }

                                if (cancellationToken.IsCancellationRequested)
                                    break;

                                if (delaySeconds > 0)
                                    Thread.Sleep(TimeSpan.FromSeconds(delaySeconds));
                            }
                        }
                    }

                    Console.WriteLine($"[{DateTime.Now:s}] Finished file broadcast!");

                    if (cancellationToken.IsCancellationRequested)
                        break;

                } while (repeat);

                if (!IsConnected(socket))
                {
                    Console.WriteLine($"[{DateTime.Now:s}] Disconnecting...");
                    socket.Disconnect(false);
                }

                socket.Close();
            }
            catch (Exception ex)
            {
                exceptionCallback(ex);
            }
        }
    }
}