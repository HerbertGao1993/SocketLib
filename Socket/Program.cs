using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.IO;

namespace SocketLib
{
    class Program
    {
        public static FileStream fs = null;
        public static StreamWriter sr = null;
        internal static TcpServerBase server = new TcpServerBase(IPAddress.Any.ToString(), 2112);
        internal static TcpClientBase client = new TcpClientBase("192.168.1.231", 2112);
        static void Main(string[] args)
        {
            try
            {
                
                Thread.CurrentThread.Name = "Main";
                server.OnExpectedData += OnExpectedData;
                server.Start();
                //client.OnExpectedData += OnExpectedData;
                //client.Start();
                while (true)
                {
                    //if (!client.isalive)
                    //{
                        //client = new TcpClient("192.168.0.231", 2112);
                        //client.Start();
                    //}
                    if (!server.Listening)
                    {
                        server.Start();
                    }
                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
            finally
            {
                if (fs != null)
                {
                    fs.Dispose();
                }
                if (sr != null)
                {
                    sr.Dispose();
                }

            }
            
        }
        static void OnExpectedData(TcpClientBase client, string currentmessage)
        {
            
            try
            {
                fs = new FileStream("D:\\log.txt", FileMode.Append, FileAccess.Write);
                sr = new StreamWriter(fs);
                sr.WriteLine("[" + DateTime.Now.AddMilliseconds(0).ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + client.LocalIP + ":" + client.LocalPort + "<->" + client.RemoteIP + ":" + client.RemotePort + "] Expected: " + currentmessage + " CurrentThread: " + Thread.CurrentThread.Name);
                sr.Close();
                fs.Close();
            }
            catch (Exception ex) { }
            finally
            {
                if (fs != null)
                {
                    fs.Dispose();
                }
                if (sr != null)
                {
                    sr.Dispose();
                }

            }
        }
        static void OnExpectedData(string currentmessage, ClientConnection connection)
        {
            try
            {
                fs = new FileStream("D:\\log.txt", FileMode.Append, FileAccess.Write);
                sr = new StreamWriter(fs);
                sr.WriteLine("[" + DateTime.Now.AddMilliseconds(0).ToString("yyyy-MM-dd_HH:mm:ss:fff") + "][" + connection.LocalIP + ":" + connection.LocalPort + "<->" + connection.RemoteIP + ":" + connection.RemotePort + "] Expected: " + currentmessage + " CurrentThread: " + Thread.CurrentThread.Name);
                sr.Close();
                fs.Close();
            }
            catch (Exception ex) { }
            finally
            {
                if (fs != null)
                {
                    fs.Dispose();
                }
                if (sr != null)
                {
                    sr.Dispose();
                }

            }
        }
    }
}
