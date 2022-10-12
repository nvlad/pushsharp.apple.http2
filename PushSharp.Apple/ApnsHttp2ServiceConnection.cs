using System;
using System.Reflection;
using PushSharp.Core;
using System.Threading.Tasks;

namespace PushSharp.Apple
{
    public class ApnsHttp2ServiceConnectionFactory : IServiceConnectionFactory<ApnsHttp2Notification>
    {
        public ApnsHttp2ServiceConnectionFactory (ApnsHttp2Configuration configuration)
        {
            Configuration = configuration;
        }

        public ApnsHttp2Configuration Configuration { get; private set; }

        public IServiceConnection<ApnsHttp2Notification> Create()
        {
            return new ApnsHttp2ServiceConnection (Configuration);
        }
    }

    public class ApnsHttp2ServiceBroker : ServiceBroker<ApnsHttp2Notification>
    {
        public ApnsHttp2Configuration Configuration { get; private set; }

        public ApnsHttp2ServiceBroker (ApnsHttp2Configuration configuration) : base (new ApnsHttp2ServiceConnectionFactory (configuration))
        {
            Configuration = configuration;
        }

        public new void Stop(bool immediately = false)
        {
            // MANDATORY -> needed to stop and dispose keep alive timer
            Configuration.TryKeepingConnectionAlive = false;

            base.Stop (immediately);
        }
    }

    public class ApnsHttp2ServiceConnection : IServiceConnection<ApnsHttp2Notification>
    {   
        readonly ApnsHttp2Connection connection;

        public ApnsHttp2ServiceConnection (ApnsHttp2Configuration configuration)
        {
            connection = new ApnsHttp2Connection (configuration);
        }

        public async Task Send (ApnsHttp2Notification notification)
        {
            await connection.Send (notification);
        }
    }
}
