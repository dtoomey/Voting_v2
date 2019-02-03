using System.Collections.Generic;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Data;
using System.Fabric.Health;
using System;

namespace VotingDataService
{
    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    public sealed class VotingDataService : StatefulService, IVotingDataService
    {
        IReliableDictionary<string, int> voteDictionary = null;  //NEW
        IReliableDictionary<string, long> ballotDictionary = null;
        public const string BALLOTS_CAST_KEY = "TotalBallotsCast";

        public VotingDataService(StatefulServiceContext context)
            : base(context)
        { }

        // NEW
        public async Task<int> AddVote(string voteItem)
        {
            ServiceEventSource.Current.ServiceMessage(this.Context, "VotingDataService.AddVote start. voteItem='{0}'", voteItem);
            int result = 0;
            long result2 = 0;

            using (ITransaction tx = StateManager.CreateTransaction())
            {
                result = await voteDictionary.AddOrUpdateAsync(tx, voteItem, 1, (key, value) => ++value);
                result2 = await ballotDictionary.AddOrUpdateAsync(tx, BALLOTS_CAST_KEY, 1, (key, value) => ++value);

                //Uncomment to introduce a dodgy bug
                //if (!string.IsNullOrEmpty(voteItem) && voteItem.ToUpper().StartsWith("TRUMP")) await voteDictionary.AddOrUpdateAsync(tx, voteItem, 1, (key, value) => value += 10);

                await tx.CommitAsync();
            }

            ServiceEventSource.Current.ServiceMessage(this.Context, "VotingDataService.AddVote end. Total votes: {0}", result.ToString());
            return result;
        }

        public async Task<int> DeleteVoteItem(string voteItem)
        {
            ServiceEventSource.Current.ServiceMessage(this.Context, "VotingDataService.DeleteVoteItem start.");
            ConditionalValue<int> result = new ConditionalValue<int>(false, -1);

            using (ITransaction tx = StateManager.CreateTransaction())
            {
                if (voteDictionary != null)
                {
                    ConditionalValue<int> deleteVotes = await voteDictionary.TryGetValueAsync(tx, voteItem);
                    result = await voteDictionary.TryRemoveAsync(tx, voteItem);
                    await ballotDictionary.AddOrUpdateAsync(tx, BALLOTS_CAST_KEY, -1, (key, value) => (value >= deleteVotes.Value ? value - deleteVotes.Value : 0));
                    await tx.CommitAsync();
                }
            }
            ServiceEventSource.Current.ServiceMessage(this.Context, "VotingDataService.DeleteVoteItem end.");
            return result.HasValue ? result.Value : -1;
        }

        public async Task<int> GetNumberOfVotes(string voteItem)
        {
            ServiceEventSource.Current.ServiceMessage(this.Context, "VotingDataService.GetNumberOfVotes start.");
            ConditionalValue<int> result = new ConditionalValue<int>(true, 0);

            using (ITransaction tx = StateManager.CreateTransaction())
            {
                if (voteDictionary != null)
                {
                    result = await voteDictionary.TryGetValueAsync(tx, voteItem);
                    await tx.CommitAsync();
                }
            }

            ServiceEventSource.Current.ServiceMessage(this.Context, "VotingDataService.GetNumberOfVotes end.");
            return result.HasValue ? result.Value : 0;
        }

        public async Task<long> GetTotalBallotsCast()
        {
            ServiceEventSource.Current.ServiceMessage(this.Context, "VotingDataService.GetTotalBallotsCast start.");
            ConditionalValue<long> result = new ConditionalValue<long>(true, 0);

            using (ITransaction tx = StateManager.CreateTransaction())
            {
                if (ballotDictionary != null)
                {
                    result = await ballotDictionary.TryGetValueAsync(tx, BALLOTS_CAST_KEY);
                    await tx.CommitAsync();
                }
            }

            ServiceEventSource.Current.ServiceMessage(this.Context, "VotingDataService.GetTotalBallotsCast end.");
            return result.HasValue ? result.Value : 0;
        }

        public async Task<List<KeyValuePair<string, int>>> GetAllVoteCounts()
        {
            ServiceEventSource.Current.Message("VotingDataService.GetAllVoteCounts start.");
            List<KeyValuePair<string, int>> kvps = new List<KeyValuePair<string, int>>();

            using (ITransaction tx = StateManager.CreateTransaction())
            {
                if (voteDictionary != null)
                {
                    IAsyncEnumerable<KeyValuePair<string, int>> e = await voteDictionary.CreateEnumerableAsync(tx);
                    IAsyncEnumerator<KeyValuePair<string, int>> items = e.GetAsyncEnumerator();

                    while (await items.MoveNextAsync(new CancellationToken()))
                    {
                        kvps.Add(new KeyValuePair<string, int>(items.Current.Key, items.Current.Value));
                    }

                    //kvps.Sort((x, y) => x.Value.CompareTo(y.Value) * -1);  // intentionally commented out!
                }
                await tx.CommitAsync();
            }

            ServiceEventSource.Current.Message("VotingDataService.GetAllVoteCounts end. Number of keys: {0}", kvps.Count.ToString());
            return kvps;
        }

        private async void CheckVotesIntegrity()
        {
            long totalVotesAcrossItems = 0;
            long totalBallotsCast = 0;
            HealthReportSendOptions sendOptions = new HealthReportSendOptions() { Immediate = true };

            using (ITransaction tx = StateManager.CreateTransaction())
            {
                totalBallotsCast = await GetTotalBallotsCast();
                var voteItems = await GetAllVoteCounts();

                foreach (var item in voteItems)
                {
                    totalVotesAcrossItems += item.Value;
                }

                if (totalBallotsCast != totalVotesAcrossItems)
                {
                    HealthInformation healthInformation = new HealthInformation("ServiceCode", "StateDictionary", HealthState.Error);
                    healthInformation.Description = string.Format("Total votes across items [{0}] does not equal total ballots cast [{1}].", totalVotesAcrossItems.ToString(), totalBallotsCast.ToString());
                    //healthInformation.TimeToLive = TimeSpan.FromSeconds(15);
                    this.Partition.ReportReplicaHealth(healthInformation, sendOptions);
                }
                else
                {
                    HealthInformation healthInformation = new HealthInformation("ServiceCode", "StateDictionary", HealthState.Ok);
                    this.Partition.ReportReplicaHealth(healthInformation, sendOptions);
                }

                await tx.CommitAsync();
            }
        }

        /// <summary>
        /// Optional override to create listeners (e.g., HTTP, Service Remoting, WCF, etc.) for this service replica to handle client or user requests.
        /// </summary>
        /// <remarks>
        /// For more information on service communication, see https://aka.ms/servicefabricservicecommunication
        /// </remarks>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            ServiceEventSource.Current.ServiceMessage(this.Context, "VotingDataService.CreateServiceReplicaListeners start.");
            ServiceEventSource.Current.ServiceMessage(this.Context, "VotingDataService.CreateServiceReplicaListeners end.");
            return new[] { new ServiceReplicaListener(context => this.CreateServiceRemotingListener(context)) };
        }

        /// <summary>
        /// This is the main entry point for your service replica.
        /// This method executes when this replica of your service becomes primary and has write status.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service replica.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            // TODO: Replace the following sample code with your own logic 
            //       or remove this RunAsync override if it's not needed in your service.

            voteDictionary = await StateManager.GetOrAddAsync<IReliableDictionary<string, int>>("voteDictionary");
            ballotDictionary = await StateManager.GetOrAddAsync<IReliableDictionary<string, long>>("ballotDictionary");

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                CheckVotesIntegrity();

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }
}
