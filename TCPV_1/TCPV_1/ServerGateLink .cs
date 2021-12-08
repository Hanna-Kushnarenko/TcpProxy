using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Threading;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft;
using TCPV_1;
using Config;

namespace TCPV_1
{
    public class ServerGateLink : CryptoLink, IDisposable
    {
        // Fields
        private ServerControlLink controlLink_;
        private Socket clientControlSocket_;
        private string clientControlAddress_;
        private TcpListener listener_;
        private int keepAliveTimeout_ = 60000;
        private DateTime lastAliveChecked_ = DateTime.MinValue;
        private bool isDisposed_;
        private List<SourcePoint> clientsAllowed_;
        private RijndaelManaged rijndael_;
        private static Mutex clientGateMutex_ = new Mutex();
        private static Socket clientSocket_ = (Socket)null;
        private static DateTime connectPendingUntil_ = DateTime.MinValue;

        public ServerGateLink(
     ServerControlLink controlLink,
     Socket clientSocket,
     int keepAliveTimeout,
     List<SourcePoint> clientsAllowed)
        {
            controlLink_ = controlLink;
            clientControlSocket_ = clientSocket;
            clientControlAddress_ = clientControlSocket_.RemoteEndPoint.ToString();
            keepAliveTimeout_ = keepAliveTimeout;
            clientsAllowed_ = clientsAllowed;
            if (controlLink.Key != null)
            {
                rijndael_ = new RijndaelManaged();
                rijndael_.Padding = PaddingMode.None;
                rijndael_.GenerateIV();
                rijndael_.Key = controlLink.Key;
                Decryptor = rijndael_.CreateDecryptor();
                Encryptor = rijndael_.CreateEncryptor();
            }
            StartAuthentication();
        }

        public void ProcessPending()
        {
            if (CheckPendingGateConnect())
                return;
            if (listener_ != null)
            {
                try
                {
                    if (listener_.Pending())
                    {
                        Socket cs = listener_.AcceptSocket();
                        IPEndPoint remoteEndPoint = cs.RemoteEndPoint as IPEndPoint;
                        if (SourcePoint.IsInside(((IPEndPoint)cs.RemoteEndPoint).Address, clientsAllowed_))
                        {
                            StartGateConnection(cs);
                        }
                        else
                        {
                            Logger.Warning("Unauthorized connection to '{0}' from '{1}' closed", (object)clientControlAddress_, (object)remoteEndPoint);
                            cs.Shutdown(SocketShutdown.Both);
                            cs.Close();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Can't accept socket on '{0}'", ex, (object)listener_.LocalEndpoint);
                }
            }
            if (CheckPendingGateConnect())
                return;
            CheckConnected();
        }

        private void CheckConnected()
        {
            if (!(lastAliveChecked_.AddMilliseconds((double)(keepAliveTimeout_ / 2)) < DateTime.UtcNow))
                return;
            lastAliveChecked_ = DateTime.UtcNow;
            try
            {
                byte[] buffer = Encrypt("ping");
                TcpProxyManager.Instance.EnterAsyncOperation();
                clientControlSocket_.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(PingSent), (object)this);
            }
            catch (Exception ex)
            {
                HandleError("Connection from '{0}' closed", ex, (object)clientControlAddress_);
            }
        }

        private void StartAuthentication()
        {
            TcpProxyManager.Instance.EnterAsyncOperation();
            byte[] buffer = new byte[16];
            if (rijndael_ != null)
                buffer = rijndael_.IV;
            clientControlSocket_.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(AuthenticationSent), (object)this);
        }

        private void AuthenticationSent(IAsyncResult ar)
        {
            if (isDisposed_)
                return;
            try
            {
                try
                {
                    clientControlSocket_.EndSend(ar);
                }
                catch (Exception ex)
                {
                    HandleError("Can't send IV to '{0}'", ex, (object)clientControlAddress_);
                    return;
                }
                string handle = string.Empty;
                Config.EndPoint endPoint = new Config.EndPoint();
                bool flag;
                try
                {
                    string[] strArray = ReceiveAndDecrypt(clientControlSocket_).Split(' ');
                    if (strArray.Length == 3 && strArray[0] == "listen")
                    {
                        flag = true;
                        endPoint.IP = strArray[1];
                        endPoint.PortNumber = int.Parse(strArray[2]);
                    }
                    else
                    {
                        handle = strArray.Length == 2 && strArray[0] == "establish" ? strArray[1] : throw new ApplicationException("Incorrect answer received");
                        flag = false;
                    }
                }
                catch (Exception ex)
                {
                    HandleError("Authentication failed for '{0}'", ex, (object)clientControlAddress_);
                    return;
                }
                if (flag)
                    StartListen(endPoint);
                else
                    EstablishGateLink(handle);
            }
            finally
            {
                TcpProxyManager.Instance.LeaveAsyncOperation();
            }
        }

