using System;
using System.Collections.Generic;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using System.Fabric.Health;
using VotingService.Controllers;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace VotingService
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    internal sealed class VotingService : StatelessService
    {
        public VotingService(StatelessServiceContext context)
            : base(context)
        {
            // Create the timer here, so we can do a change operation on it later, avoiding creating/disposing of the 
            // timer.
            _healthTimer = new Timer(ReportHealthAndLoad, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Optional override to create listeners (like tcp, http) for this service instance.
        /// </summary>
        /// <returns>The collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[]
            {
                new ServiceInstanceListener(serviceContext =>
                    new WebListenerCommunicationListener(serviceContext, "ServiceEndpoint", (url, listener) =>
                    {
                        ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting WebListener on {url}");

                        return new WebHostBuilder().UseWebListener()
                                    .ConfigureServices(
                                        services => services
                                            .AddSingleton<StatelessServiceContext>(serviceContext))
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.None)
                                    .UseUrls(url)
                                    .Build();
                    }))
            };
        }


        private TimeSpan _interval = TimeSpan.FromSeconds(30);
        private long _lastCount = 0L;
        private DateTime _lastReport = DateTime.UtcNow;
        private Timer _healthTimer = null;
        private FabricClient _client = null;

        protected override Task OnOpenAsync(CancellationToken cancellationToken)
        {
            _client = new FabricClient();
            _healthTimer = new Timer(ReportHealthAndLoad, null, _interval, _interval);
            return base.OnOpenAsync(cancellationToken);
        }

        public void ReportHealthAndLoad(object notused)
        {
            // Calculate the values and then remember current values for the next report.
            long total = VotesController._requestCount;
            long diff = total - _lastCount;
            long duration = Math.Max((long)DateTime.UtcNow.Subtract(_lastReport).TotalSeconds, 1L);
            long rps = diff / duration;
            _lastCount = total;
            _lastReport = DateTime.UtcNow;

            // Create the health information for this instance of the service and send report to Service Fabric.
            HealthInformation hi = new HealthInformation("VotingServiceHealth", "Heartbeat", HealthState.Ok)
            {
                TimeToLive = _interval.Add(_interval),
                Description = $"{diff} requests since last report. RPS: {rps} Total requests: {total}.",
                RemoveWhenExpired = false,
                SequenceNumber = HealthInformation.AutoSequenceNumber
            };
            var sshr = new StatelessServiceInstanceHealthReport(Context.PartitionId, Context.InstanceId, hi);
            _client.HealthManager.ReportHealth(sshr);

            // Report the load
            Partition.ReportLoad(new[] { new LoadMetric("RPS", (int)rps) });
        }
    }
}
