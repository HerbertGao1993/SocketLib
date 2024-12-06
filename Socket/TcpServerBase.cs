using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;



namespace SocketLib
{
    public class TcpServerBase : IDisposable
    {
        public delegate void OnExpectedDataEvent(string currentmessage, ClientConnection connection);
        public event OnExpectedDataEvent OnExpectedData;
        public delegate void logmsgEvent(string message, ClientConnection connection);
        public event logmsgEvent logmsg;
        public delegate void OnConnectEvent(ClientConnection connection);
        public event OnConnectEvent OnConnect;
        public delegate void DisplayEvent(ClientConnection connection,Byte[] Bytes);
        public event DisplayEvent Display;
        protected bool _disposed = false;
        public bool Disposed
        {
            get
            {
                return _disposed;
            }
        }
        protected bool _listening = false;
        public bool Listening
        {
            get
            {
                return _listening;
            }
        }
        protected Socket _Socket = null;
        public List<ClientConnection> _activeConnections = new List<ClientConnection>();
        public object ConnectionListLock { get; }
        protected byte[] _LocalIPArray { get;}
        protected string _localip { get; set; }
        public string LocalIP
        {
            get
            {
                return _localip;
            }
        }
        protected int _localport { get; set; }
        public int LocalPort
        {
            get
            {
                return _localport;
            }
        }
        public TcpServerBase(string ip, int port)
        {
            string[] ips = ip.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            this._LocalIPArray = new byte[4];
            for (int i = 0; i < 4; ++i)
            {
                this._LocalIPArray[i] = Convert.ToByte(ips[i]);
            }
            this._localip = ip;
            this._localport = port;
            ConnectionListLock = new object();
        }
        ~TcpServerBase()
        {
            Dispose(false);
        }
        private byte[] GetKeepAliveData()
        {
            uint dummy = 0;
            byte[] inOptionValues = new byte[Marshal.SizeOf(dummy) * 3];
            BitConverter.GetBytes((uint)1).CopyTo(inOptionValues, 0);
            BitConverter.GetBytes((uint)3000).CopyTo(inOptionValues, Marshal.SizeOf(dummy));//keep-alive间隔
            BitConverter.GetBytes((uint)500).CopyTo(inOptionValues, Marshal.SizeOf(dummy) * 2);// 尝试间隔
            return inOptionValues;
        }
        public void Start()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("Client is disposed");
            }
            if (!_listening)
            {
                try
                {
                    if (_LocalIPArray.Length == 4 && _localport > 0 && _localport < 65536)
                    {
                        _Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        _Socket.IOControl(IOControlCode.KeepAliveValues, GetKeepAliveData(), null);
                        _Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                        _Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, true);
                        _Socket.SendTimeout = 1000;
                        EndPoint endpoint = new IPEndPoint(new IPAddress(_LocalIPArray), _localport);
                        _Socket.Bind(endpoint);
                        _Socket.Listen(1000);
                        if (_activeConnections != null)
                        {
                            lock (ConnectionListLock)
                            {
                                foreach (ClientConnection connection in _activeConnections)
                                {
                                    if (connection != null)
                                    {
                                        connection.Dispose();
                                    }
                                }
                                _activeConnections.Clear();
                            }
                        }
                        else
                        {
                            _activeConnections = new List<ClientConnection>();
                        }
                        _listening = true;
                        _Socket.BeginAccept(HandleAcceptTcpClient, _Socket);//监测到连接后返回，调用回调函数
                    }
                    else
                    {
                        kill();
                    }
                }
                catch (Exception ex)
                {
                    kill();
                    //Thread.Sleep(1000);
                }
            }
            if (_disposed)
            {
                Dispose();
            }
        }
        protected void kill()
        {
            if (_Socket != null)
            {
                _Socket.Close();
                _Socket.Dispose();
            }
            if (_activeConnections != null)
            {
                lock (ConnectionListLock)
                {
                    foreach (ClientConnection connection in _activeConnections)
                    {
                        if (connection != null)
                        {
                            connection.Dispose();
                        }
                    }
                    _activeConnections.Clear();
                }
            }
            _listening = false;
        }
        /*
        public void Stop()
        {
            _listening = false;
            _Socket.Close();
            _Socket.Dispose();
        }
        */
        public void Close()
        {
            Dispose();
        }
        private void HandleAcceptTcpClient(IAsyncResult result)
        {
            if (_listening && _Socket != null)
            {
                Socket socket = null;
                try
                {
                    socket = _Socket.EndAccept(result);
                    if (socket != null)
                    {
                        socket.IOControl(IOControlCode.KeepAliveValues, GetKeepAliveData(), null);
                        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, true);
                        socket.SendTimeout = 1000;
                        ClientConnection connection = new ClientConnection(socket, this);
                        if (OnConnect != null)
                        {
                            OnConnect(connection);
                        }
                        int count = 0;
                        lock (ConnectionListLock)
                        {
                            if (_activeConnections != null)
                            {
                                _activeConnections.Add(connection);
                                count = _activeConnections.Count;
                            }
                        }
                        logmsgHandle(connection.HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + connection.LocalIP + ":" + connection.LocalPort + "<->" + connection.RemoteIP + ":" + connection.RemotePort + "] connection established! Server Total Active Connections: " + count, connection);
                        new Thread(connection.CheckAlive).Start();
                        new Thread(connection.Receive).Start();
                    }
                }
                catch (Exception ex) { }
                try
                {
                    _Socket.BeginAccept(HandleAcceptTcpClient, _Socket);
                }
                catch (Exception ex) 
                {
                    kill();
                } 
            }
            if (_disposed)
            {
                Dispose();
            }
        }
        /*
        virtual protected void OnConnect(ClientConnection connection)
        {
            connection.ExpectFramed();
        }
        */
        public void DisplayHandle(ClientConnection connection, Byte[] Bytes)
        {
            if (Display != null)
            {
                Display(connection, Bytes);
            }
        }
        public void logmsgHandle(string currentmessage, ClientConnection connection)
        {
            if (logmsg != null)
            {
                logmsg(currentmessage, connection);
            }
        }
        public void OnExpectedDataHandle(string currentmessage, ClientConnection connection)
        {
            if (OnExpectedData != null)
            {
                OnExpectedData(currentmessage, connection);
            }
        }
        public void Remove(string remoteip, int remoteport)
        {
            lock (ConnectionListLock)
            {
                if (_activeConnections != null)
                {
                    for (int i = 0; i < _activeConnections.Count; i++)
                    {
                        if (_activeConnections[i] != null)
                        {
                            if (_activeConnections[i].RemoteIP == remoteip && _activeConnections[i].RemotePort == remoteport)
                            {
                                _activeConnections[i].Dispose();
                                _activeConnections.RemoveAt(i);
                                break;
                            }
                        }
                    }
                }
            }
        }
        public void Remove(string remoteip)
        {
            lock (ConnectionListLock)
            {
                if (_activeConnections != null)
                {
                    for (int i = 0; i < _activeConnections.Count; i++)
                    {
                        if (_activeConnections[i] != null)
                        {
                            if (_activeConnections[i].RemoteIP == remoteip)
                            {
                                _activeConnections[i].Dispose();
                                _activeConnections.RemoveAt(i);
                                i--;
                            }
                        }
                    }
                }
            }
        }
        public void Send(string message,string remoteip, int remoteport)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("Client is disposed");
            }
            lock (ConnectionListLock)
            {
                if (_activeConnections != null)
                {
                    for (int i = 0; i < _activeConnections.Count; i++)
                    {
                        try
                        {
                            if (_activeConnections[i] != null)
                            {
                                if (_activeConnections[i].RemoteIP == remoteip && _activeConnections[i].RemotePort == remoteport)
                                {
                                    _activeConnections[i].Send(Encoding.UTF8.GetBytes(message));
                                    logmsgHandle(_activeConnections[i].HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + _activeConnections[i].LocalIP + ":" + _activeConnections[i].LocalPort + "<->" + _activeConnections[i].RemoteIP + ":" + _activeConnections[i].RemotePort + "] Send Message: " + message, _activeConnections[i]);
                                    break;
                                }
                            }
                        }
                        catch (Exception ex) { }
                    }
                }
            }
            if (_disposed)
            {
                Dispose();
            }
        }
        public void Send(string message, string remoteip)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("Client is disposed");
            }
            lock (ConnectionListLock)
            {
                if (_activeConnections != null)
                {
                    for (int i = 0; i < _activeConnections.Count; i++)
                    {
                        try
                        {
                            if (_activeConnections[i] != null)
                            {
                                if (_activeConnections[i].RemoteIP == remoteip)
                                {
                                    _activeConnections[i].Send(Encoding.UTF8.GetBytes(message));
                                    logmsgHandle(_activeConnections[i].HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + _activeConnections[i].LocalIP + ":" + _activeConnections[i].LocalPort + "<->" + _activeConnections[i].RemoteIP + ":" + _activeConnections[i].RemotePort + "] Send Message: " + message, _activeConnections[i]);
                                }
                            }
                        }
                        catch (Exception ex) { }
                    }
                }
            }
            if (_disposed)
            {
                Dispose();
            }
        }
        public bool CheckStatus(string remoteip, int remoteport)
        {
            bool status = false;
            lock (ConnectionListLock)
            {
                if (_activeConnections != null)
                {
                    for (int i = 0; i < _activeConnections.Count; i++)
                    {
                        try
                        {
                            if (_activeConnections[i] != null)
                            {
                                if (_activeConnections[i].RemoteIP == remoteip && _activeConnections[i].RemotePort == remoteport)
                                {
                                    status = _activeConnections[i].isalive;
                                    break;
                                }
                            }
                        }
                        catch (Exception ex) { }
                    }
                }
                return status;
            }
        }
        public bool CheckStatus(string remoteip)
        {
            bool status = false;
            lock (ConnectionListLock)
            {
                if (_activeConnections != null)
                {
                    for (int i = 0; i < _activeConnections.Count; i++)
                    {
                        try
                        {
                            if (_activeConnections[i] != null)
                            {
                                if (_activeConnections[i].RemoteIP == remoteip)
                                {
                                    if (_activeConnections[i].isalive)
                                    {
                                        status = true;
                                    }
                                }
                            }
                        }
                        catch (Exception ex) { }
                    }
                }
                return status;
            }
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            /*if (_disposed)
            {
                return;
            }*/
            if (disposing)
            {
                
            }
            if (_Socket != null)
            {
                _Socket.Close();
                _Socket.Dispose();
            }
            if (_activeConnections != null)
            {
                lock (ConnectionListLock)
                {
                    foreach (ClientConnection connection in _activeConnections)
                    {
                        if (connection != null)
                        {
                            connection.Dispose();
                        }
                    }
                    _activeConnections.Clear();
                }
            }
            _disposed = true;
            _listening = false;
        }
    }
}