        private void EstablishGateLink(string handle)
        {
            Socket socket = (Socket)null;
            ServerGateLink.clientGateMutex_.WaitOne();
            try
            {
                if (ServerGateLink.clientSocket_ != null && ServerGateLink.clientSocket_.Handle.ToString() == handle)
                {
                    socket = ServerGateLink.clientSocket_;
                    ServerGateLink.clientSocket_ = (Socket)null;
                }
                else
                {
                    DisposeGateClientSocket();
                    HandleError("Gate link connection for handle '{0}' timed out", (Exception)null, (object)handle);
                    return;
                }
            }
            finally
            {
                ServerGateLink.clientGateMutex_.ReleaseMutex();
            }
            try
            {
                EncryptAndSend("ok", clientControlSocket_);
                SocketPair socketPair = new SocketPair(socket, socket.RemoteEndPoint.ToString(), clientControlSocket_, clientControlAddress_);
                clientControlSocket_ = (Socket)null;
                socketPair.StartPairWork();
            }
            catch (Exception ex)
            {
                HandleError("Gate link connection for '{0}' failed", ex, (object)Tools.GetSafeRemoteEndPoint(socket));
                try
                {
                    if (socket.Connected)
                        socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
                catch
                {
                }
            }
        }

        private void StartListen(Config.EndPoint endPoint)
        {
            Logger.Important("Remote command from '{0}'. Starting listening on '{1}'", (object)clientControlAddress_, (object)endPoint);
            try
            {
                listener_ = new TcpListener(IPAddress.Parse(endPoint.IP), endPoint.PortNumber);
                listener_.Start();
                EncryptAndSend("ok", clientControlSocket_);
            }
            catch (Exception ex1)
            {
                string str = string.Format("Can't listen on '{0}'. Details: {1}", (object)endPoint, (object)ex1.ToString());
                try
                {
                    EncryptAndSend(str, clientControlSocket_);
                }
                catch (Exception ex2)
                {
                    HandleError("Can't send answer to '{0}'", ex2, (object)clientControlAddress_);
                    return;
                }
                HandleError(str, (Exception)null);
                return;
            }
            controlLink_.AddServerGateLink(this);
            ProcessPending();
        }

        private void StartGateConnection(Socket cs)
        {
            try
            {
                byte[] buffer = Encrypt("connect " + cs.Handle.ToString() + " " + ((IPEndPoint)cs.RemoteEndPoint).Address.ToString());
                ServerGateLink.clientGateMutex_.WaitOne();
                try
                {
                    ServerGateLink.clientSocket_ = cs;
                    ServerGateLink.connectPendingUntil_ = DateTime.UtcNow.AddMilliseconds((double)keepAliveTimeout_);
                    TcpProxyManager.Instance.EnterAsyncOperation();
                    clientControlSocket_.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(ClientConnectedSent), (object)cs);
                }
                finally
                {
                    ServerGateLink.clientGateMutex_.ReleaseMutex();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Can't establish gate link from '{0}' for client control '{1}'", ex, (object)Tools.GetSafeRemoteEndPoint(cs), (object)clientControlAddress_);
                DisposeGateClientSocket();
            }
        }

        private bool CheckPendingGateConnect()
        {
            ServerGateLink.clientGateMutex_.WaitOne();
            try
            {
                if (ServerGateLink.clientSocket_ == null)
                    return false;
                if (!(ServerGateLink.connectPendingUntil_ < DateTime.UtcNow))
                    return true;
                DisposeGateClientSocket();
                return false;
            }
            finally
            {
                ServerGateLink.clientGateMutex_.ReleaseMutex();
            }
        }

        private void DisposeGateClientSocket()
        {
            ServerGateLink.clientGateMutex_.WaitOne();
            try
            {
                if (ServerGateLink.clientSocket_ == null)
                    return;
                try
                {
                    if (ServerGateLink.clientSocket_.Connected)
                        ServerGateLink.clientSocket_.Shutdown(SocketShutdown.Both);
                    ServerGateLink.clientSocket_.Close();
                }
                catch
                {
                }
                ServerGateLink.clientSocket_ = (Socket)null;
            }
            finally
            {
                ServerGateLink.clientGateMutex_.ReleaseMutex();
            }
        }

        private void ClientConnectedSent(IAsyncResult ar)
        {
            if (isDisposed_)
                return;
            Socket asyncState = (Socket)ar.AsyncState;
            string str = "";
            try
            {
                str = Tools.GetSafeRemoteEndPoint(asyncState);
                clientControlSocket_.EndSend(ar);
                string andDecrypt = ReceiveAndDecrypt(clientControlSocket_);
                if (andDecrypt != "ok")
                    throw new ApplicationException("Failed with message: " + andDecrypt);
            }
            catch (Exception ex)
            {
                Logger.Error("Can't establish gate link from '{0}' for client control '{1}'", ex, (object)str, (object)clientControlAddress_);
                DisposeGateClientSocket();
            }
            TcpProxyManager.Instance.LeaveAsyncOperation();
        }

        private void HandleError(string Message, Exception ex, params object[] args)
        {
            Logger.Error(Message, ex, args);
            Dispose();
        }

        private void PingSent(IAsyncResult ar)
        {
            if (isDisposed_)
                return;
            try
            {
                try
                {
                    clientControlSocket_.EndSend(ar);
                }
                catch (Exception ex)
                {
                    HandleError("Can't send ping to '{0}'", ex, (object)clientControlAddress_);
                    return;
                }
                try
                {
                    if (ReceiveAndDecrypt(clientControlSocket_) != "pong")
                        throw new ApplicationException("Incorrect answer received");
                }
                catch (Exception ex)
                {
                    HandleError("RemoteGate client disconnected '{0}'", ex, (object)clientControlAddress_);
                }
            }
            finally
            {
                TcpProxyManager.Instance.LeaveAsyncOperation();
            }
        }

        public void Dispose()
        {
            isDisposed_ = true;
            if (clientControlSocket_ != null)
            {
                try
                {
                    if (clientControlSocket_.Connected)
                        clientControlSocket_.Shutdown(SocketShutdown.Both);
                    clientControlSocket_.Close();
                }
                catch
                {
                }
            }
            if (listener_ != null)
            {
                Logger.Important("Stopping listening on '{0}' [gate]", (object)listener_.LocalEndpoint);
                try
                {
                    listener_.Stop();
                }
                catch
                {
                }
            }
            controlLink_.RemoveServerGateLink(this);
        }
    }
}
