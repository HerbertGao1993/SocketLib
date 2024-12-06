using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;

namespace SocketLib
{
    public class ClientConnection : IDisposable
    {
        public string HandlerName = "UnexpectedClient: ";
        private bool _disposed = false;
        public bool Disposed
        {
            get
            {
                return _disposed;
            }
        }
        private object BufferLock { get;}
        private TcpServerBase _Server { get; }
        private Socket _Socket = null;
        private string startchar = "[";
        private string endchar = "]";
        private int messageMaxLength = 1024;
        public string LocalIP { get; }
        public int LocalPort { get; }
        public string RemoteIP { get; }
        public int RemotePort { get; }
        private string RecvBuffer = "";
        private bool _isAlive = true;
        public bool isalive
        {
            get { return _isAlive; }
            //set { _isAlive = value; }
        }
        //public Socket socket { get { return _Socket; } }
        public ClientConnection(Socket socket,TcpServerBase server)
        {
            _Server = server;
            _Socket = socket;
            LocalIP = ((IPEndPoint)_Socket.LocalEndPoint).Address.ToString();
            LocalPort = ((IPEndPoint)_Socket.LocalEndPoint).Port;
            RemoteIP = ((IPEndPoint)_Socket.RemoteEndPoint).Address.ToString();
            RemotePort = ((IPEndPoint)_Socket.RemoteEndPoint).Port;
            BufferLock = new object();
        }
        public void kill()
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
        public void ExpectFramed(string start="[", string end= "]", int length = 1024)
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
        public void HandleMessage()
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
                                    _Server.logmsgHandle(HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + LocalIP + ":" + LocalPort + "<->" + RemoteIP + ":" + RemotePort + "] Expected: " + currentMessage,this);
                                    _Server.OnExpectedDataHandle(currentMessage,this);
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
        public void CheckAlive()
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
            int count = 0;
            lock (_Server.ConnectionListLock)
            {
                if (_Server._activeConnections != null)
                {
                    _Server._activeConnections.Remove(this);
                    count = _Server._activeConnections.Count();
                }
            }
            _Server.logmsgHandle(HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + LocalIP + ":" + LocalPort + "<->" + RemoteIP + ":" + RemotePort + "] disconnected. Stop CheckAlive thread! Server Total Active Connections: " + count, this);
        }
        /*
        public void CheckAlive()
        {
            while (_isAlive && _Socket != null)
            {
                if (!isClientConnected())
                {
                    lock (_Server.ConnectionListLock)
                    {
                        _Server._activeConnections.Remove(this);
                    }
                    kill();
                    break;
                }
                Thread.Sleep(500);
            }
            _Server.logmsg("connection at " + LocalIP + ":" + LocalPort + "<->" + RemoteIP + ":" + RemotePort + " disconnected.Total: " + _Server._activeConnections.Count());
        }*/
        public void Receive()
        {
            DateTime lastrecv = DateTime.Now;
            Int32 bytesread;
            int bufferlen = 8096;
            Byte[] bytesArray = new Byte[bufferlen];
            Thread.CurrentThread.Name = "Receive";
            while (_isAlive && _Socket != null)
            {
                try
                {
                    if (_Socket.Available > 0)
                    {
                        if ((bytesread = _Socket.Receive(bytesArray, 0, bufferlen, SocketFlags.None)) > 0)//readline读不到东西会阻塞，直到读到信息，或者连接断开。连接断开的返回为null
                        {
                            _Server.DisplayHandle(this, bytesArray);
                            string recvData = System.Text.Encoding.UTF8.GetString(bytesArray, 0, bytesread);
                            RecvBuffer += recvData;
                            _Server.logmsgHandle(HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + LocalIP + ":" + LocalPort + "<->" + RemoteIP + ":" + RemotePort + "] Received:" + recvData + " Buffer:" + RecvBuffer + " BufferLength:" + RecvBuffer.Length,this);
                            HandleMessage();
                            _Server.logmsgHandle(HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + LocalIP + ":" + LocalPort + "<->" + RemoteIP + ":" + RemotePort + "] After Handle, Buffer:" + RecvBuffer + " BufferLength:" + RecvBuffer.Length,this);
                        }
                        else
                        { }
                    }
                }
                catch (Exception ex) { }
                Thread.Sleep(1);
            }
            _Server.logmsgHandle(HandlerName + "[" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + LocalIP + ":" + LocalPort + "<->" + RemoteIP + ":" + RemotePort + "] disconnected. Stop Receiving thread!",this);
        }
        public void Send(Byte[] sendArray)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("Client is disposed");
            }
            try
            {
                if (_isAlive && _Socket != null)
                {
                    _Socket.Send(sendArray, 0, sendArray.Length, SocketFlags.None);
                }
            }
            catch(Exception ex)
            {
            }
            if (_disposed)
            {
                Dispose();
            }
        }
        ~ClientConnection()
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
            _disposed = true;
            _isAlive = false;
        }
    }
}
