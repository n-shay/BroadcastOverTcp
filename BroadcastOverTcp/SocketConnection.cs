namespace BroadcastOverTcp
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Security.Authentication;
    using System.Security.Cryptography.X509Certificates;

    public class SocketConnection : IDisposable
    {
        private readonly EndPoint endPoint;
        private readonly X509Certificate2 certificate;
        private Socket innerSocket;
        private Stream innerStream;

        public SocketConnection(EndPoint endPoint)
            : this(endPoint, null)
        {
        }

        public SocketConnection(EndPoint endPoint, X509Certificate2 certificate)
        {
            if (endPoint == null)
                throw new ArgumentNullException(nameof(endPoint));

            this.endPoint = endPoint;
            this.certificate = certificate;
        }

        public event EventHandler Disconnecting;

        public event EventHandler<ConnectionEventArgs> Connected;

        public event EventHandler<MessageEventArgs> DataSent;

        public event EventHandler<ExceptionEventArgs> ConnectionError;

        public event EventHandler<ExceptionEventArgs> SendError;

        public bool Connect()
        {
            try
            {
                if (this.innerSocket != null)
                {
                    if (this.IsConnected())
                        return true;

                    this.innerSocket.Close();

                    this.innerSocket = null;
                }

                if (this.innerSocket == null)
                    this.innerSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);

                this.innerSocket.Connect(this.endPoint);

                this.Connected?.Invoke(this, new ConnectionEventArgs(this.innerSocket.RemoteEndPoint));

                if (this.innerStream != null)
                {
                    this.innerStream.Dispose();
                    this.innerStream = null;
                }

                this.innerStream = new NetworkStream(this.innerSocket, false);
                if (this.certificate != null)
                {
                    SslStream sslStream = null;
                    try
                    {
                        sslStream = new SslStream(this.innerStream, true);
                        sslStream.AuthenticateAsServer(this.certificate, false, SslProtocols.Default, true);
                    }
                    catch (AuthenticationException ex)
                    {
                        sslStream?.Dispose();

                        throw new Exception($"Failed to authenticate certificate ({ex.Message})", ex);
                    }

                    this.innerStream = sslStream;
                }

                return true;
            }
            catch (Exception ex)
            {
                this.ConnectionError?.Invoke(this, new ExceptionEventArgs(ex));
            }

            return false;
        }

        public bool IsConnected()
        {
            return this.innerSocket != null &&
                   !((this.innerSocket.Poll(1000, SelectMode.SelectRead) && (this.innerSocket.Available == 0)) ||
                     !this.innerSocket.Connected);
        }

        public void Disconnect()
        {
            if (this.IsConnected())
            {
                this.Disconnecting?.Invoke(this, EventArgs.Empty);

                this.innerSocket.Disconnect(false);
            }

            this.innerSocket = null;

            if (this.innerStream != null)
            {
                this.innerStream.Dispose();
                this.innerStream = null;
            }
        }

        public void Send(byte[] buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            try
            {
                if (this.IsConnected())
                {
                    this.innerStream.Write(buffer, 0, buffer.Length);

                    this.DataSent?.Invoke(this, new MessageEventArgs(this.innerSocket.RemoteEndPoint, buffer));
                }
            }
            catch (Exception ex)
            {
                this.SendError?.Invoke(this, new ExceptionEventArgs(ex));
            }
        }

        public void Dispose()
        {
            this.innerStream?.Dispose();
            this.innerSocket?.Close();
        }
    }
}