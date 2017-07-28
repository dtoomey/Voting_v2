using System;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
//using System.Net.Http.Headers;
//using System.Web.Http;
using System.Fabric;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Services.Remoting.Client;
using Microsoft.ServiceFabric.Services.Client;
using VotingDataService;
using VotingService.Models;

namespace VotingService.Controllers
{
    [Route("api/[controller]")]
    public class VotesController : Controller
    {
        private const string REMOTING_URI = "fabric:/Voting/VotingDataService";
        private static IVotingDataService client = null;

        // Used for health checks.
        public static long _requestCount = 0L;

        // GET api/votes 
        [HttpGet]
        public async Task<List<Vote>> Get()
        {
            string activityId = Guid.NewGuid().ToString();
            ServiceEventSource.Current.ServiceRequestStart("VotesController.Get", activityId);

            Interlocked.Increment(ref _requestCount);

            try
            {
                IVotingDataService client = GetRemotingClient();

                var votes = await client.GetAllVoteCounts();
                return votes == null ? null : votes.Select(v => new Vote { Name = v.Key, VoteCount = v.Value }).ToList();
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Message("Error in VotesController.Get method: {0}", ex.Message);
                return null;
            }
        }

        // GET api/ballots 
        [HttpGet("ballots")]
        public async Task<string> GetTotalBallots()
        {
            string activityId = Guid.NewGuid().ToString();
            ServiceEventSource.Current.ServiceRequestStart("VotesController.GetTotalBallots", activityId);

            Interlocked.Increment(ref _requestCount);

            try
            {
                IVotingDataService client = GetRemotingClient();

                var totalBallots = await client.GetTotalBallotsCast();
                return totalBallots.ToString();
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Message("Error in VotesController.GetTotalBallots method: {0}", ex.Message);
                return "An error occurred: " + ex.Message;
            }
        }

        // GET api/appVersion 
        [HttpGet("appVersion")]
        public async Task<string> GetAppVersion()
        {
            string activityId = Guid.NewGuid().ToString();
            ServiceEventSource.Current.ServiceRequestStart("VotesController.GetAppVersion", activityId);

            Interlocked.Increment(ref _requestCount);

            string version;

            try
            {
                var applicationName = new Uri("fabric:/Voting");
                using (var client = new FabricClient())
                {
                    var applications = await client.QueryManager.GetApplicationListAsync(applicationName).ConfigureAwait(false);
                    version = applications[0].ApplicationTypeVersion;
                }

                return version;
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Message("Error in VotesController.GetAppVersion method: {0}", ex.Message);
                return "An error occurred: " + ex.Message;
            }

        }

        [HttpPost("{key}")]
        public async Task<HttpResponseMessage> Post(string key)
        {
            string activityId = Guid.NewGuid().ToString();
            ServiceEventSource.Current.ServiceRequestStart("VotesController.Post", activityId);

            Interlocked.Increment(ref _requestCount);

            IVotingDataService client = GetRemotingClient();
            await client.AddVote(key);

            ServiceEventSource.Current.ServiceRequestStop("VotesController.Post", activityId);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        [HttpDelete("{key}")]
        public async Task<HttpResponseMessage> Delete(string key)
        {
            string activityId = Guid.NewGuid().ToString();
            ServiceEventSource.Current.ServiceRequestStart("VotesController.Delete", activityId);

            Interlocked.Increment(ref _requestCount);

            IVotingDataService client = GetRemotingClient();
            await client.DeleteVoteItem(key);

            ServiceEventSource.Current.ServiceRequestStop("VotesController.Delete", activityId);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        [Produces("text/html")]
        [HttpGet("{file}")]
        public string GetFile(string file)
        {
            string activityId = Guid.NewGuid().ToString();
            ServiceEventSource.Current.ServiceRequestStart("VotesController.GetFile", activityId);

            string response = null;
            string responseType = "text/html";

            Interlocked.Increment(ref _requestCount);

            // Validate file name.
            if ("index.html" == file)
            {
                string path = string.Format(@"..\VotingServicePkg.Code.1.0.0\{0}", file);
                response = System.IO.File.ReadAllText(path);
            }

            return response;
        }

        private static IVotingDataService GetRemotingClient()
        {
            if (client == null)
            {
                var resolver = ServicePartitionResolver.GetDefault();
                var partKey = new ServicePartitionKey(1);
                var partition = resolver.ResolveAsync(new Uri(REMOTING_URI), partKey, new CancellationToken());
                client = ServiceProxy.Create<IVotingDataService>(new Uri(REMOTING_URI), partKey);
            }
            return client;
        }


    }
}