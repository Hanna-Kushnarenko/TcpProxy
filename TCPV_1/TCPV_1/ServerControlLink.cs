using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using TCPV_1;
using Config;

namespace TCPV_1
{
    public class ServerControlLink : IDisposable
    {
        // Fields
        private Config.ServerControlLink linkConfig_;
        private TcpListener listener_;
        private int bindTimeout_;
        private int keepAliveTimeout_;
        private DateTime lastListenTry_;
        private bool listening_;
        private List<ServerGateLink> serverGateLinks = new List<ServerGateLink>();
        private byte[] key_;
        public byte[] Key => key_;

        public void ProcessPending()
        {
            if (listening_)
            {
                while (listener_.Pending())
                {
                    try
                    {
                        Socket clientSocket = listener_.AcceptSocket();
                        IPEndPoint remoteEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
                        if (!SourcePoint.IsInside(remoteEndPoint.Address, linkConfig_.Sources))
                        {
                            Logger.Warning("Unauthorized connection to '{0}' from '{1}' closed", (object)linkConfig_.EndPoint, (object)remoteEndPoint);
                            clientSocket.Shutdown(SocketShutdown.Both);
                            clientSocket.Close();
                        }
                        else
                        {
                            ServerGateLink serverGateLink = new ServerGateLink(this, clientSocket, keepAliveTimeout_, linkConfig_.ClientsAllowed);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Can't accept socket on '{0}'", ex, (object)linkConfig_.EndPoint);
                    }
                }
            }
            else if (lastListenTry_.AddMilliseconds((double)bindTimeout_) < DateTime.UtcNow)
            {
                try
                {
                    lastListenTry_ = DateTime.UtcNow;
                    Logger.Important("Listening on '{0}' [control]", (object)linkConfig_.EndPoint);
                    try
                    {
                        listener_.Start();
                        listening_ = true;
                    }
                    catch
                    {
                    }
                    if (!listening_)
                    {
                        Thread.Sleep(300);
                        listener_.Start();
                        listening_ = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Can't listen on '{0}'", ex, (object)linkConfig_.EndPoint);
                }
            }
            foreach (ServerGateLink copyServerGateLink in GetSafeCopyServerGateLinks())
            {
                copyServerGateLink.ProcessPending();
            }
        }
        public ServerControlLink(int bindTimeout, int keepAliveTimeout, Config.ServerControlLink linkConfig)
        {
            linkConfig_ = linkConfig;
            listener_ = new TcpListener(IPAddress.Parse(linkConfig.EndPoint.IP), linkConfig.EndPoint.PortNumber);
            listener_.ExclusiveAddressUse = false;
            bindTimeout_ = bindTimeout;
            keepAliveTimeout_ = keepAliveTimeout;
            key_ = Tools.GenerateKey(linkConfig.Key);
            ProcessPending();
        }


        public void AddServerGateLink(ServerGateLink sgl)
        {
            TcpProxyManager.Instance.EnterAsyncOperation();
            Monitor.Enter((object)serverGateLinks);
            try
            {
                serverGateLinks.Add(sgl);
            }
            finally
            {
                Monitor.Exit((object)serverGateLinks);
            }
        }
        public void Dispose()
        {
            if (listening_)
            {
                Logger.Important("Stopping listening on '{0}' [control]", (object)linkConfig_.EndPoint);
                listening_ = false;
                try
                {
                    listener_.Stop();
                }
                catch
                {
                }
            }
            foreach (ServerGateLink copyServerGateLink in GetSafeCopyServerGateLinks())
                copyServerGateLink.Dispose();
        }
    
    

        private ServerGateLink[] GetSafeCopyServerGateLinks()
        {
            Monitor.Enter((object)serverGateLinks);
            try
            {
                return serverGateLinks.ToArray();
            }
            finally
            {
                Monitor.Exit((object)serverGateLinks);
            }
        }

       
        public void RemoveServerGateLink(ServerGateLink sgl)
        {
            Monitor.Enter((object)serverGateLinks);
            try
            {
                serverGateLinks.Remove(sgl);
            }
            finally
            {
                Monitor.Exit((object)serverGateLinks);
            }
            TcpProxyManager.Instance.LeaveAsyncOperation();
        }


    }
}
