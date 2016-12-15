namespace BroadcastOverTcp
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Security.Cryptography.X509Certificates;
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
                Console.WriteLine(@"    broadcastOverTcp -f ""\path\to\file.txt"" -p 80 [-a ""192.168.1.1"" | ");
                Console.WriteLine(@"                                                    -r |");
                Console.WriteLine(@"                                                    -d 10 |");
                Console.WriteLine(@"                                                    -i |");
                Console.WriteLine(@"                                                    -s ""certificate name"" ]");
                Console.WriteLine();
                Console.WriteLine("REQUIRED:");
                Console.WriteLine("    -f [file path]         Path to text file (line separated by CR/LF)");
                Console.WriteLine("    -p [tcp port]          TCP port of broadcast destination");

                Console.WriteLine();
                Console.WriteLine("OPTIONAL:");
                Console.WriteLine("    -a [ip address]        IP address (or host name) of broadcast destination (default=127.0.0.1)");
                Console.WriteLine("    -r                     Start again immediated, when file broadcast is finished");
                Console.WriteLine("    -d [seconds]           Number of seconds to wait between broadcast (default=2)");
                Console.WriteLine("    -i                     Include line breaks (CR/LF) in broadcast");
                Console.WriteLine("    -s [certificate name]  Use SSL certificate for socket encryption");
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

            X509Certificate2 cert = null;
            if (!string.IsNullOrEmpty(utilArgs.SslCertificateName))
                cert = GetCertificate(utilArgs.SslCertificateName);

            using (var connection = new SocketConnection(ipEndPoint, cert))
            {
                connection.Connected += (obj, e) =>
                    {
                        Console.WriteLine(
                            $"[{DateTime.Now:s}] Connected: {((IPEndPoint) e.RemoteEndPoint).Address}:{((IPEndPoint) e.RemoteEndPoint).Port}");
                    };
                connection.Disconnecting += (obj, e) =>
                    {
                        Console.WriteLine($"[{DateTime.Now:s}] Disconnecting...");
                    };
                connection.DataSent += (obj, e) =>
                    {
                        Console.WriteLine(
                            $"[{DateTime.Now:s}] Sent: {Encoding.ASCII.GetString(e.Data).TrimEnd('\r', '\n')} [{e.Data.Length} bytes]");
                    };
                connection.ConnectionError += (obj, e) =>
                    {
                        Console.WriteLine($"[{DateTime.Now:s}] Server unreachable ({e.InnerException.Message}).");
                    };
                connection.SendError += (obj, e) =>
                    {
                        Console.WriteLine($"[{DateTime.Now:s}] Send failed ({e.InnerException.Message}).");
                    };

                var task =
                    Task.Run(
                        () => Broadcast(
                            connection,
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
            }

            Console.WriteLine($"[{DateTime.Now:s}] Goodbye!");
        }
        
        public static void Broadcast(
            SocketConnection connection,
            FileInfo file,
            uint delaySeconds,
            bool repeat,
            bool includeLineBreak,
            CancellationToken cancellationToken,
            Action<Exception> exceptionCallback)
        {
            try
            {
                
                do
                {
                    while (!connection.Connect())
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

                                    while (!connection.Connect())
                                        Thread.Sleep(500);

                                    connection.Send(data);
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

                connection.Disconnect();
            }
            catch (Exception ex)
            {
                exceptionCallback(ex);
            }
        }

        private static X509Certificate2 GetCertificate(string name)
        {
            if(string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.ReadOnly);

                var collection = store.Certificates;
                collection = collection.Find(X509FindType.FindByTimeValid, DateTime.Now, false);
                collection = collection.Find(X509FindType.FindBySubjectDistinguishedName, name, false);

                if (collection.Count > 0)
                    return collection[0];
                
                throw new Exception($"Certificate '{name}' was not found in the certificate store (LocalMachine\\Root).");
            }
        }

        
    }
}