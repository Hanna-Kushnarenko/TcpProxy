using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ServiceProcess;
using System.ComponentModel;
namespace TCPV_1
{
    public class TcpProxyService : ServiceBase
    {
        // Fields
        private IContainer components;

        public TcpProxyService()
        {
            this.InitializeComponent();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && (this.components != null))
            {
                this.components.Dispose();
            }
            base.Dispose(disposing);
        }
        private void InitializeComponent()
        {
            this.components = new Container();
            base.ServiceName = "TcpProxy";
        }

        protected override void OnStart(string[] args)
        {
            TcpProxyManager.Instance.Run(false);
        }
        protected override void OnStop()
        {
            TcpProxyManager.Instance.Stop();
        }


    }
}
