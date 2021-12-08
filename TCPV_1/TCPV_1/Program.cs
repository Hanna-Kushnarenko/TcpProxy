using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace TCPV_1
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "/console")
            {
                Console.CancelKeyPress += new ConsoleCancelEventHandler(Program.BreakProcess);
                TcpProxyManager.Instance.Run(true);
            }
            else
                ServiceBase.Run(new ServiceBase[1]
                {
          (ServiceBase) new TcpProxyService()
                });
        }

        private static void BreakProcess(object sender, ConsoleCancelEventArgs args) => TcpProxyManager.Instance.Stop();
    }
}
