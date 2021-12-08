using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Config
{
    public class RemoteGateLink
    {
        public ServiceAddress GateControlAddress { get; set; }

        public EndPoint GateEndPoint { get; set; }

        public string Key { get; set; }

        public ServiceAddress ServiceAddress { get; set; }

        public List<SourcePoint> Sources { get; set; }

        public RemoteGateLink()
        {
            this.GateControlAddress = new ServiceAddress();
            this.GateEndPoint = new EndPoint();
            this.Key = string.Empty;
            this.ServiceAddress = new ServiceAddress();
            this.Sources = new List<SourcePoint>();
        }
    }
}
