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

    public class TcpClientBase : IDisposable
    {
        public string HandlerName = "UnexpectedServer: ";
        public delegate void OnExpectedDataEvent(TcpClientBase client, string inputstring);
        public event OnExpectedDataEvent OnExpectedData;
        public delegate void logmsgEvent(TcpClientBase client, string message);
        public event logmsgEvent logmsg;
        public delegate void OnConnectEvent(TcpClientBase client);
        public event OnConnectEvent OnConnect;
        public delegate void DisplayEvent(Byte[] Bytes,int bytesread);
        public event DisplayEvent Display;
        private object BufferLock { get; }
        protected Socket _Socket = null;
        protected bool _disposed = false;
        public bool Disposed
        {
            get
            {
                return _disposed;
            }
        }
        protected string _localip{ get; set;}
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
        protected string _remoteip{ get; set; }
        public string RemoteIP
        {
            get
            {
                return _remoteip;
            }
        }
        protected int _remoteport { get; set; }
        public int RemotePort
        {
            get
            {
                return _remoteport;
            }
        }
        private string RecvBuffer = "";
        protected bool _isAlive = false;
        public bool isalive
        {
            get { return _isAlive; }
        }
        protected string startchar = "[";
        protected string endchar = "]";
        protected int messageMaxLength = 1024;
        
        public TcpClientBase(string localIP, int localPort, string remoteIP, int remotePort)
        {
            string[] localips = localIP.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            string[] remoteips = remoteIP.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            if (localips.Length == 4 && remoteips.Length == 4)
            {
                this._localip = localIP;
                this._remoteip = remoteIP;
                this._localport = localPort;
                this._remoteport = remotePort;
                BufferLock = new object();
            }
        }
        public TcpClientBase(string remoteIP, int remotePort)
        {
            string[] remoteips = remoteIP.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            if (remoteips.Length == 4)
            {
                this._localip = "";
                this._remoteip = remoteIP;
                this._localport = 0;
                this._remoteport = remotePort;
                BufferLock = new object();
            }
        }
        public void ExpectFramed(string start = "[", string end = "]", int length = 1024)
        {
            if (start != "")
            {
                startchar = start;
            }
            if (end != "")
            {
                endchar = end;
            }
            if (length > 0)
            {
                messageMaxLength = length;
            }
        }
        /*
        virtual protected void logmsg(string msg)
        {
            Console.WriteLine(msg);
        }
        */
        /*
        virtual protected void OnConnect()
        {
            ExpectFramed();
        }
        */
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
            if (!_isAlive)
            {
                try
                {
                    _Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    _Socket.IOControl(IOControlCode.KeepAliveValues, GetKeepAliveData(), null);
                    _Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    _Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, true);
                    _Socket.SendTimeout = 1000;
                    string[] localips = _localip.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                    string[] remoteips = _remoteip.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                    if (localips.Length == 4 && _localport > 0 && _localport < 65536)
                    {
                        byte[] localIPArray = new byte[4];
                        for (int i = 0; i < 4; ++i)
                        {
                            localIPArray[i] = Convert.ToByte(localips[i]);
                        }
                        EndPoint endpoint = new IPEndPoint(new IPAddress(localIPArray), _localport);
                        _Socket.Bind(endpoint);
                    }
                    if (remoteips.Length == 4 && _remoteport > 0 && _remoteport < 65536)
                    {
                        byte[] remoteIPArray = new byte[4];
                        for (int i = 0; i < 4; ++i)
                        {
                            remoteIPArray[i] = Convert.ToByte(remoteips[i]);
                        }
                        _Socket.Connect(new IPAddress(remoteIPArray), _remoteport);
                        _localip = (_Socket.LocalEndPoint as IPEndPoint).Address.ToString();
                        _localport = (_Socket.LocalEndPoint as IPEndPoint).Port;
                        _remoteip = (_Socket.RemoteEndPoint as IPEndPoint).Address.ToString();
                        _remoteport = (_Socket.RemoteEndPoint as IPEndPoint).Port;
                        if (OnConnect != null)
                        {
                            OnConnect(this);
                        }
                        if (logmsg != null)
                        {
                            logmsg(this, HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + _localip + ":" + _localport + "<->" + _remoteip + ":" + _remoteport + "] connection established!");
                        }
                        _isAlive = true;
                        new Thread(CheckAlive).Start();
                        new Thread(Receive).Start();
                        //new Thread(HandleMessage).Start();
                    }
                    else
                    {
                        kill();
                        //Dispose();
                        //break;
                    }
                }
                catch (Exception ex)
                {
                    //TcpState s=CheckConnectionStatus();
                    kill();
                    //Thread.Sleep(1000);
                    //Dispose();
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
            _isAlive = false;
        }
        
        private void Stop()
        {
            if (_Socket != null)
            {
                try
                {
                    _Socket.Shutdown(SocketShutdown.Both);
                    _Socket.Disconnect(false);
                }
                catch (Exception ex)
                {

                }
                finally
                {
                    _Socket.Close();
                    _Socket.Dispose();
                }
            }
            _isAlive = false;
        }
        /*
        virtual protected void OnExpectedData(string currentmessage)
        {
            logmsg("[" + DateTime.Now.AddMilliseconds(0).ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + LocalIP + ":" + LocalPort + "<->" + RemoteIP + ":" + RemotePort + "] Expected: " + currentmessage);
            try
            {

            }
            catch (Exception ex) { }
        }*/
        private TcpState CheckConnectionStatus()
        {
            try
            {
                IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();
                TcpConnectionInformation[] tcpConnections = ipProperties.GetActiveTcpConnections();
                foreach (TcpConnectionInformation c in tcpConnections)
                {
                    TcpState stateOfConnection = c.State;
                    if (_Socket != null)
                    {
                        if (c.LocalEndPoint.Equals(_Socket.LocalEndPoint) && c.RemoteEndPoint.Equals(_Socket.RemoteEndPoint))
                        {
                            /*if (stateOfConnection == TcpState.Established)
                            {
                                return true;
                            }
                            else
                            {
                                return false;
                            }*/
                            return stateOfConnection;
                        }
                    }
                }
                return TcpState.Unknown;
            }
            catch
            {
                return TcpState.Unknown;
            }
        }
        private void CheckAlive()
        {
            while (_isAlive && _Socket != null)
            {
                TcpState state = CheckConnectionStatus();
                if (state != TcpState.Established)
                {
                    kill();
                    break;
                }
                /*else if (state == TcpState.CloseWait)
                {
                    Stop();
                }*/
                Thread.Sleep(10);
            }
            kill();
            if (logmsg != null)
            {
                logmsg(this, HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + _localip + ":" + _localport + "<->" + _remoteip + ":" + _remoteport + "] disconnected. Stop CheckAlive thread!");

            }
        }
        private void HandleMessage()
        {
            string currentMessage = "";
            while (RecvBuffer != "")
            {
                lock (BufferLock)
                {
                    try
                    {
                        int startpos = RecvBuffer.IndexOf(startchar);
                        if (startpos < 0)//没有报头
                        {
                            RecvBuffer = RecvBuffer.Remove(0, RecvBuffer.Length);
                            break;
                        }
                        else //有报头
                        {
                            int endpos = RecvBuffer.IndexOf(endchar, startpos);
                            if (endpos < 0)//有报头无报尾
                            {
                                int length = RecvBuffer.Length - startpos - 1;
                                if (length <= messageMaxLength)
                                {
                                    RecvBuffer = RecvBuffer.Remove(0, startpos);
                                    break;
                                }
                                else
                                {
                                    RecvBuffer = RecvBuffer.Remove(0, RecvBuffer.Length);
                                    break;
                                }
                            }
                            else//有报头有报尾
                            {
                                int length = endpos - startpos - 1;
                                if (length <= messageMaxLength && length >= 0)
                                {
                                    currentMessage = RecvBuffer.Substring(startpos + 1, length);
                                    if (logmsg != null)
                                    {
                                        logmsg(this, HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + _localip + ":" + _localport + "<->" + _remoteip + ":" + _remoteport + "] Expected:" + currentMessage);
                                    }
                                    if (OnExpectedData != null)
                                    {
                                        OnExpectedData(this, currentMessage);
                                    }
                                }
                                RecvBuffer = RecvBuffer.Remove(0, endpos + 1);
                                if (RecvBuffer.Length == 0)
                                {
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex) { RecvBuffer = ""; }
                }
            }
        }
        private void Receive()
        {
            DateTime lastrecv = DateTime.Now;
            Int32 bytesread;
            int bufferlen = 8096;
            
            while (_isAlive && _Socket != null)
            {
                try
                {
                    if (_Socket.Available > 0)
                    {
                        Byte[] bytesArray = new Byte[bufferlen];
                        if ((bytesread = _Socket.Receive(bytesArray, 0, bufferlen, SocketFlags.None)) > 0)//readline读不到东西会阻塞，直到读到信息，或者连接断开。连接断开的返回为null
                        {
                            if (Display != null)
                            {
                                Display(bytesArray, bytesread);
                            }
                            string recvData = System.Text.Encoding.UTF8.GetString(bytesArray, 0, bytesread);
                            RecvBuffer += recvData;
                            if (logmsg != null)
                            {
                                logmsg(this, HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + _localip + ":" + _localport + "<->" + _remoteip + ":" + _remoteport + "] Received:" + recvData + " Buffer:" + RecvBuffer + " BufferLength:" + RecvBuffer.Length);
                            }
                            HandleMessage();
                            if (logmsg != null)
                            {
                                logmsg(this, HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + _localip + ":" + _localport + "<->" + _remoteip + ":" + _remoteport + "] After Handle, Buffer:" + RecvBuffer + " BufferLength:" + RecvBuffer.Length);
                            }
                        }
                        else
                        { }
                    }
                }
                catch (Exception ex) { }
                Thread.Sleep(1);
            }
            if (logmsg != null)
            {
                logmsg(this, HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + _localip + ":" + _localport + "<->" + _remoteip + ":" + _remoteport + "] disconnected. Stop Receiving thread!");
            }
        }
        public void Send(string message)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("Client is disposed");
            }
            try
            {
                if (_isAlive && _Socket != null)
                {
                    Send(Encoding.UTF8.GetBytes(message));
                    if (logmsg != null)
                    {
                        logmsg(this, HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + _localip + ":" + _localport + "<->" + _remoteip + ":" + _remoteport + "] Send Message:" + message);
                    }
                }
            }
            catch (Exception ex)
            {
            }
            if (_disposed)
            {
                Dispose();
            }
            
        }
        protected void Send(Byte[] sendArray)
        {
            _Socket.Send(sendArray, 0, sendArray.Length, SocketFlags.None);
        }
        ~TcpClientBase()
        {
            Dispose(false);
        }
       
        public void Close()
        {
            Dispose();
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
            _isAlive = false;
            _disposed = true;
        }
    }
}

